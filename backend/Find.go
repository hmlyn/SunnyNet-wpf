package main

import (
	"bytes"
	"changeme/MapHash"
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"github.com/qtgolang/SunnyNet/src/encoding/hex"
	"io"
	"math"
	"net/http"
	"net/url"
	"strconv"
	"strings"
)

type FindValue struct {
	Options       string
	Value         string
	Type          string
	Range         string
	Color         string
	Bytes         []byte
	BytesReverse  []byte //倒叙后的Bytes 查找 Int Float 需要使用
	ReplaceValue  string
	ReplaceBytes  []byte
	ReplaceCount  int
	SearchResult  map[int]bool
	CaseSensitive bool //是否区分大小写
	PbSkip        int
}

func (c *FindValue) getValue() string {
	DelSpace := strings.Contains(c.Options, "删除空格后搜索")
	v := ""
	if DelSpace {
		v = strings.ReplaceAll(strings.ReplaceAll(c.Value, "\t", ""), " ", "")
	} else {
		v = c.Value
	}
	if c.CaseSensitive {
		return v
	}
	return strings.ToLower(v)
}
func (c *FindValue) Find() any {
	c.CaseSensitive = !strings.Contains(c.Options, "不区分大小写")
	defer func() {
		Insert.Lock()
		SearchPercentage = -1
		Insert.Unlock()
	}()
	c.SearchResult = make(map[int]bool)
	if c.Value == "" {
		CallJs("弹出错误提示", "查找失败：请输入要搜索的内容")
		return nil
	}
	switch c.Type {
	case "UTF8":
		return c.FindUTF8()
	case "GBK":
		return c.FindGBK()
	case "Hex":
		c.CaseSensitive = true
		return c.FindHex()
	case "pb":
		return c.FindProtoBuf()
	case "Base64":
		c.CaseSensitive = true
		return c.FindBase64()
	case "整数4":
		c.CaseSensitive = true
		return c.FindInt32()
	case "整数8":
		c.CaseSensitive = true
		return c.FindInt64()
	case "浮点数4":
		c.CaseSensitive = true
		return c.FindFloat32()
	case "浮点数8":
		c.CaseSensitive = true
		return c.FindFloat64()
	default:
		return nil
	}
}
func (c *FindValue) FindUTF8() any {
	c.Bytes = []byte(c.getValue())
	return c.FindStart()
}
func (c *FindValue) FindGBK() any {
	c.Bytes = Utf8ToGBK([]byte(c.getValue()))
	return c.FindStart()
}
func (c *FindValue) FindHex() any {
	bs, e := hex.DecodeString(c.getValue())
	if e != nil {
		CallJs("弹出错误提示", "查找失败：输入的 HEX 不正确,请检查！！")
		return nil
	}
	c.Bytes = bs
	return c.FindStart()
}
func (c *FindValue) FindBase64() any {
	c.CaseSensitive = false
	b, e := base64.StdEncoding.DecodeString(c.getValue())
	if e != nil {
		CallJs("弹出错误提示", "查找失败：输入的 Base64 不正确,请检查！！")
		return nil
	}
	c.Bytes = b
	return c.FindStart()
}
func (c *FindValue) FindProtoBuf() any {
	c.Bytes = []byte(c.getValue())
	return c.FindStart()
}

func (c *FindValue) Replace() any {
	c.CaseSensitive = !strings.Contains(c.Options, "不区分大小写")
	defer func() {
		Insert.Lock()
		SearchPercentage = -1
		Insert.Unlock()
	}()
	c.SearchResult = make(map[int]bool)
	if c.Value == "" {
		CallJs("弹出错误提示", "替换失败：请输入要查找的内容")
		return nil
	}
	if !c.prepareReplaceBytes() {
		return nil
	}
	return c.ReplaceStart()
}

