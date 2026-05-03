package MapHash

import (
	"bufio"
	"bytes"
	"crypto/sha256"
	"crypto/tls"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/GoWinHttp"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"sort"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

type Map struct {
	Request            map[int]*Request
	lock               sync.Mutex
	UpdateLength       map[int]*ResponseLength
	BodyStore          *BodyStore
	httpActionLock     sync.Mutex
	pendingHTTPActions []pendingHTTPAction
	pendingRuleHits    map[int][]TrafficRuleHit
}

type WaitGroup struct {
	lock      sync.Mutex
	waitGroup sync.WaitGroup
	i         int
}

func (v *WaitGroup) Add(i int) {
	v.lock.Lock()
	v.i += i
	v.waitGroup.Add(i)
	v.lock.Unlock()
}
func (v *WaitGroup) Done() {
	v.lock.Lock()
	v.i--
	if v.i < 0 {
		v.lock.Unlock()
		return
	}
	v.waitGroup.Done()
	v.lock.Unlock()
}
func (v *WaitGroup) Wait() {
	v.waitGroup.Wait()
}

type Request struct {
	PID            string      `json:"PID"`    //进程
	Method         string      `json:"Method"` //方式
	URL            string      `json:"URL"`    //请求地址
	Proto          string      `json:"Proto"`
	Header         http.Header `json:"Header"`
	Body           []byte      `json:"Body"`
	BodyRef        BodyRef     `json:"-"`
	Display        bool        `json:"Display"` //是否需要显示到列表
	DisplayBody    []byte      `json:"-"`
	DisplayBodyRef BodyRef     `json:"-"`
	HasDisplayBody bool        `json:"-"`
	Response       struct {
		Conn           *SunnyNet.HttpConn `json:"-"`
		Header         http.Header        `json:"Header"`
		Body           []byte             `json:"Body"`
		BodyRef        BodyRef            `json:"-"`
		DisplayBody    []byte             `json:"-"`
		DisplayBodyRef BodyRef            `json:"-"`
		HasDisplayBody bool               `json:"-"`
		StateCode      int                `json:"StateCode"`
		Error          bool               `json:"Error"`
	} `json:"Response"`
	Break   uint8              `json:"Break"`
	Wait    WaitGroup          `json:"-"`
	Conn    *SunnyNet.HttpConn `json:"-"`
	WsConn  *SunnyNet.WsConn   `json:"-"`
	TcpConn *SunnyNet.TcpConn  `json:"-"`
	UdpConn *SunnyNet.UDPConn  `json:"-"`
	Options struct {
		StopSend bool `json:"StopSend"`
		StopRec  bool `json:"StopRec"`
		StopALL  bool `json:"StopALL"`
	} `json:"StopSend"`
	SocketData     []*UpdateSocketData `json:"SocketData"`
	SendTime       string              `json:"SendTime"`
	RecTime        string              `json:"RecTime"`
	SendNum        int                 `json:"SendNum"`
	RecNum         int                 `json:"RecNum"`
	Way            string              `json:"Way"`
	Notes          string              `json:"Notes"`
	ClientIP       string              `json:"ClientIP"`
	TLSFingerprint *TLSFingerprint     `json:"TLSFingerprint,omitempty"`
	Color          struct {
		TagColor string `json:"TagColor"` //标记的文本颜色
		Search   string `json:"search"`   //搜索的背景颜色
	} `json:"color"` //显示图标
	RuleHits []TrafficRuleHit `json:"RuleHits"`
}

type TrafficRuleHit struct {
	Time      string `json:"Time"`
	Theology  int    `json:"Theology"`
	RuleType  string `json:"RuleType"`
	RuleHash  string `json:"RuleHash"`
	RuleName  string `json:"RuleName"`
	Action    string `json:"Action"`
	Direction string `json:"Direction"`
	URL       string `json:"URL"`
}
type RequestWeb struct {
	Method                 string
	URL                    string
	Proto                  string //HTTP/1.1
	Header                 http.Header
	Body                   []byte
	BodySize               int
	BodyPreviewSize        int
	BodyTruncated          bool
	DisplayBody            []byte
	DisplayBodySize        int
	DisplayBodyPreviewSize int
	DisplayBodyTruncated   bool
	HasDisplayBody         bool
	Response               struct {
		Header                 http.Header
		Body                   []byte
		BodySize               int
		BodyPreviewSize        int
		BodyTruncated          bool
		DisplayBody            []byte
		DisplayBodySize        int
		DisplayBodyPreviewSize int
		DisplayBodyTruncated   bool
		HasDisplayBody         bool
		StateCode              int
		StateText              string
		Error                  bool
	}
	SocketData     []*UpdateSocketList
	TLSFingerprint *TLSFingerprint
	Options        struct {
		StopSend bool
		StopRec  bool
		StopALL  bool
	}
}
type ResponseLength struct {
	Send int
	Rec  int
}

const httpDetailBodyPreviewLimit = 256 * 1024

type pendingHTTPAction struct {
	Method    string
	URL       string
	BodyHash  [32]byte
	BodyLen   int
	Mode      int
	ExpiresAt time.Time
}

type UpdateResponseLength struct {
	Send     int
	Rec      int
	Theology int
}

func (m *Map) GetALLResponseLength() []*UpdateResponseLength {
	m.lock.Lock()
	defer m.lock.Unlock()
	res := make([]*UpdateResponseLength, 0)
	for kk, vv := range m.UpdateLength {
		res = append(res, &UpdateResponseLength{Theology: kk, Rec: vv.Rec, Send: vv.Send})
	}
	m.UpdateLength = make(map[int]*ResponseLength)
	return res
}
func (m *Map) addResponseLength(TheologyID int, a, b int) {
	if m.UpdateLength[TheologyID] == nil {
		m.UpdateLength[TheologyID] = &ResponseLength{Send: a, Rec: b}
	} else {
		m.UpdateLength[TheologyID].Send = a
		m.UpdateLength[TheologyID].Rec = b
	}

}
func (m *Map) SetRequest(TheologyID int, h *Request) {
	m.lock.Lock()
	defer m.lock.Unlock()
	if h != nil && len(m.pendingRuleHits[TheologyID]) > 0 {
		h.RuleHits = appendRuleHits(h.RuleHits, m.pendingRuleHits[TheologyID]...)
		delete(m.pendingRuleHits, TheologyID)
	}
	m.Request[TheologyID] = h
}

func (m *Map) RecordRuleHit(TheologyID int, hit TrafficRuleHit) {
	if m == nil || TheologyID < 1 {
		return
	}

	m.lock.Lock()
	defer m.lock.Unlock()
	if hit.Theology <= 0 {
		hit.Theology = TheologyID
	}
	if h := m.Request[TheologyID]; h != nil {
		h.RuleHits = appendRuleHits(h.RuleHits, hit)
		return
	}
	if m.pendingRuleHits == nil {
		m.pendingRuleHits = make(map[int][]TrafficRuleHit)
	}
	m.pendingRuleHits[TheologyID] = appendRuleHits(m.pendingRuleHits[TheologyID], hit)
}

func appendRuleHits(target []TrafficRuleHit, hits ...TrafficRuleHit) []TrafficRuleHit {
	for _, hit := range hits {
		duplicate := false
		for _, existing := range target {
			if existing.RuleHash == hit.RuleHash &&
				existing.RuleType == hit.RuleType &&
				existing.Direction == hit.Direction &&
				existing.Action == hit.Action &&
				existing.Time == hit.Time {
				duplicate = true
				break
			}
		}
		if !duplicate {
			target = append(target, hit)
		}
	}
	return target
}
func (m *Map) TrackBody(body []byte) BodyRef {
	if m == nil || m.BodyStore == nil {
		return BodyRef{Size: len(body)}
	}
	return m.BodyStore.Put(body)
}
func (m *Map) SetRequestBody(h *Request, body []byte) {
	if h == nil {
		return
	}
	old := h.BodyRef
	h.Body = body
	h.BodyRef = m.TrackBody(body)
	if m != nil && m.BodyStore != nil {
		m.BodyStore.Delete(old)
	}
}
func (m *Map) SetResponseBody(h *Request, body []byte) {
	if h == nil {
		return
	}
	old := h.Response.BodyRef
	h.Response.Body = body
	h.Response.BodyRef = m.TrackBody(body)
	if m != nil && m.BodyStore != nil {
		m.BodyStore.Delete(old)
	}
}
func (m *Map) RegisterHTTPReplayAction(method string, rawURL string, body []byte, mode int) {
	if m == nil || mode <= 0 {
		return
	}
	action := pendingHTTPAction{
		Method:    strings.ToUpper(method),
		URL:       rawURL,
		BodyHash:  sha256.Sum256(body),
		BodyLen:   len(body),
		Mode:      mode,
		ExpiresAt: time.Now().Add(30 * time.Second),
	}
	m.httpActionLock.Lock()
	m.pendingHTTPActions = append(m.pendingHTTPActions, action)
	m.compactHTTPActionsLocked(time.Now())
	m.httpActionLock.Unlock()
}
func (m *Map) ConsumeHTTPReplayAction(method string, rawURL string, body []byte) int {
	if m == nil {
		return 0
	}
	now := time.Now()
	bodyHash := sha256.Sum256(body)
	method = strings.ToUpper(method)

	m.httpActionLock.Lock()
	defer m.httpActionLock.Unlock()
	m.compactHTTPActionsLocked(now)

	match := func(action pendingHTTPAction, strictURL bool) bool {
		if action.Method != method || action.BodyLen != len(body) || action.BodyHash != bodyHash {
			return false
		}
		return !strictURL || action.URL == rawURL
	}
	for i, action := range m.pendingHTTPActions {
		if match(action, true) {
			m.pendingHTTPActions = append(m.pendingHTTPActions[:i], m.pendingHTTPActions[i+1:]...)
			return action.Mode
		}
	}
	for i, action := range m.pendingHTTPActions {
		if match(action, false) {
			m.pendingHTTPActions = append(m.pendingHTTPActions[:i], m.pendingHTTPActions[i+1:]...)
			return action.Mode
		}
	}
	return 0
}
func (m *Map) compactHTTPActionsLocked(now time.Time) {
	if len(m.pendingHTTPActions) == 0 {
		return
	}
	dst := m.pendingHTTPActions[:0]
	for _, action := range m.pendingHTTPActions {
		if action.ExpiresAt.After(now) {
			dst = append(dst, action)
		}
	}
	m.pendingHTTPActions = dst
}
func (m *Map) SetRequestDisplayBody(TheologyID int, body []byte) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[TheologyID]
	if h == nil {
		return false
	}
	old := h.DisplayBodyRef
	h.DisplayBody = append(h.DisplayBody[:0], body...)
	h.DisplayBodyRef = m.TrackBody(h.DisplayBody)
	if m.BodyStore != nil {
		m.BodyStore.Delete(old)
	}
	h.HasDisplayBody = true
	return true
}
func (m *Map) SetResponseDisplayBody(TheologyID int, body []byte) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[TheologyID]
	if h == nil {
		return false
	}
	old := h.Response.DisplayBodyRef
	h.Response.DisplayBody = append(h.Response.DisplayBody[:0], body...)
	h.Response.DisplayBodyRef = m.TrackBody(h.Response.DisplayBody)
	if m.BodyStore != nil {
		m.BodyStore.Delete(old)
	}
	h.Response.HasDisplayBody = true
	return true
}
func (m *Map) CreateUniqueID() int {
	m.lock.Lock()
	defer m.lock.Unlock()
	tm := atomic.AddInt64(&public.Theology, 1)
	return int(tm)
}
func (m *Map) SetRequestUDP(TheologyID int, Conn *SunnyNet.UDPConn) *Request {
	m.lock.Lock()
	defer m.lock.Unlock()
	if m.Request[TheologyID] == nil {
		m.Request[TheologyID] = &Request{UdpConn: Conn}
	} else {
		m.Request[TheologyID].UdpConn = Conn
	}
	m.Request[TheologyID].Display = true
	return m.Request[TheologyID]
}
func (m *Map) SetRequestTCP(TheologyID int, Conn *SunnyNet.TcpConn) *Request {
	m.lock.Lock()
	defer m.lock.Unlock()
	if m.Request[TheologyID] == nil {
		m.Request[TheologyID] = &Request{TcpConn: Conn}
	} else {
		m.Request[TheologyID].TcpConn = Conn
	}
	m.Request[TheologyID].Display = true
	return m.Request[TheologyID]
}
func (m *Map) GetRequestWeb(Theology int) *RequestWeb {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[Theology]
	if h == nil {
		return nil
	}
	requestBody, requestBodySize, requestBodyTruncated := BodyPreview(h.Body, h.BodyRef)
	requestDisplayBody, requestDisplayBodySize, requestDisplayBodyTruncated := BodyPreview(h.DisplayBody, h.DisplayBodyRef)
	responseBody, responseBodySize, responseBodyTruncated := BodyPreview(h.Response.Body, h.Response.BodyRef)
	responseDisplayBody, responseDisplayBodySize, responseDisplayBodyTruncated := BodyPreview(h.Response.DisplayBody, h.Response.DisplayBodyRef)
	r := &RequestWeb{Body: requestBody, URL: h.URL, Proto: h.Proto, Header: h.Header, Method: h.Method, TLSFingerprint: h.TLSFingerprint}
	r.BodySize = requestBodySize
	r.BodyPreviewSize = len(requestBody)
	r.BodyTruncated = requestBodyTruncated
	r.DisplayBody = requestDisplayBody
	r.DisplayBodySize = requestDisplayBodySize
	r.DisplayBodyPreviewSize = len(requestDisplayBody)
	r.DisplayBodyTruncated = requestDisplayBodyTruncated
	r.HasDisplayBody = h.HasDisplayBody
	r.Response.Header = h.Response.Header
	r.Response.Body = responseBody
	r.Response.BodySize = responseBodySize
	r.Response.BodyPreviewSize = len(responseBody)
	r.Response.BodyTruncated = responseBodyTruncated
	r.Response.DisplayBody = responseDisplayBody
	r.Response.DisplayBodySize = responseDisplayBodySize
	r.Response.DisplayBodyPreviewSize = len(responseDisplayBody)
	r.Response.DisplayBodyTruncated = responseDisplayBodyTruncated
	r.Response.HasDisplayBody = h.Response.HasDisplayBody
	r.Response.StateText = http.StatusText(h.Response.StateCode)
	r.Response.StateCode = h.Response.StateCode
	r.Response.Error = h.Response.Error
	r.SocketData = make([]*UpdateSocketList, 0)
	for i := 0; i < len(h.SocketData); i++ {
		r.SocketData = append(r.SocketData, h.SocketData[i].Info)
	}
	r.Options.StopSend = h.Options.StopSend
	r.Options.StopRec = h.Options.StopRec
	r.Options.StopALL = h.Options.StopALL

	return r
}

