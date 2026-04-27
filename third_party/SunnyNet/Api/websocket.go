package Api

import (
	"crypto/tls"
	"errors"
	"github.com/qtgolang/SunnyNet/Call"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/Certificate"
	"github.com/qtgolang/SunnyNet/src/websocket"
	"net/http"
	"net/textproto"
	"net/url"
	"strings"
	"sync"
	"time"
)

var WebSocketMap = make(map[int]interface{})
var WebSocketMapLock sync.Mutex

type WebsocketClient struct {
	err         error
	wb          *websocket.Conn
	call        int
	Context     int
	synchronous bool
	l           sync.Mutex
}

func LoadWebSocketContext(Context int) *WebsocketClient {
	WebSocketMapLock.Lock()
	s := WebSocketMap[Context]
	WebSocketMapLock.Unlock()
	if s == nil {
		return nil
	}
	return s.(*WebsocketClient)
}

// CreateWebsocket
// 创建 Websocket客户端 对象
func CreateWebsocket() int {
	w := &WebsocketClient{}
	Context := newMessageId()
	w.Context = Context
	WebSocketMapLock.Lock()
	WebSocketMap[Context] = w
	WebSocketMapLock.Unlock()
	return Context
}
func DelWebSocketContext(Context int) {
	WebSocketMapLock.Lock()
	delete(WebSocketMap, Context)
	WebSocketMapLock.Unlock()
}

// RemoveWebsocket
// 释放 Websocket客户端 对象
func RemoveWebsocket(Context int) {
	k := LoadWebSocketContext(Context)
	if k != nil {
		if k.wb != nil {
			_ = k.wb.Close()
		}
	}
	DelWebSocketContext(Context)
}

// WebsocketGetErr
// Websocket客户端 获取错误
func WebsocketGetErr(Context int) uintptr {
	k := LoadWebSocketContext(Context)
	if k != nil {
		if k.err == nil {
			return 0
		}
		if k.err != nil {
			return public.PointerPtr(k.err.Error())
		}

	}
	return 0
}

// WebsocketDial
// Websocket客户端 连接
func WebsocketDial(Context int, URL, Heads string, call int, synchronous bool, ProxyUrl string, CertificateConText int, outTime int) bool {
	w := LoadWebSocketContext(Context)
	if w == nil {
		return false
	}
	w.l.Lock()
	defer w.l.Unlock()
	w.call = call
	w.err = nil
	head := strings.ReplaceAll(Heads, "\r", "")
	var dialer websocket.Dialer
	//Request, _ := http.NewRequest("GET", strings.Replace(URL, "wss://", "https://", 1), nil)
	Header := make(http.Header)
	arr := strings.Split(head, "\n")
	for _, v := range arr {
		arr1 := strings.Split(v, ":")
		if len(arr1) >= 2 {
			k := arr1[0]
			val := strings.TrimSpace(strings.Replace(v, arr1[0]+":", "", 1))
			Header[textproto.TrimString(k)] = []string{val}
		}
	}
	mUrl := strings.ToLower(URL)
	if strings.HasPrefix(mUrl, "https") || strings.HasPrefix(mUrl, "wss") {
		var t *tls.Config
		Certificate.Lock.Lock()
		fig := Certificate.LoadCertificateContext(CertificateConText)
		Certificate.Lock.Unlock()
		if fig != nil {
			if fig.Tls != nil {
				t = fig.Tls
			} else {
				t = &tls.Config{InsecureSkipVerify: true}
			}
		} else {
			t = &tls.Config{InsecureSkipVerify: true}
		}
		dialer = websocket.Dialer{TLSClientConfig: t}
	} else {
		dialer = websocket.Dialer{}
	}
	w.synchronous = synchronous
	Proxy_ := ProxyUrl
	a, _ := url.Parse(Proxy_)
	if len(a.Host) < 3 {
		Proxy_ = ""
	}
	//w.wb, _, w.err = dialer.Dial(Request.URL.String(), Request.Header, Proxy_)
	//w.wb, _, w.err = dialer.ConnDialContext(Request, Proxy_)
	w.wb, _, w.err = dialer.Dial(URL, Header, Proxy_, outTime)
	if w.err != nil {
		return false
	}
	if w.synchronous == false {
		go w.WebsocketRead()
	}
	return true
}

// WebsocketClose
// Websocket客户端 断开
func WebsocketClose(Context int) {
	w := LoadWebSocketContext(Context)
	if w == nil {
		return
	}
	w.l.Lock()
	defer w.l.Unlock()
	if w.wb != nil {
		_ = w.wb.Close()
	}
}

// WebsocketReadWrite
// Websocket客户端  发送数据
func WebsocketReadWrite(Context int, val uintptr, valLen int, messageType int) bool {
	data := public.CStringToBytes(val, valLen)
	w := LoadWebSocketContext(Context)
	if w == nil {
		return false
	}
	w.l.Lock()
	defer w.l.Unlock()
	i := messageType
	if i != 1 && i != 2 && i != 8 && i != 9 && i != 10 {
		/*
			TextMessage = 1
			BinaryMessage = 2
			CloseMessage = 8
			PingMessage = 9
			PongMessage = 10
		*/
		i = 1
	}
	if w.wb == nil {
		return false
	}
	err := w.wb.WriteMessage(i, data)
	if err != nil {
		s := err.Error()
		WebsocketSendCall([]byte(s), w.call, 3, Context, 255)
		_ = w.wb.Close()
		return false
	}
	return true
}
func (w *WebsocketClient) WebsocketRead() {
	for {
		if w.wb == nil {
			WebsocketSendCall([]byte("Pointer = null"), w.call, 2, w.Context, 255)
			return
		}
		m, msg, err := w.wb.ReadMessage()
		if err != nil {
			s := err.Error()
			WebsocketSendCall([]byte(s), w.call, 2, w.Context, 255)
			_ = w.wb.Close()
			return
		}
		WebsocketSendCall(msg, w.call, 1, w.Context, m)
	}
}

// WebsocketClientReceive
// Websocket客户端 同步模式下 接收数据 返回数据指针 失败返回0 length=返回数据长度
func WebsocketClientReceive(Context, OutTimes int) uintptr {
	w := LoadWebSocketContext(Context)
	if w == nil {
		w.err = errors.New("The Context does not exist ")
		return 0
	}
	w.l.Lock()
	defer w.l.Unlock()
	if w.synchronous == false {
		w.err = errors.New("Not synchronous mode ")
		return 0
	}
	_OutTime := OutTimes
	if _OutTime < 1 {
		_OutTime = 3000
	}
	if w.wb == nil {
		return 0
	}
	w.err = w.wb.SetReadDeadline(time.Now().Add(time.Duration(_OutTime) * time.Millisecond))
	var Buff []byte
	messageType := 0
	length := 0
	messageType, Buff, w.err = w.wb.ReadMessage()
	length = len(Buff)
	if w.err == nil {
		if length > 0 {
			return public.PointerPtr(public.BytesCombine(public.IntToBytes(length), public.BytesCombine(public.IntToBytes(messageType), Buff)))
		}
	}
	return 0
}
func WebsocketSendCall(b []byte, call, types, Context, messageType int) {
	if call > 10 {
		Call.Call(call, Context, types, b, len(b), messageType)
	}

}