func (c *FindValue) prepareReplaceBytes() bool {
	switch c.Type {
	case "UTF8":
		c.Bytes = []byte(c.Value)
		c.ReplaceBytes = []byte(c.ReplaceValue)
	case "GBK":
		c.Bytes = Utf8ToGBK([]byte(c.Value))
		c.ReplaceBytes = Utf8ToGBK([]byte(c.ReplaceValue))
	case "Hex":
		c.CaseSensitive = true
		findBytes, err := hex.DecodeString(normalizeHexText(c.Value))
		if err != nil {
			CallJs("弹出错误提示", "替换失败：查找 HEX 不正确,请检查！！")
			return false
		}
		replaceBytes, err := hex.DecodeString(normalizeHexText(c.ReplaceValue))
		if err != nil {
			CallJs("弹出错误提示", "替换失败：替换 HEX 不正确,请检查！！")
			return false
		}
		c.Bytes = findBytes
		c.ReplaceBytes = replaceBytes
	case "Base64":
		c.CaseSensitive = true
		findBytes, err := base64.StdEncoding.DecodeString(strings.TrimSpace(c.Value))
		if err != nil {
			CallJs("弹出错误提示", "替换失败：查找 Base64 不正确,请检查！！")
			return false
		}
		replaceBytes, err := base64.StdEncoding.DecodeString(strings.TrimSpace(c.ReplaceValue))
		if err != nil {
			CallJs("弹出错误提示", "替换失败：替换 Base64 不正确,请检查！！")
			return false
		}
		c.Bytes = findBytes
		c.ReplaceBytes = replaceBytes
	default:
		CallJs("弹出错误提示", "替换失败：当前查找类型不支持替换")
		return false
	}
	if len(c.Bytes) == 0 {
		CallJs("弹出错误提示", "替换失败：查找内容不能为空")
		return false
	}
	return true
}

func normalizeHexText(value string) string {
	replacer := strings.NewReplacer(" ", "", "\t", "", "\r", "", "\n", "")
	return replacer.Replace(value)
}

func (c *FindValue) FindInt32() any {
	num, e := strconv.Atoi(c.getValue())
	if e != nil {
		CallJs("弹出错误提示", "查找失败：输入的数值不正确,或类型选择错误")
		return nil
	}
	bs := make([]byte, 4)
	binary.BigEndian.PutUint32(bs, uint32(num))
	c.Bytes = bs
	bs = make([]byte, 4)
	binary.LittleEndian.PutUint32(bs, uint32(num))
	c.BytesReverse = bs
	return c.FindStart()
}
func (c *FindValue) FindInt64() any {
	num, e := strconv.Atoi(c.getValue())
	if e != nil {
		CallJs("弹出错误提示", "查找失败：输入的数值不正确,或查找类型选择错误")
		return nil
	}
	bs := make([]byte, 8)
	binary.BigEndian.PutUint64(bs, uint64(num))
	c.Bytes = bs
	bs = make([]byte, 8)
	binary.LittleEndian.PutUint64(bs, uint64(num))
	c.BytesReverse = bs
	return c.FindStart()
}
func (c *FindValue) FindFloat32() any {
	// 将字符串转换为float32
	f32, err := strconv.ParseFloat(c.getValue(), 32)
	if err != nil {
		CallJs("弹出错误提示", "查找失败：输入的数值不正确,或查找类型选择错误")
		return nil
	}
	bytes32 := make([]byte, 4)
	binary.BigEndian.PutUint32(bytes32, math.Float32bits(float32(f32)))
	c.Bytes = bytes32
	binary.LittleEndian.PutUint32(bytes32, math.Float32bits(float32(f32)))
	c.BytesReverse = bytes32
	return c.FindStart()
}
func (c *FindValue) FindFloat64() any {
	// 将字符串转换为 float64
	f64, err := strconv.ParseFloat(c.getValue(), 64)
	if err != nil {
		CallJs("弹出错误提示", "查找失败：输入的数值不正确,或查找类型选择错误")
		return nil
	}
	bytes32 := make([]byte, 4)
	binary.BigEndian.PutUint64(bytes32, math.Float64bits(f64))
	c.Bytes = bytes32
	binary.LittleEndian.PutUint64(bytes32, math.Float64bits(f64))
	c.BytesReverse = bytes32
	return c.FindStart()
}