func BodyPreview(body []byte, ref BodyRef) ([]byte, int, bool) {
	total := len(body)
	if ref.Size > total {
		total = ref.Size
	}
	if total <= httpDetailBodyPreviewLimit {
		return public.CopyBytes(body), total, false
	}
	if len(body) > httpDetailBodyPreviewLimit {
		return public.CopyBytes(body[:httpDetailBodyPreviewLimit]), total, true
	}
	return public.CopyBytes(body), total, true
}

type HTTPBodyRangeResult struct {
	Ok        bool   `json:"Ok"`
	Error     string `json:"Error"`
	Theology  int    `json:"Theology"`
	Direction string `json:"Direction"`
	Kind      string `json:"Kind"`
	Offset    int64  `json:"Offset"`
	Count     int    `json:"Count"`
	Total     int    `json:"Total"`
	End       bool   `json:"End"`
	Body      []byte `json:"Body"`
}

func (m *Map) GetHTTPBodyRange(theology int, direction, kind string, offset int64, count int) *HTTPBodyRangeResult {
	result := &HTTPBodyRangeResult{
		Ok:        false,
		Theology:  theology,
		Direction: direction,
		Kind:      kind,
		Offset:    offset,
	}
	if count < 1 {
		result.Error = "count must be greater than 0"
		return result
	}
	if offset < 0 {
		result.Error = "offset must be greater than or equal to 0"
		return result
	}

	body, ref, total, ok := m.selectHTTPBody(theology, direction, kind)
	if !ok {
		result.Error = "body not found"
		return result
	}
	result.Total = total
	if offset >= int64(total) {
		result.Ok = true
		result.End = true
		result.Body = []byte{}
		return result
	}
	if maxCount := total - int(offset); count > maxCount {
		count = maxCount
	}

	var chunk []byte
	if ref.Stored && m.BodyStore != nil {
		if data, exists := m.BodyStore.ReadRange(ref, offset, count); exists {
			chunk = data
		}
	}
	if chunk == nil {
		end := int(offset) + count
		if int(offset) > len(body) {
			chunk = []byte{}
		} else {
			if end > len(body) {
				end = len(body)
			}
			chunk = public.CopyBytes(body[int(offset):end])
		}
	}

	result.Ok = true
	result.Count = len(chunk)
	result.End = int(offset)+len(chunk) >= total
	result.Body = chunk
	return result
}

