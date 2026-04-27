package Api

import (
	"bufio"
	"bytes"
	"crypto/tls"
	"errors"
	"github.com/qtgolang/SunnyNet/Call"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/Certificate"
	"github.com/qtgolang/SunnyNet/src/GoWinHttp"
	"net"
	"net/url"
	"sync"
	"time"
)

type SocketClient struct {
	err         error
	wb          net.Conn
	call        int
	Context     int
	BufferSize  int
	synchronous bool
	R           *bufio.Reader
	l           sync.Mutex
}

var SocketMap = make(map[int]interface{})
var SocketMapLock sync.Mutex

func LoadSocketContext(Context int) *SocketClient {
	SocketMapLock.Lock()
	s := SocketMap[Context]
	SocketMapLock.Unlock()
	if s == nil {
		return nil
	}
	return s.(*SocketClient)
}

// CreateSocketClient
// 创建 TCP客户端
func CreateSocketClient() int {
	w := &SocketClient{}
	Context := newMessageId()
	w.Context = Context
	SocketMapLock.Lock()
	SocketMap[Context] = w
	SocketMapLock.Unlock()
	return Context
}

// 释放 TCP客户端
//
//export RemoveSocketClient
func RemoveSocketClient(Context int) {
	k := LoadSocketContext(Context)
	if k != nil {
		k.l.Lock()
		if k.wb != nil {
			k.Close()
		}
		k.l.Unlock()
	}
	DelClientContext(Context)

}

func DelClientContext(Context int) {
	SocketMapLock.Lock()
	delete(SocketMap, Context)
	SocketMapLock.Unlock()
}

// TCP客户端 取错误
//
//export SocketClientGetErr
func SocketClientGetErr(Context int) uintptr {
	k := LoadSocketContext(Context)
	if k != nil {
		if k.err != nil {
			return public.PointerPtr(k.err.Error())
		}
	}
	return 0
}

// SocketClientSetBufferSize
// TCP客户端 置缓冲区大小
func SocketClientSetBufferSize(Context, BufferSize int) bool {
	k := LoadSocketContext(Context)
	if k != nil {
		k.l.Lock()
		defer k.l.Unlock()
		k.BufferSize = BufferSize
		if k.BufferSize < 1 {
			k.BufferSize = 4096
		}
		return true
	}
	return false
}

// SocketClientDial
//
//	TCP客户端 连接
func SocketClientDial(Context int, addr string, call int, isTls, synchronous bool, ProxyUrl string, CertificateConText int, ProxyOutTime int) bool {
	w := LoadSocketContext(Context)
	if w == nil {
		return false
	}
	w.l.Lock()
	defer w.l.Unlock()
	w.err = nil
	w.call = call
	if w.BufferSize < 1 {
		w.BufferSize = 4096
	}
	uAddr := SunnyNet.TargetInfo{}
	uAddr.Parse(addr, 0)
	if uAddr.Port == 0 {
		w.err = errors.New("addr error ")
		return false
	}
	if ProxyUrl != "" {
		Pu, e := url.Parse(ProxyUrl)
		if e != nil {
			w.err = errors.New("ProxyUrl Error ")
			return false
		}
		if Pu.Scheme != "socket" && Pu.Scheme != "socket5" && Pu.Scheme != "socks5" {
			w.err = errors.New("Wrong proxy type, only Socket5 is supported ")
			return false
		}
		if len(Pu.Host) > 3 {
			c := &GoWinHttp.Proxy{S5TypeProxy: true, User: Pu.User.Username()}
			p, ok := Pu.User.Password()
			if ok {
				c.Pass = p
			}
			out := time.Duration(ProxyOutTime) * time.Millisecond
			if ProxyOutTime < 1 {
				out = 15000 * time.Millisecond
			}
			a, b := net.DialTimeout("tcp", Pu.Host, out)
			if b != nil {
				w.err = b
			} else {
				_ = a.SetDeadline(time.Now().Add(out))
				if GoWinHttp.ConnectS5(&a, c, uAddr.Host, uAddr.Port) == false {
					w.err = errors.New("Socket5 Proxy Connect Fail ")
				} else {
					_ = a.SetDeadline(time.Time{})
				}

			}
			w.wb = a
			w.err = b
		} else {
			a, b := net.Dial("tcp", uAddr.String())
			w.wb = a
			w.err = b
		}
	} else {
		a, b := net.Dial("tcp", uAddr.String())
		w.wb = a
		w.err = b
	}
	if w.err != nil {
		return false
	}
	w.synchronous = synchronous
	if isTls {
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
		tl := tls.Client(w.wb, t)
		w.err = tl.Handshake()
		w.wb = tl
	}

	if w.err != nil {
		w.Close()
		return false
	}
	w.R = bufio.NewReaderSize(w.wb, w.BufferSize)
	if synchronous == false {
		go w.SocketClientRead()
	}
	return true
}