func CancelSearch() []int {
	Insert.Lock()
	i := LastSearch
	for _, v := range LastSearch {
		h := HashMap.GetRequest(v)
		if h != nil {
			h.Color.Search = ""
		}
	}
	LastSearch = make([]int, 0)
	for n := 0; n < len(LastSearchSocket); n++ {
		LastSearchSocket[n].Color = ""
	}
	LastSearchSocket = make([]*MapHash.UpdateSocketList, 0)
	Insert.Unlock()
	return i
}

func (c *FindValue) FindStart() any {
	if strings.Contains(c.Options, "取消之前的颜色标记") {
		Insert.Lock()
		for i := 0; i < len(LastSearchSocket); i++ {
			LastSearchSocket[i].Color = ""
		}
		LastSearchSocket = make([]*MapHash.UpdateSocketList, 0)
		Insert.Unlock()
	}
	HashMap.Search(c.Search)
	Insert.Lock()
	var _SearchResult []int
	for o, v := range c.SearchResult {
		if v {
			_SearchResult = append(_SearchResult, o)
		}
	}
	f := &SearchResult{SearchResult: _SearchResult, Color: c.Color}
	if strings.Contains(c.Options, "取消之前的颜色标记") {
		for _, v := range LastSearch {
			h := HashMap.GetRequest(v)
			if h != nil {
				h.Color.Search = ""
			}
		}
		f.LastSearchResult = LastSearch
		LastSearch = _SearchResult
	} else {
		for i := 0; i < len(_SearchResult); i++ {
			LastSearch = append(LastSearch, _SearchResult[i])
		}
	}
	Insert.Unlock()
	return f
}

var LastSearch []int
var LastSearchSocket []*MapHash.UpdateSocketList

type SearchResult struct {
	LastSearchResult []int
	SearchResult     []int
	Color            string
	ReplaceCount     int
}

func (c *FindValue) ReplaceStart() any {
	if strings.Contains(c.Options, "取消之前的颜色标记") {
		Insert.Lock()
		for i := 0; i < len(LastSearchSocket); i++ {
			LastSearchSocket[i].Color = ""
		}
		LastSearchSocket = make([]*MapHash.UpdateSocketList, 0)
		Insert.Unlock()
	}
	HashMap.Search(c.ReplaceSearch)
	Insert.Lock()
	var _SearchResult []int
	for o, v := range c.SearchResult {
		if v {
			_SearchResult = append(_SearchResult, o)
		}
	}
	f := &SearchResult{SearchResult: _SearchResult, Color: c.Color, ReplaceCount: c.ReplaceCount}
	if strings.Contains(c.Options, "取消之前的颜色标记") {
		for _, v := range LastSearch {
			h := HashMap.GetRequest(v)
			if h != nil {
				h.Color.Search = ""
			}
		}
		f.LastSearchResult = LastSearch
		LastSearch = _SearchResult
	} else {
		for i := 0; i < len(_SearchResult); i++ {
			LastSearch = append(LastSearch, _SearchResult[i])
		}
	}
	Insert.Unlock()
	return f
}

func (c *FindValue) caseSensitiveSearch(Theology int, s string) bool {
	res := ""
	if !c.CaseSensitive {
		res = strings.ToLower(s)
	} else {
		res = s
	}
	if strings.Contains(res, string(c.Bytes)) {
		c.SearchResult[Theology] = true
		return true
	}
	if len(c.BytesReverse) > 0 {
		if strings.Contains(res, string(c.BytesReverse)) {
			c.SearchResult[Theology] = true
			return true
		}
	}
	return false
}