func (m *Map) selectHTTPBody(theology int, direction, kind string) ([]byte, BodyRef, int, bool) {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[theology]
	if h == nil {
		return nil, BodyRef{}, 0, false
	}

	direction = strings.ToLower(strings.TrimSpace(direction))
	kind = strings.ToLower(strings.TrimSpace(kind))
	if kind == "" {
		kind = "raw"
	}

	var body []byte
	var ref BodyRef
	var has bool
	if direction == "response" || direction == "响应" {
		if kind == "display" && h.Response.HasDisplayBody {
			body = h.Response.DisplayBody
			ref = h.Response.DisplayBodyRef
		} else {
			body = h.Response.Body
			ref = h.Response.BodyRef
		}
		has = h.Response.HasDisplayBody || len(h.Response.Body) > 0 || h.Response.BodyRef.Size > 0
	} else {
		if kind == "display" && h.HasDisplayBody {
			body = h.DisplayBody
			ref = h.DisplayBodyRef
		} else {
			body = h.Body
			ref = h.BodyRef
		}
		has = h.HasDisplayBody || len(h.Body) > 0 || h.BodyRef.Size > 0
	}

	total := len(body)
	if ref.Size > total {
		total = ref.Size
	}
	return body, ref, total, has
}
func (m *Map) GetRequest(Theology int) *Request {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[Theology]
	return h
}
func (m *Map) SetOptions(Theology int, send, rec, all bool) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[Theology]
	if h != nil {
		h.Options.StopRec = rec
		h.Options.StopALL = all
		h.Options.StopSend = send
	}
	return h != nil
}
func (m *Map) SetSocketData(Theology int, data *UpdateSocketData, up bool, num int) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[Theology]
	if h != nil {
		if num > 0 {
			if up {
				h.SendNum += num
			} else {
				h.RecNum += num
			}
			m.addResponseLength(Theology, h.SendNum, h.RecNum)
		}
		h.SocketData = append(h.SocketData, data)
	}
	return h != nil
}
func (m *Map) SetSocketDataEmpty(Theology int) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	h := m.Request[Theology]
	if h != nil {
		h.SocketData = make([]*UpdateSocketData, 0)
		h.SendNum = 0
		h.RecNum = 0
		m.addResponseLength(Theology, h.SendNum, h.RecNum)
	}
	return h != nil
}
func NewHashMap() *Map {
	return &Map{
		Request:         make(map[int]*Request),
		UpdateLength:    make(map[int]*ResponseLength),
		BodyStore:       NewBodyStore(),
		pendingRuleHits: make(map[int][]TrafficRuleHit),
	}
}
func (m *Map) Empty() {
	m.lock.Lock()
	defer m.lock.Unlock()
	mz := make(map[int]*Request)
	for k, v := range m.Request {
		if v != nil {
			if v.UdpConn != nil || v.TcpConn != nil || v.WsConn != nil {
				if v.Display {
					v.SocketData = make([]*UpdateSocketData, 0)
					v.RecNum = 0
					v.SendNum = 0
					mz[k] = v
				}
			} else {
				m.releaseBodies(v)
			}
			v.Wait.Done()
		}
	}
	m.Request = mz
	m.UpdateLength = make(map[int]*ResponseLength)
	m.pendingRuleHits = make(map[int][]TrafficRuleHit)
}

