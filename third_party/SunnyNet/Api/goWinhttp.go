package Api

import (
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/Certificate"
	"github.com/qtgolang/SunnyNet/src/GoWinHttp"
	"io"
	mrand "math/rand"
	"net/http"
	"net/url"
	"sync"
)

// ---------------------------------------------
type h struct {
	*GoWinHttp.WinHttp
	Error error
	Lock  sync.Mutex
	Body  []byte
	Heads []byte
	Resp  *http.Response
}

var HTTPMap = make(map[int]*h)
var HTTPMapLock sync.Mutex

func LoadHTTPClient(Context int) *h {
	HTTPMapLock.Lock()
	s := HTTPMap[Context]
	HTTPMapLock.Unlock()
	if s == nil {
		return nil
	}
	return s
}

// 创建 HTTP 客户端
//
//export CreateHTTPClient
func CreateHTTPClient() int {
	HTTPMapLock.Lock()
	HTTPMapLock.Unlock()
	Context := newMessageId()
	HTTPMapLock.Lock()
	HTTPMap[Context] = &h{WinHttp: GoWinHttp.NewGoWinHttp()}
	HTTPMapLock.Unlock()
	return Context
}

// RemoveHTTPClient
// 释放 HTTP客户端
func RemoveHTTPClient(Context int) {
	HTTPMapLock.Lock()
	defer HTTPMapLock.Unlock()
	delete(HTTPMap, Context)
}

// HTTPClientGetErr
// HTTP 客户端 取错误
func HTTPClientGetErr(Context int) uintptr {
	k := LoadHTTPClient(Context)
	if k != nil {
		k.Lock.Lock()
		defer k.Lock.Unlock()
		if k.Error != nil {
			return public.PointerPtr(k.Error.Error())
		}
	}
	return 0
}

// HTTPOpen
// HTTP 客户端 Open
func HTTPOpen(Context int, Method, URL string) {
	k := LoadHTTPClient(Context)
	if k == nil {
		return
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	k.Open(Method, URL)
}

// HTTPSetHeader
// HTTP 客户端 设置协议头
func HTTPSetHeader(Context int, name, value string) {
	k := LoadHTTPClient(Context)
	if k == nil {
		return
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	k.SetHeader(name, value)
}

// HTTPSetProxyIP
// HTTP 客户端 设置代理IP 127.0.0.1:8888
func HTTPSetProxyIP(Context int, ProxyUrl string) bool {
	k := LoadHTTPClient(Context)
	if k == nil {
		return false
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	proxy, err := url.Parse(ProxyUrl)
	if err != nil || proxy == nil {
		return false
	}
	if proxy.Scheme != "http" && proxy.Scheme != "socket5" && proxy.Scheme != "socket" && proxy.Scheme != "socks5" {
		return false
	}
	if len(proxy.Host) < 3 {
		return false
	}
	k.SetProxyType(proxy.Scheme != "http")
	k.SetProxyIP(proxy.Host)
	PWD, _ := proxy.User.Password()
	k.SetProxyUser(proxy.User.Username(), PWD)
	return true
}

// HTTPSetTimeouts
// HTTP 客户端 设置超时 毫秒
func HTTPSetTimeouts(Context int, t1, t2, t3 int) {
	k := LoadHTTPClient(Context)
	if k == nil {
		return
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	k.SetOutTime(t2, t3, t1)
}

// HTTPSendBin
// HTTP 客户端 发送Body
func HTTPSendBin(Context int, b uintptr, l int) {
	data := public.CStringToBytes(b, l)
	k := LoadHTTPClient(Context)
	if k == nil {
		return
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	k.Resp, k.Error = k.Send(data)
	defer func() {
		if k != nil {
			if k.Resp != nil {
				if k.Resp.Body != nil {
					_ = k.Resp.Body.Close()
				}
			}
			k.Save()
		}
	}()
	var B string
	if k.Resp != nil {
		if k.Resp.Body != nil {
			i, _ := io.ReadAll(k.Resp.Body)
			k.Body = i
			_ = k.Resp.Body.Close()
		}
		for name, values := range k.Resp.Header {
			for _, value := range values {
				B += name + ": " + value + "\r\n"
			}
		}
	}
	k.Heads = []byte(B)
}

// HTTPGetBodyLen
// HTTP 客户端 返回响应长度
func HTTPGetBodyLen(Context int) int {
	k := LoadHTTPClient(Context)
	if k == nil {
		return 0
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	if k.Body == nil {
		return 0
	}
	return len(k.Body)
}

// HTTPGetHeads
// HTTP 客户端 返回响应全部Heads
func HTTPGetHeads(Context int) uintptr {
	k := LoadHTTPClient(Context)
	if k == nil {
		return 0
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	if k.Heads == nil {
		return 0
	}
	if len(k.Heads) < 1 {
		return 0
	}
	return public.PointerPtr(k.Heads)
}

// HTTPGetBody
// HTTP 客户端 返回响应内容
func HTTPGetBody(Context int) uintptr {
	k := LoadHTTPClient(Context)
	if k == nil {
		return 0
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	if k.Body == nil {
		return 0
	}
	if len(k.Body) < 1 {
		return 0
	}
	return public.PointerPtr(k.Body)
}

// HTTPGetCode
// HTTP 客户端 返回响应状态码
func HTTPGetCode(Context int) int {
	k := LoadHTTPClient(Context)
	if k == nil {
		return 0
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	if k.Resp == nil {
		return 0
	}
	return k.Resp.StatusCode
}

// HTTPSetCertManager
// HTTP 客户端 设置证书管理器
func HTTPSetCertManager(Context, CertManagerContext int) bool {
	k := LoadHTTPClient(Context)
	if k == nil {
		return false
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()

	Certificate.Lock.Lock()
	defer Certificate.Lock.Unlock()
	c := Certificate.LoadCertificateContext(CertManagerContext)
	if c == nil {
		return false
	}
	if c.Tls == nil {
		return false
	}
	k.SetTlsConfig(c.Tls)
	return true
}

// HTTPSetRedirect
// HTTP 客户端 设置重定向
func HTTPSetRedirect(Context int, Redirect bool) bool {
	k := LoadHTTPClient(Context)
	if k == nil {
		return false
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	k.SetRedirect(Redirect)
	return true
}

// HTTPSetRandomTLS
// HTTP 客户端 设置随机使用TLS指纹
func HTTPSetRandomTLS(Context int, Open bool) bool {
	k := LoadHTTPClient(Context)
	if k == nil {
		return false
	}
	k.Lock.Lock()
	defer k.Lock.Unlock()
	if Open {
		k.GetTLSValues = GetTLSValues
	} else {
		k.GetTLSValues = nil
	}
	return true
}

var _httpRandomTLSValue []uint16

func init() {
	_httpRandomTLSValue = make([]uint16, public.RandomTLSValueArrayLen)
	copy(_httpRandomTLSValue, public.RandomTLSValueArray)
}
func GetTLSValues() []uint16 {
	HTTPMapLock.Lock()
	defer HTTPMapLock.Unlock()
	n := mrand.Intn(public.RandomTLSValueArrayLen) + 1
	for i := public.RandomTLSValueArrayLen - 1; i > 0; i-- {
		j := mrand.Intn(i + 1)
		_httpRandomTLSValue[i], _httpRandomTLSValue[j] = _httpRandomTLSValue[j], _httpRandomTLSValue[i]
	}
	shuffledArray := make([]uint16, n)
	copy(shuffledArray, _httpRandomTLSValue[:n])
	return shuffledArray
}