func (c *FindValue) Search(Theology, percentage int, request *MapHash.Request) {
	Insert.Lock()
	SearchPercentage = percentage
	Insert.Unlock()
	if request == nil {
		return
	}
	if !request.Display {
		return
	}
	if c.Type == "pb" {
		if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "HTTP请求Body" {
			//在请求Body中搜索
			{
				if c.caseSensitiveSearch(Theology, _PbToJson(request.Body, c.PbSkip)) {
					request.Color.Search = c.Color
					return
				}
			}
		}
		if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应Body" {
			//在请求响应Body中搜索
			{
				if c.caseSensitiveSearch(Theology, _PbToJson(request.Response.Body, c.PbSkip)) {
					request.Color.Search = c.Color
					return
				}
			}
		}
		if c.Range == "全部" || c.Range == "socketSend" || c.Range == "socketRec" || c.Range == "socketAll" {
			sv := "上行"
			if c.Range == "socketRec" {
				sv = "下行"
			}
			if c.Range == "socketAll" || c.Range == "全部" {
				sv = "上行下行"
			}
			if request.SocketData != nil {
				for i := 0; i < len(request.SocketData); i++ {
					v := request.SocketData[i]
					if v != nil {
						if strings.Contains(sv, v.Info.Ico) {
							if c.caseSensitiveSearch(Theology, _PbToJson(v.Body, c.PbSkip)) {
								v.Info.Color = c.Color
								request.Color.Search = c.Color
								Insert.Lock()
								LastSearchSocket = append(LastSearchSocket, v.Info)
								Insert.Unlock()
							}
						}
					}
				}
			}
		}

		return
	}

	if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "URL" || c.Range == "HTTP请求头" || c.Range == "HTTP请求Body" {
		//在URL中搜索
		if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "URL" {
			if c.caseSensitiveSearch(Theology, request.URL) {
				request.Color.Search = c.Color
				return
			}
		}
		//在请求协议头中搜索
		if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "HTTP请求头" {
			if request.Header != nil {
				_t := ""
				for k, v := range request.Header {
					if len(v) == 0 {
						_t += k + ": \r\n"
						continue
					}
					for i := 0; i < len(v); i++ {
						_t += k + ": " + v[i] + "\r\n"
					}
				}
				if c.caseSensitiveSearch(Theology, _t) {
					request.Color.Search = c.Color
					return
				}
			}
		}
		//在请求Body中搜索
		if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "HTTP请求Body" {
			if c.caseSensitiveSearch(Theology, string(request.Body)) {
				request.Color.Search = c.Color
				return
			}
		}
	}
	if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应头" || c.Range == "HTTP响应Body" {
		//在请求响应协议头中搜索
		if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应头" {
			if request.Response.Header != nil {
				_t := ""
				for k, v := range request.Response.Header {
					if len(v) == 0 {
						_t += k + ": \r\n"
						continue
					}
					for i := 0; i < len(v); i++ {
						_t += k + ": " + v[i] + "\r\n"
					}
				}
				if c.caseSensitiveSearch(Theology, _t) {
					request.Color.Search = c.Color
					return
				}
			}
		}
		//在请求响应Body中搜索
		if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应Body" {
			if c.caseSensitiveSearch(Theology, string(request.Response.Body)) {
				request.Color.Search = c.Color
				return
			}
		}
	}
	if c.Range == "全部" || c.Range == "socketSend" || c.Range == "socketRec" || c.Range == "socketAll" {
		sv := "上行"
		if c.Range == "socketRec" {
			sv = "下行"
		}
		if c.Range == "socketAll" || c.Range == "全部" {
			sv = "上行下行"
		}
		if request.SocketData != nil {
			for i := 0; i < len(request.SocketData); i++ {
				v := request.SocketData[i]
				if v != nil {
					if strings.Contains(sv, v.Info.Ico) {
						if c.caseSensitiveSearch(Theology, string(v.Body)) {
							v.Info.Color = c.Color
							request.Color.Search = c.Color
							Insert.Lock()
							LastSearchSocket = append(LastSearchSocket, v.Info)
							Insert.Unlock()
						}
					}
				}
			}
		}
	}
}