// ReleaseAll 全部放行
func (m *Map) ReleaseAll() {
	m.lock.Lock()
	defer m.lock.Unlock()
	for _, v := range m.Request {
		if v != nil {
			v.Wait.Done()
		}
	}
}
func (m *Map) Delete(TheologyArray []int) {
	m.lock.Lock()
	defer m.lock.Unlock()
	for _, k := range TheologyArray {
		v := m.Request[k]
		vv := m.UpdateLength[k]
		if vv != nil {
			vv.Rec = 0
			vv.Send = 0
		}
		if v != nil {
			if v.UdpConn != nil || v.TcpConn != nil || v.WsConn != nil {
				if v.Display {
					v.SocketData = make([]*UpdateSocketData, 0)
					v.RecNum = 0
					v.SendNum = 0
				}
			} else {
				m.releaseBodies(v)
				delete(m.Request, k)
				delete(m.UpdateLength, k)
			}
			v.Wait.Done()
		}

	}
}

func (m *Map) releaseBodies(v *Request) {
	if m == nil || m.BodyStore == nil || v == nil {
		return
	}
	m.BodyStore.Delete(v.BodyRef)
	m.BodyStore.Delete(v.DisplayBodyRef)
	m.BodyStore.Delete(v.Response.BodyRef)
	m.BodyStore.Delete(v.Response.DisplayBodyRef)
}
func (m *Map) Search(callSearch func(int, int, *Request)) {
	m.lock.Lock()
	defer m.lock.Unlock()
	max := float64(len(m.Request))
	i := float64(0)
	for k, v := range m.Request {
		i++
		callSearch(k, int(i/max*100), v)
	}
}
func (m *Map) CloseSession(TheologyArray []int) {
	m.lock.Lock()
	defer m.lock.Unlock()
	for _, k := range TheologyArray {
		v := m.Request[k]
		if v != nil {
			if v.TcpConn != nil {
				v.TcpConn.Close()
			}
			if v.WsConn != nil {
				v.WsConn.Close()
			}
		}
	}

}