// TCP客户端 同步模式下 接收数据
//
//export SocketClientReceive
func SocketClientReceive(Context, OutTimes int) uintptr {
	w := LoadSocketContext(Context)
	if w == nil {
		w.err = errors.New("The Context does not exist ")
		return 0
	}

	//w.l.Lock()
	//defer w.l.Unlock()
	if w.synchronous == false {
		w.err = errors.New("Not synchronous mode ")
		return 0
	}
	_OutTime := OutTimes
	if _OutTime < 1 {
		_OutTime = 100
	}
	if w.wb == nil {
		return 0
	}
	_ = w.wb.SetReadDeadline(time.Now().Add(time.Duration(_OutTime) * time.Millisecond))
	var Buff = make([]byte, w.BufferSize)
	var le = 0
	le, w.err = w.R.Read(Buff[0:])
	if le > 0 {
		return public.PointerPtr(public.BytesCombine(public.IntToBytes(le), Buff[0:le]))
	}
	return 0
}

// SocketClientClose
// TCP客户端 断开连接
func SocketClientClose(Context int) {
	w := LoadSocketContext(Context)
	if w == nil {
		return
	}
	w.l.Lock()
	defer w.l.Unlock()
	w.Close()
}

// SocketClientWrite
// TCP客户端 发送数据
func SocketClientWrite(Context, OutTimes int, val uintptr, valLen int) int {
	data := public.CStringToBytes(val, valLen)
	w := LoadSocketContext(Context)
	if w == nil {
		return 0
	}

	//w.l.Lock()
	//defer w.l.Unlock()
	_OutTimes := OutTimes
	if _OutTimes < 0 {
		_OutTimes = 30000
	}
	m, err := w.Write(data, _OutTimes)
	if err != nil {
		s := err.Error()
		SocketClientSendCall([]byte(s), w.call, 3, Context)
		//w.Close()
		return m
	}
	return m
}
func (w *SocketClient) Write(b []byte, OutTimes int) (int, error) {
	if w.wb == nil {
		return 0, errors.New("Connection closed")
	}
	_ = (w.wb).SetWriteDeadline(time.Now().Add(time.Duration(OutTimes) * time.Millisecond))
	return (w.wb).Write(b)
}
func (w *SocketClient) Close() {
	if w.wb != nil {
		_ = w.wb.Close()
		w.wb = nil
	}
}
func (w *SocketClient) SocketClientRead() {
	defer func() {
		if err := recover(); err != nil {

		}
	}()
	non := 0
	for {
		if w.wb == nil {
			SocketClientSendCall([]byte("The connection may be closed "), w.call, 2, w.Context)
			w.Close()
			return
		}
		_ = w.wb.SetReadDeadline(time.Time{})
		response, err := w.readAllShut()
		if len(response) == 0 {
			non++
			if non > 10 {
				SocketClientSendCall([]byte("The connection may be closed "), w.call, 2, w.Context)
				w.Close()
				return
			}
			continue
		} else {
			non = 0
			SocketClientSendCall(response, w.call, 1, w.Context)
		}
		if err != nil {
			SocketClientSendCall([]byte(err.Error()), w.call, 2, w.Context)
			w.Close()
			return
		}
	}
}
func (w *SocketClient) readAllShut() ([]byte, error) {
	if w.R == nil {
		return make([]byte, 0), errors.New("Connection closed ")
	}
	re := bytes.NewBuffer(nil)
	_bytes := make([]byte, w.BufferSize)
	length, err := w.R.Read(_bytes[0:])
	re.Write(_bytes[:length])
	if err != nil {
		if _, ok := err.(*net.OpError); ok {
			re.Reset()
			rb := re.Bytes()
			re.Reset()
			return rb, err
		}
	}
	rb := re.Bytes()
	re.Reset()
	return rb, nil
}
func SocketClientSendCall(b []byte, call, types, Context int) {
	if call > 0 {
		Call.Call(call, Context, types, b, len(b))
	}
}