func (c *FindValue) ReplaceSearch(Theology, percentage int, request *MapHash.Request) {
	Insert.Lock()
	SearchPercentage = percentage
	Insert.Unlock()
	if request == nil || !request.Display {
		return
	}

	replaceCount := 0
	if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "URL" {
		replaceCount += c.replaceURL(request)
	}
	if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "HTTP请求头" {
		replaceCount += c.replaceHeader(&request.Header, true, request)
	}
	if c.Range == "全部" || c.Range == "HTTP请求" || c.Range == "HTTP请求Body" {
		replaceCount += c.replaceRequestBody(request)
	}
	if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应头" {
		replaceCount += c.replaceHeader(&request.Response.Header, false, request)
	}
	if c.Range == "全部" || c.Range == "HTTP响应" || c.Range == "HTTP响应Body" {
		replaceCount += c.replaceResponseBody(request)
	}
	if c.Range == "全部" || c.Range == "socketSend" || c.Range == "socketRec" || c.Range == "socketAll" {
		replaceCount += c.replaceSocketData(Theology, request)
	}

	if replaceCount <= 0 {
		return
	}
	request.Color.Search = c.Color
	c.SearchResult[Theology] = true
	c.ReplaceCount += replaceCount
}

func (c *FindValue) replaceURL(request *MapHash.Request) int {
	next, count := replaceBytes([]byte(request.URL), c.Bytes, c.ReplaceBytes, c.CaseSensitive)
	if count <= 0 {
		return 0
	}
	request.URL = string(next)
	if request.Conn != nil && request.Conn.Request != nil {
		if parsed, err := url.Parse(request.URL); err == nil {
			request.Conn.Request.URL = parsed
			request.Conn.Request.Host = parsed.Host
		}
	}
	return count
}

func (c *FindValue) replaceHeader(header *http.Header, requestSide bool, request *MapHash.Request) int {
	if header == nil || *header == nil {
		return 0
	}

	nextHeader := make(http.Header)
	total := 0
	for name, values := range *header {
		nextNameBytes, nameCount := replaceBytes([]byte(name), c.Bytes, c.ReplaceBytes, c.CaseSensitive)
		nextName := strings.TrimSpace(string(nextNameBytes))
		if nextName == "" {
			nextName = name
			nameCount = 0
		}
		total += nameCount
		if len(values) == 0 {
			nextHeader.Add(nextName, "")
			continue
		}
		for _, value := range values {
			nextValueBytes, valueCount := replaceBytes([]byte(value), c.Bytes, c.ReplaceBytes, c.CaseSensitive)
			total += valueCount
			nextHeader.Add(nextName, string(nextValueBytes))
		}
	}
	if total <= 0 {
		return 0
	}

	*header = nextHeader
	if requestSide {
		if request.Conn != nil && request.Conn.Request != nil {
			request.Conn.Request.Header = nextHeader
		}
	} else if request.Response.Conn != nil && request.Response.Conn.Response != nil {
		request.Response.Conn.Response.Header = nextHeader
	}
	return total
}

func (c *FindValue) replaceRequestBody(request *MapHash.Request) int {
	body := readStoredBody(request.Body, request.BodyRef)
	next, count := replaceBytes(body, c.Bytes, c.ReplaceBytes, c.CaseSensitive)
	if count <= 0 {
		return 0
	}
	setStoredBody(&request.Body, &request.BodyRef, next)
	updateContentLength(request.Header, len(next))
	if request.HasDisplayBody {
		displayBody := readStoredBody(request.DisplayBody, request.DisplayBodyRef)
		nextDisplay, _ := replaceBytes(displayBody, c.Bytes, c.ReplaceBytes, c.CaseSensitive)
		setStoredBody(&request.DisplayBody, &request.DisplayBodyRef, nextDisplay)
	}
	if request.Conn != nil && request.Conn.Request != nil {
		if request.Conn.Request.Body != nil {
			_ = request.Conn.Request.Body.Close()
		}
		request.Conn.Request.Body = io.NopCloser(bytes.NewBuffer(request.Body))
		request.Conn.Request.ContentLength = int64(len(request.Body))
	}
	return count
}