func (m *Map) SaveToFile(Path string, All bool, TheologyArray []int, SetStatusText func(string)) bool {
	m.lock.Lock()
	defer m.lock.Unlock()
	var SaveData []*Request
	SetStatusText("正在统计需要储存的信息")
	if All {
		var keys []int
		for k := range m.Request {
			keys = append(keys, k)
		}
		sort.Ints(keys)
		for _, _ket := range keys {
			k := m.Request[_ket]
			if k != nil {
				if k.Display {
					SaveData = append(SaveData, k)
				}
			}
		}
	} else {
		for _, k := range TheologyArray {
			v := m.Request[k]
			if v != nil {
				SaveData = append(SaveData, v)
			}
		}
	}
	if len(SaveData) < 1 {
		SetStatusText("需要储存的数量小于1")
		return false
	}
	SetStatusText("有 " + strconv.Itoa(len(SaveData)) + " 条数据正在序列化储存...")
	bs, e := json.Marshal(&SaveData)
	if e != nil {
		SetStatusText("数据序列化储存失败！！")
		return false
	}
	SetStatusText("数据压缩中...")
	bs2 := BrCompress(bs)
	if len(bs2) < 1 {
		SetStatusText("数据失败！")
		return false
	}
	SetStatusText("正在写入文件")
	err := os.WriteFile(Path, bs2, 666)
	if err == nil {
		SetStatusText("保存记录文件成功：" + Path)
	} else {
		SetStatusText("保存记录文件失败：" + err.Error())
	}
	return err == nil
}

// Resend 重发请求
func (m *Map) Resend(TheologyArray []int, mode int, Port int) {
	m.lock.Lock()
	defer m.lock.Unlock()
	for _, k := range TheologyArray {
		v := m.Request[k]
		if v != nil {
			go m.resend(v, mode, Port)
		}
	}
}

func (m *Map) resend(r *Request, mode, Port int) {
	if r != nil {
		/*
			if r.Way == "Websocket" {
				resendWS(r, mode, Port)
			}
		*/
		if r.Way == "HTTP" {
			m.resendHttp(r, mode, Port)
		}
		/*
			else if r.Way == "UDP" {
				resendUDP(r, Port)
			} else if strings.Contains(strings.ToUpper(r.Way), "TCP") {
				resendTCP(r, strings.Contains(strings.ToUpper(r.Way), "TLS"), Port)
			}
		*/
	}

}

// 写完但是貌似有问题
func resendTCP(m *Request, _TLS bool, LocalSunnyNetPort int) {
	_t := strings.Split(m.URL, "->")
	if len(_t) != 2 {
		return
	}
	DataInfo := m.SocketData
	if DataInfo == nil {
		return
	}
	RemoteAddr := _t[1]
	uAddr := SunnyNet.TargetInfo{}
	uAddr.Parse(RemoteAddr, 0)
	if uAddr.Port == 0 {
		return
	}
	Conn, err := net.DialTimeout("tcp", "127.0.0.1:"+strconv.Itoa(LocalSunnyNetPort), time.Duration(10000)*time.Millisecond)
	defer func() { _ = Conn.Close() }()
	if err != nil {
		return
	}
	if GoWinHttp.ConnectS5(&Conn, &GoWinHttp.Proxy{}, uAddr.Host, uAddr.Port) == false {
		return
	}
	if _TLS {
		cfg := &tls.Config{ServerName: uAddr.Host}
		cfg.InsecureSkipVerify = true
		tlsConn := tls.Client(Conn, cfg)
		err = tlsConn.Handshake()
		if err != nil {
			return
		}
		Conn = tlsConn
	}
	var t time.Time
	var t2 time.Time
	for _, v := range m.SocketData {
		t2, err = time.Parse("2006-01-02 15:04:05.000", "2024-01-01 "+v.Info.Time)
		if t.Year() == 2024 {
			if err == nil {
				time.Sleep(t2.Sub(t))
			}
		}
		t = t2
		if v.Info.Ico == "上行" {
			_, _ = Conn.Write(v.Body)
			mx := make([]byte, 4096)
			_, _ = Conn.Read(mx)
		}
	}
	time.Sleep(time.Second)
}

// 未写完
func resendUDP(m *Request, LocalSunnyNetPort int) {
	_t := strings.Split(m.URL, "->")
	if len(_t) != 2 {
		return
	}
	DataInfo := m.SocketData
	if DataInfo == nil {
		return
	}
	RemoteAddr := _t[1]
	uAddr := SunnyNet.TargetInfo{}
	uAddr.Parse(RemoteAddr, 0)
	if uAddr.Port == 0 {
		return
	}
	PackMsg := func(data []byte) []byte {
		return nil
	}
	// 创建一个 UDP 地址
	serverAddr, err := net.ResolveUDPAddr("udp", "127.0.0.1:"+strconv.Itoa(LocalSunnyNetPort))
	if err != nil {
		return
	}
	// 连接 UDP 服务器
	conn, err := net.DialUDP("udp", nil, serverAddr)
	if err != nil {
		return
	}
	defer func() { _ = conn.Close() }()

	var t time.Time
	var t2 time.Time
	for _, v := range m.SocketData {
		t2, err = time.Parse("2006-01-02 15:04:05.000", "2024-01-01 "+v.Info.Time)
		if t.Year() == 2024 {
			if err == nil {
				time.Sleep(t2.Sub(t))
			}
		}
		t = t2
		if v.Info.Ico == "上行" {
			_, _ = conn.Write(PackMsg(v.Body))
			buffer := make([]byte, 4096)
			_ = conn.SetReadDeadline(time.Now().Add(time.Second))
			_, _, _ = conn.ReadFromUDP(buffer)
		}
	}
	time.Sleep(time.Second)

}