func (c *FindValue) replaceResponseBody(request *MapHash.Request) int {
	body := readStoredBody(request.Response.Body, request.Response.BodyRef)
	next, count := replaceBytes(body, c.Bytes, c.ReplaceBytes, c.CaseSensitive)
	if count <= 0 {
		return 0
	}
	setStoredBody(&request.Response.Body, &request.Response.BodyRef, next)
	updateContentLength(request.Response.Header, len(next))
	if request.Response.HasDisplayBody {
		displayBody := readStoredBody(request.Response.DisplayBody, request.Response.DisplayBodyRef)
		nextDisplay, _ := replaceBytes(displayBody, c.Bytes, c.ReplaceBytes, c.CaseSensitive)
		setStoredBody(&request.Response.DisplayBody, &request.Response.DisplayBodyRef, nextDisplay)
	}
	if request.Response.Conn != nil && request.Response.Conn.Response != nil {
		if request.Response.Conn.Response.Body != nil {
			_ = request.Response.Conn.Response.Body.Close()
		}
		request.Response.Conn.Response.Body = io.NopCloser(bytes.NewBuffer(request.Response.Body))
		request.Response.Conn.Response.ContentLength = int64(len(request.Response.Body))
	}
	return count
}

func (c *FindValue) replaceSocketData(Theology int, request *MapHash.Request) int {
	if request.SocketData == nil {
		return 0
	}

	target := "上行"
	if c.Range == "socketRec" {
		target = "下行"
	}
	if c.Range == "socketAll" || c.Range == "全部" {
		target = "上行下行"
	}

	total := 0
	for _, item := range request.SocketData {
		if item == nil || item.Info == nil || !strings.Contains(target, item.Info.Ico) {
			continue
		}
		next, count := replaceBytes(item.Body, c.Bytes, c.ReplaceBytes, c.CaseSensitive)
		if count <= 0 {
			continue
		}
		item.Body = next
		item.Info.Length = len(next)
		item.Info.BodyHash = formatSocketBodyPreview(next)
		item.Info.Color = c.Color
		Insert.Lock()
		LastSearchSocket = append(LastSearchSocket, item.Info)
		Insert.Unlock()
		total += count
	}
	return total
}

func readStoredBody(body []byte, ref MapHash.BodyRef) []byte {
	if ref.Stored && HashMap.BodyStore != nil {
		if data, ok := HashMap.BodyStore.Read(ref); ok {
			return data
		}
	}
	return body
}

func setStoredBody(target *[]byte, ref *MapHash.BodyRef, body []byte) {
	old := *ref
	*target = body
	*ref = HashMap.TrackBody(body)
	if HashMap.BodyStore != nil {
		HashMap.BodyStore.Delete(old)
	}
}

func updateContentLength(header http.Header, length int) {
	if header == nil {
		return
	}
	for name := range header {
		if strings.EqualFold(name, "Content-Length") {
			header[name] = []string{strconv.Itoa(length)}
			return
		}
	}
}

func formatSocketBodyPreview(body []byte) string {
	if len(body) > 64 {
		return fmt.Sprintf("% X", body[:64]) + "..."
	}
	return fmt.Sprintf("% X", body)
}

func replaceBytes(data []byte, find []byte, replacement []byte, caseSensitive bool) ([]byte, int) {
	if len(find) == 0 || len(data) == 0 {
		return data, 0
	}
	if caseSensitive {
		count := bytes.Count(data, find)
		if count <= 0 {
			return data, 0
		}
		return bytes.ReplaceAll(data, find, replacement), count
	}

	result := make([]byte, 0, len(data))
	count := 0
	for index := 0; index < len(data); {
		if index <= len(data)-len(find) && equalFoldASCII(data[index:index+len(find)], find) {
			result = append(result, replacement...)
			index += len(find)
			count++
			continue
		}
		result = append(result, data[index])
		index++
	}
	if count <= 0 {
		return data, 0
	}
	return result, count
}

func equalFoldASCII(left []byte, right []byte) bool {
	if len(left) != len(right) {
		return false
	}
	for i := 0; i < len(left); i++ {
		if lowerASCII(left[i]) != lowerASCII(right[i]) {
			return false
		}
	}
	return true
}

func lowerASCII(value byte) byte {
	if value >= 'A' && value <= 'Z' {
		return value + ('a' - 'A')
	}
	return value
}