func resendWS(m *Request, mode, Port int) {

}
func (m *Map) resendHttp(r *Request, mode, SunnyNetServerPort int) {
	if r == nil {
		return
	}
	Body := io.NopCloser(bytes.NewBuffer(r.Body))
	defer func() {
		if Body != nil {
			_ = Body.Close()
		}
	}()
	h, e := http.NewRequest(r.Method, r.URL, Body)
	if e != nil {
		return
	}
	h.Header = r.Header.Clone()
	if mode > 0 {
		m.RegisterHTTPReplayAction(r.Method, r.URL, r.Body, mode)
	}
	w := GoWinHttp.NewGoWinHttp()
	w.SetProxyType(true)
	w.SetProxyIP("127.0.0.1:" + strconv.Itoa(SunnyNetServerPort))
	RES, _ := w.Do(h)
	if RES != nil {
		if RES.Body != nil {
			_ = RES.Body.Close()
		}
	}
}

type UpdateSocketList struct {
	Index    int    `json:"#"`
	Theology int    `json:"Theology"`
	BodyHash string `json:"数据"`
	Ico      string `json:"ico"`
	Time     string `json:"时间"`
	Length   int    `json:"长度"`
	WsType   string `json:"类型"`
	Color    string `json:"background"`
}

type UpdateSocketData struct {
	Info *UpdateSocketList `json:"Info"`
	Body []byte            `json:"Body"`
}
type RequestImg struct {
	Body string `json:"Body"`
	Type string `json:"Type"`
}

type BuiltHttpResult struct {
	StatusCode int         `json:"StatusCode"`
	Status     string      `json:"Status"`
	Proto      string      `json:"Proto"`
	Header     http.Header `json:"Header"`
	Body       []byte      `json:"Body"`
}

func SendBuiltHttp(method, rawURL, proto string, header http.Header, body []byte, sunnyNetPort int) (*BuiltHttpResult, error) {
	requestURL, err := url.Parse(rawURL)
	if err != nil {
		return nil, err
	}
	if requestURL.Scheme != "http" && requestURL.Scheme != "https" {
		return nil, fmt.Errorf("仅支持 http/https 请求")
	}
	if requestURL.Hostname() == "" {
		return nil, fmt.Errorf("URL 缺少主机")
	}

	conn, err := dialSunnySocks5("127.0.0.1:"+strconv.Itoa(sunnyNetPort), requestURL)
	if err != nil {
		return nil, err
	}
	defer func() { _ = conn.Close() }()

	if requestURL.Scheme == "https" {
		tlsConn := tls.Client(conn, &tls.Config{
			ServerName:         requestURL.Hostname(),
			InsecureSkipVerify: true,
		})
		if err = tlsConn.Handshake(); err != nil {
			return nil, err
		}
		conn = tlsConn
	}

	requestProto, err := normalizeHttpProto(proto)
	if err != nil {
		return nil, err
	}
	if err = writeBuiltHttpRequest(conn, method, requestURL, requestProto, header, body); err != nil {
		return nil, err
	}

	response, err := http.ReadResponse(bufio.NewReader(conn), nil)
	if err != nil {
		return nil, err
	}
	defer func() { _ = response.Body.Close() }()

	responseBody, _ := io.ReadAll(response.Body)
	return &BuiltHttpResult{
		StatusCode: response.StatusCode,
		Status:     response.Status,
		Proto:      response.Proto,
		Header:     response.Header,
		Body:       responseBody,
	}, nil
}

func dialSunnySocks5(proxyAddress string, requestURL *url.URL) (net.Conn, error) {
	conn, err := net.DialTimeout("tcp", proxyAddress, 15*time.Second)
	if err != nil {
		return nil, err
	}

	fail := func(e error) (net.Conn, error) {
		_ = conn.Close()
		return nil, e
	}

	_ = conn.SetDeadline(time.Now().Add(20 * time.Second))
	if _, err = conn.Write([]byte{0x05, 0x01, 0x00}); err != nil {
		return fail(err)
	}
	reply := make([]byte, 2)
	if _, err = io.ReadFull(conn, reply); err != nil {
		return fail(err)
	}
	if reply[0] != 0x05 || reply[1] != 0x00 {
		return fail(fmt.Errorf("Sunny SOCKS5 握手失败"))
	}

	host := requestURL.Hostname()
	port := requestURL.Port()
	if port == "" {
		if requestURL.Scheme == "https" {
			port = "443"
		} else {
			port = "80"
		}
	}
	portNumber, err := strconv.Atoi(port)
	if err != nil || portNumber <= 0 || portNumber > 65535 {
		return fail(fmt.Errorf("目标端口无效"))
	}

	connectRequest := []byte{0x05, 0x01, 0x00}
	if ip := net.ParseIP(host); ip != nil {
		if ip4 := ip.To4(); ip4 != nil {
			connectRequest = append(connectRequest, 0x01)
			connectRequest = append(connectRequest, ip4...)
		} else {
			connectRequest = append(connectRequest, 0x04)
			connectRequest = append(connectRequest, ip.To16()...)
		}
	} else {
		if len(host) > 255 {
			return fail(fmt.Errorf("目标主机名过长"))
		}
		connectRequest = append(connectRequest, 0x03, byte(len(host)))
		connectRequest = append(connectRequest, []byte(host)...)
	}
	connectRequest = append(connectRequest, byte(portNumber>>8), byte(portNumber))
	if _, err = conn.Write(connectRequest); err != nil {
		return fail(err)
	}

	header := make([]byte, 4)
	if _, err = io.ReadFull(conn, header); err != nil {
		return fail(err)
	}
	if header[1] != 0x00 {
		return fail(fmt.Errorf("Sunny SOCKS5 连接目标失败: %d", header[1]))
	}

	var skip int
	switch header[3] {
	case 0x01:
		skip = 4
	case 0x03:
		length := make([]byte, 1)
		if _, err = io.ReadFull(conn, length); err != nil {
			return fail(err)
		}
		skip = int(length[0])
	case 0x04:
		skip = 16
	default:
		return fail(fmt.Errorf("Sunny SOCKS5 返回地址类型无效"))
	}
	if skip > 0 {
		if _, err = io.ReadFull(conn, make([]byte, skip)); err != nil {
			return fail(err)
		}
	}
	if _, err = io.ReadFull(conn, make([]byte, 2)); err != nil {
		return fail(err)
	}
	_ = conn.SetDeadline(time.Time{})
	return conn, nil
}

func writeBuiltHttpRequest(conn net.Conn, method string, requestURL *url.URL, proto string, header http.Header, body []byte) error {
	var builder strings.Builder
	path := requestURL.RequestURI()
	if path == "" {
		path = "/"
	}
	builder.WriteString(strings.ToUpper(strings.TrimSpace(method)))
	builder.WriteString(" ")
	builder.WriteString(path)
	builder.WriteString(" ")
	builder.WriteString(proto)
	builder.WriteString("\r\n")

	hasHost := false
	hasContentLength := false
	for name, values := range header {
		lowerName := strings.ToLower(name)
		if lowerName == "host" {
			hasHost = true
		}
		if lowerName == "content-length" {
			hasContentLength = true
			continue
		}
		for _, value := range values {
			builder.WriteString(name)
			builder.WriteString(": ")
			builder.WriteString(value)
			builder.WriteString("\r\n")
		}
	}
	if !hasHost {
		builder.WriteString("Host: ")
		builder.WriteString(requestURL.Host)
		builder.WriteString("\r\n")
	}
	if len(body) > 0 && !hasContentLength {
		builder.WriteString("Content-Length: ")
		builder.WriteString(strconv.Itoa(len(body)))
		builder.WriteString("\r\n")
	}
	builder.WriteString("\r\n")

	if _, err := conn.Write([]byte(builder.String())); err != nil {
		return err
	}
	if len(body) == 0 {
		return nil
	}
	_, err := conn.Write(body)
	return err
}

func normalizeHttpProto(proto string) (string, error) {
	switch strings.TrimSpace(strings.ToUpper(proto)) {
	case "HTTP/1.0":
		return "HTTP/1.0", nil
	case "HTTP/1.2":
		return "HTTP/1.2", nil
	case "HTTP/2.0":
		return "", fmt.Errorf("Sunny 构造发送暂不支持 HTTP/2.0 精确请求")
	case "", "HTTP/1.1":
		return "HTTP/1.1", nil
	default:
		return "", fmt.Errorf("不支持的 HTTP 版本: %s", proto)
	}
}

var multipartTag = []byte("--")
var multipartTag2 = []byte("\r\n\r\n")

func (r *Request) GetRequestImg() *RequestImg {
	if r == nil || r.Body == nil {
		return nil
	}
	ar := bytes.Split(r.Body, []byte("\n"))
	tag := make([]byte, 0)
	if len(ar) > 0 {
		tag = ar[0]
	}
	if !bytes.HasPrefix(tag, multipartTag) {
		return nil
	}
	ar = bytes.Split(r.Body, tag)
	for _, v := range ar {
		m := strings.ToLower(string(v))
		_type := strings.TrimSpace(public.SubString(m, "content-type:", "\n"))
		if strings.Contains(_type, "image/") {
			ar1 := strings.Split(_type, "/")
			if len(ar1) < 2 {
				continue
			}
			_type = ar1[1]
			ar2 := bytes.Split(v, multipartTag2)
			if len(ar2) < 2 {
				continue
			}
			bs := make([]byte, 0)
			for k, vv := range ar2 {
				if k == 0 {
					continue
				}
				if k == 1 {
					bs = append(bs, vv...)
					continue
				}
				bs = append(bs, multipartTag2...)
				bs = append(bs, vv...)
			}
			res := &RequestImg{}
			res.Body = base64.StdEncoding.EncodeToString(bs)
			res.Type = _type
			return res
		}
	}
	return nil
}
