package main

import (
	"bytes"
	"changeme/MapHash"
	"compress/gzip"
	"compress/zlib"
	"encoding/base64"
	"encoding/json"
	"github.com/andybalholm/brotli"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/encoding/hex"
	"github.com/qtgolang/SunnyNet/src/protobuf/JSON"
	"github.com/traefik/yaegi/interp"
	"github.com/traefik/yaegi/stdlib"
	"io"
	"net/http"
	"net/url"
	"os"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"
)

const ReplaceRulesType_Bytes = uint8(1)
const ReplaceRulesType_File = uint8(2)

type ReplaceRules struct {
	Type   uint8
	source []byte //源内容
	target []byte //目标内容
}

var _ReplaceRules []ReplaceRules
var decodeScriptCache = make(map[string]decodeScriptCacheEntry)

type decodeScriptCacheEntry struct {
	fn  func(string, string, string, []byte) []byte
	err error
}

type requestRewriteOperation struct {
	Target    string `json:"Target"`
	Operation string `json:"Operation"`
	Key       string `json:"Key"`
	Value     string `json:"Value"`
	ValueType string `json:"ValueType"`
}

func ReplaceRulesEvent(command string, args *JSON.SyJson) any {
	switch command {
	case "保存替换规则":
		_TmpLock.Lock()
		defer _TmpLock.Unlock()
		var failHash []string
		var _Rules []ReplaceRules
		var _CRules []ConfigReplaceRules
		for i := 0; i < args.GetNum("Data"); i++ {
			_Hash := args.GetData("Data[" + strconv.Itoa(i) + "].Hash")
			_Type := args.GetData("Data[" + strconv.Itoa(i) + "].替换类型")
			_source := args.GetData("Data[" + strconv.Itoa(i) + "].源内容")
			_target := args.GetData("Data[" + strconv.Itoa(i) + "].替换内容")
			_source = strings.ReplaceAll(_source, "\\\\", "\\")
			_source = strings.ReplaceAll(_source, "\\\"", "\"")
			_target = strings.ReplaceAll(_target, "\\\\", "\\")
			_target = strings.ReplaceAll(_target, "\\\"", "\"")
			if _source == "" {
				failHash = append(failHash, _Hash)
				continue
			}
			if _Type == "Base64" {
				bs1, e := base64.StdEncoding.DecodeString(_source)
				if e != nil {
					failHash = append(failHash, _Hash)
					continue
				}
				bs2, e := base64.StdEncoding.DecodeString(_target)
				if e != nil {
					failHash = append(failHash, _Hash)
					continue
				}
				_Rules = append(_Rules, ReplaceRules{Type: ReplaceRulesType_Bytes, source: bs1, target: bs2})
				_CRules = append(_CRules, ConfigReplaceRules{Type: _Type, Hash: _Hash, Src: _source, Dest: _target})
			} else if _Type == "HEX" {
				bs1, e := hex.DecodeString(_source)
				if e != nil {
					failHash = append(failHash, _Hash)
					continue
				}
				bs2, e := hex.DecodeString(_target)
				if e != nil {
					failHash = append(failHash, _Hash)
					continue
				}
				_Rules = append(_Rules, ReplaceRules{Type: ReplaceRulesType_Bytes, source: bs1, target: bs2})
				_CRules = append(_CRules, ConfigReplaceRules{Type: _Type, Hash: _Hash, Src: _source, Dest: _target})

			} else if _Type == "String(UTF8)" {
				_Rules = append(_Rules, ReplaceRules{Type: ReplaceRulesType_Bytes, source: []byte(_source), target: []byte(_target)})
				_CRules = append(_CRules, ConfigReplaceRules{Type: _Type, Hash: _Hash, Src: _source, Dest: _target})
			} else if _Type == "String(GBK)" {
				_Rules = append(_Rules, ReplaceRules{Type: ReplaceRulesType_Bytes, source: Utf8ToGBK([]byte(_source)), target: Utf8ToGBK([]byte(_target))})
				_CRules = append(_CRules, ConfigReplaceRules{Type: _Type, Hash: _Hash, Src: _source, Dest: _target})
			} else if _Type == "响应文件" {
				bs1, e := os.ReadFile(_target)
				if e != nil {
					failHash = append(failHash, _Hash)
					continue
				}
				_Rules = append(_Rules, ReplaceRules{Type: ReplaceRulesType_File, source: []byte(_source), target: bs1})
				_CRules = append(_CRules, ConfigReplaceRules{Type: _Type, Hash: _Hash, Src: _source, Dest: _target})
			} else {
				failHash = append(failHash, _Hash)
			}
		}
		GlobalConfig.ReplaceRules = _CRules
		_ = GlobalConfig.saveToFile()
		_ReplaceRules = _Rules
		return failHash
	default:
		return HostsRulesEvent(command, args)
	}
}
func ReplaceURL(theology int, method string, u *url.URL) (*url.URL, []byte) {
	if u == nil {
		return u, nil
	}
	if mappedURL, responseBody, ok := ApplyRequestMapping(theology, method, u); ok {
		return mappedURL, responseBody
	}

	ur := u.String()
	_TmpLock.Lock()
	defer _TmpLock.Unlock()
	ok := false
	res := make([]byte, 0)
	for i := 0; i < len(_ReplaceRules); i++ {
		if _ReplaceRules[i].Type == ReplaceRulesType_Bytes {
			if strings.Contains(ur, string(_ReplaceRules[i].source)) {
				ur = strings.ReplaceAll(ur, string(_ReplaceRules[i].source), string(_ReplaceRules[i].target))
				ok = true
			}
		} else if _ReplaceRules[i].Type == ReplaceRulesType_File {
			if strings.Contains(ur, string(_ReplaceRules[i].source)) {
				res = _ReplaceRules[i].target
				ok = true
				break
			}
		}
	}
	if !ok {
		return u, nil
	}
	um, e := url.Parse(ur)
	if e != nil {
		return u, nil
	}
	return um, res
}

type RequestBlockResult struct {
	Close bool
}

type WebSocketBlockResult struct {
	Close bool
	Drop  bool
}

type SocketBlockResult struct {
	Close bool
	Drop  bool
}

func ApplyRequestBlock(theology int, method string, u *url.URL) (*RequestBlockResult, bool) {
	if matchRequestBlockRule(theology, method, u, "断开请求", "请求") {
		return &RequestBlockResult{Close: true}, true
	}

	return nil, false
}

func ApplyResponseBlock(theology int, method string, u *url.URL) bool {
	return matchRequestBlockRule(theology, method, u, "断开响应", "响应")
}

func matchRequestBlockRule(theology int, method string, u *url.URL, expectedAction string, direction string) bool {
	if u == nil {
		return false
	}

	requestURL := u.String()
	_TmpLock.Lock()
	rules := append([]ConfigRequestBlockRule(nil), GlobalConfig.RuleCenter.BlockRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	for _, rule := range rules {
		if !rule.Enable {
			continue
		}
		action := normalizeRequestBlockAction(rule.Action)
		if action != expectedAction {
			continue
		}
		if !mappingMethodMatches(rule.Method, method) || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, requestURL) {
			continue
		}
		recordTrafficRuleHit(theology, "HTTP屏蔽", rule.ConfigTrafficRuleBase, action, direction, requestURL)
		return true
	}

	return false
}

func normalizeRequestBlockAction(action string) string {
	if strings.TrimSpace(action) == "断开响应" {
		return "断开响应"
	}
	return "断开请求"
}

func ApplyWebSocketBlock(theology int, wsType int, method string, u *url.URL) (*WebSocketBlockResult, bool) {
	if u == nil {
		return nil, false
	}

	requestURL := u.String()
	_TmpLock.Lock()
	rules := append([]ConfigWebSocketBlockRule(nil), GlobalConfig.RuleCenter.WebSocketBlockRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return nil, false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	for _, rule := range rules {
		if !rule.Enable {
			continue
		}
		action := normalizeWebSocketBlockAction(rule.Action)
		if !webSocketBlockActionMatches(action, wsType) {
			continue
		}
		if !mappingMethodMatches(rule.Method, method) || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, requestURL) {
			continue
		}

		direction := webSocketDirectionText(wsType)
		recordTrafficRuleHit(theology, "WebSocket屏蔽", rule.ConfigTrafficRuleBase, action, direction, requestURL)
		return &WebSocketBlockResult{
			Close: action == "断开连接",
			Drop:  action == "丢弃上行帧" || action == "丢弃下行帧",
		}, true
	}

	return nil, false
}

func normalizeWebSocketBlockAction(action string) string {
	switch strings.TrimSpace(action) {
	case "丢弃上行帧":
		return "丢弃上行帧"
	case "丢弃下行帧":
		return "丢弃下行帧"
	default:
		return "断开连接"
	}
}

func webSocketBlockActionMatches(action string, wsType int) bool {
	switch action {
	case "丢弃上行帧":
		return wsType == public.WebsocketUserSend
	case "丢弃下行帧":
		return wsType == public.WebsocketServerSend
	default:
		return wsType == public.WebsocketConnectionOK ||
			wsType == public.WebsocketUserSend ||
			wsType == public.WebsocketServerSend
	}
}

func webSocketDirectionText(wsType int) string {
	switch wsType {
	case public.WebsocketUserSend:
		return "上行"
	case public.WebsocketServerSend:
		return "下行"
	case public.WebsocketConnectionOK:
		return "连接"
	case public.WebsocketDisconnect:
		return "断开"
	default:
		return "WebSocket"
	}
}

func ApplyTcpBlock(theology int, tcpType int, protocol string, rawURL string) (*SocketBlockResult, bool) {
	rawURL = strings.TrimSpace(rawURL)
	if rawURL == "" {
		return nil, false
	}

	_TmpLock.Lock()
	rules := append([]ConfigTcpBlockRule(nil), GlobalConfig.RuleCenter.TcpBlockRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return nil, false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	for _, rule := range rules {
		if !rule.Enable {
			continue
		}
		action := normalizeTcpBlockAction(rule.Action)
		if !tcpBlockActionMatches(action, tcpType) {
			continue
		}
		if !mappingMethodMatches(rule.Method, protocol) || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, rawURL) {
			continue
		}

		direction := tcpDirectionText(tcpType)
		recordTrafficRuleHit(theology, "TCP屏蔽", rule.ConfigTrafficRuleBase, action, direction, rawURL)
		return &SocketBlockResult{
			Close: action == "断开连接",
			Drop:  action == "丢弃上行包" || action == "丢弃下行包",
		}, true
	}

	return nil, false
}

func ApplyUdpBlock(theology int, udpType int8, rawURL string) (*SocketBlockResult, bool) {
	rawURL = strings.TrimSpace(rawURL)
	if rawURL == "" {
		return nil, false
	}

	_TmpLock.Lock()
	rules := append([]ConfigUdpBlockRule(nil), GlobalConfig.RuleCenter.UdpBlockRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return nil, false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	for _, rule := range rules {
		if !rule.Enable {
			continue
		}
		action := normalizeUdpBlockAction(rule.Action)
		if !udpBlockActionMatches(action, udpType) {
			continue
		}
		if !mappingMethodMatches(rule.Method, "UDP") || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, rawURL) {
			continue
		}

		direction := udpDirectionText(udpType)
		recordTrafficRuleHit(theology, "UDP屏蔽", rule.ConfigTrafficRuleBase, action, direction, rawURL)
		return &SocketBlockResult{Drop: true}, true
	}

	return nil, false
}

func normalizeTcpBlockAction(action string) string {
	switch strings.TrimSpace(action) {
	case "丢弃上行包":
		return "丢弃上行包"
	case "丢弃下行包":
		return "丢弃下行包"
	default:
		return "断开连接"
	}
}

func normalizeUdpBlockAction(action string) string {
	if strings.TrimSpace(action) == "丢弃下行包" {
		return "丢弃下行包"
	}
	return "丢弃上行包"
}

func tcpBlockActionMatches(action string, tcpType int) bool {
	switch action {
	case "丢弃上行包":
		return tcpType == public.SunnyNetMsgTypeTCPClientSend
	case "丢弃下行包":
		return tcpType == public.SunnyNetMsgTypeTCPClientReceive
	default:
		return tcpType == public.SunnyNetMsgTypeTCPAboutToConnect ||
			tcpType == public.SunnyNetMsgTypeTCPConnectOK ||
			tcpType == public.SunnyNetMsgTypeTCPClientSend ||
			tcpType == public.SunnyNetMsgTypeTCPClientReceive
	}
}

func udpBlockActionMatches(action string, udpType int8) bool {
	if action == "丢弃下行包" {
		return udpType == public.SunnyNetUDPTypeReceive
	}
	return udpType == public.SunnyNetUDPTypeSend
}

func tcpDirectionText(tcpType int) string {
	switch tcpType {
	case public.SunnyNetMsgTypeTCPClientSend:
		return "上行"
	case public.SunnyNetMsgTypeTCPClientReceive:
		return "下行"
	case public.SunnyNetMsgTypeTCPAboutToConnect, public.SunnyNetMsgTypeTCPConnectOK:
		return "连接"
	case public.SunnyNetMsgTypeTCPClose:
		return "断开"
	default:
		return "TCP"
	}
}

func udpDirectionText(udpType int8) string {
	switch udpType {
	case public.SunnyNetUDPTypeSend:
		return "上行"
	case public.SunnyNetUDPTypeReceive:
		return "下行"
	case public.SunnyNetUDPTypeClosed:
		return "断开"
	default:
		return "UDP"
	}
}

func ApplyRequestMapping(theology int, method string, u *url.URL) (*url.URL, []byte, bool) {
	if u == nil {
		return u, nil, false
	}

	requestURL := u.String()
	_TmpLock.Lock()
	rules := append([]ConfigRequestMappingRule(nil), GlobalConfig.RuleCenter.MappingRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return u, nil, false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	for _, rule := range rules {
		if !rule.Enable || rule.LegacyReplaceRule && strings.EqualFold(rule.MappingType, "旧替换规则") {
			continue
		}
		if !mappingMethodMatches(rule.Method, method) || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, requestURL) {
			continue
		}

		switch strings.TrimSpace(rule.MappingType) {
		case "本地文件", "响应文件":
			path := strings.ReplaceAll(rule.TargetContent, "\\\\", "\\")
			body, err := os.ReadFile(path)
			if err != nil {
				continue
			}
			recordTrafficRuleHit(theology, "请求映射", rule.ConfigTrafficRuleBase, rule.MappingType, "响应", requestURL)
			return u, body, true
		case "固定响应":
			body, err := decodeMappingBody(rule.TargetContent, rule.ValueType)
			if err != nil {
				continue
			}
			recordTrafficRuleHit(theology, "请求映射", rule.ConfigTrafficRuleBase, rule.MappingType, "响应", requestURL)
			return u, body, true
		case "远程地址", "远程URL", "重定向":
			target := strings.TrimSpace(rule.TargetContent)
			if target == "" {
				continue
			}
			parsed, err := url.Parse(target)
			if err != nil {
				continue
			}
			if !parsed.IsAbs() {
				parsed = u.ResolveReference(parsed)
			}
			recordTrafficRuleHit(theology, "请求映射", rule.ConfigTrafficRuleBase, rule.MappingType, "请求", requestURL)
			return parsed, nil, true
		}
	}

	return u, nil, false
}

func ApplyRequestRewrite(theology int, method string, u *url.URL, header http.Header, body []byte) (string, *url.URL, []byte) {
	if u == nil {
		return method, u, body
	}

	rules := getSortedRewriteRules()
	if len(rules) == 0 {
		return method, u, body
	}

	currentMethod := method
	currentURL := cloneURL(u)
	currentBody := body
	for _, rule := range rules {
		if !rewriteRuleMatches(rule, currentMethod, currentURL, true) {
			continue
		}

		operations := getRewriteOperations(rule)
		if len(operations) == 0 {
			continue
		}
		recordTrafficRuleHit(theology, "请求重写", rule.ConfigTrafficRuleBase, buildRewriteAction(operations), "请求", currentURL.String())
		for _, operation := range operations {
			currentMethod, currentURL, currentBody = applyRequestRewriteOperation(operation, currentMethod, currentURL, header, currentBody)
		}
	}

	return currentMethod, currentURL, currentBody
}

func ApplyResponseRewrite(theology int, method string, u *url.URL, response *http.Response, body []byte) []byte {
	if u == nil || response == nil {
		return body
	}

	rules := getSortedRewriteRules()
	if len(rules) == 0 {
		return body
	}

	currentBody := body
	for _, rule := range rules {
		if !rewriteRuleMatches(rule, method, u, false) {
			continue
		}

		operations := getRewriteOperations(rule)
		if len(operations) == 0 {
			continue
		}
		recordTrafficRuleHit(theology, "请求重写", rule.ConfigTrafficRuleBase, buildRewriteAction(operations), "响应", u.String())
		for _, operation := range operations {
			currentBody = applyResponseRewriteOperation(operation, response, currentBody)
		}
	}

	return currentBody
}

func ApplyHTTPDecodeRules(theology int, request bool, method string, u *url.URL, body []byte) bool {
	if theology < 1 || u == nil || len(body) == 0 {
		return false
	}

	_TmpLock.Lock()
	rules := append([]ConfigRequestDecodeRule(nil), GlobalConfig.RuleCenter.DecodeRules...)
	_TmpLock.Unlock()
	if len(rules) == 0 {
		return false
	}

	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})

	currentBody := body
	applied := false
	for _, rule := range rules {
		if !rule.Enable {
			continue
		}
		if !rewriteDirectionMatches(rule.Direction, request) {
			continue
		}
		if !mappingMethodMatches(rule.Method, method) || !mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, u.String()) {
			continue
		}
		decoded, ok := decodeDisplayBody(currentBody, rule, request, method, u.String())
		if !ok || len(decoded) == 0 {
			continue
		}
		recordTrafficRuleHit(theology, "请求解密", rule.ConfigTrafficRuleBase, rule.DecoderType, directionText(request), u.String())
		currentBody = decoded
		applied = true
	}

	if !applied {
		return false
	}
	if request {
		return HashMap.SetRequestDisplayBody(theology, currentBody)
	}
	return HashMap.SetResponseDisplayBody(theology, currentBody)
}

func decodeDisplayBody(body []byte, rule ConfigRequestDecodeRule, request bool, method string, rawURL string) ([]byte, bool) {
	direction := "响应"
	if request {
		direction = "请求"
	}
	switch strings.TrimSpace(rule.DecoderType) {
	case "GZIP解压", "GZIP", "gzip":
		return decodeGzipBody(body)
	case "ZLIB解压", "ZLIB", "zlib", "Deflate解压":
		return decodeZlibBody(body)
	case "Brotli解压", "BROTLI", "br", "BR":
		return decodeBrotliBody(body)
	case "Base64解码", "Base64":
		return decodeBase64Body(body)
	case "URL解码", "URLDecode":
		return decodeURLBody(body)
	case "HEX解码", "HEX":
		return decodeHexBody(body)
	case "脚本", "Go脚本":
		return decodeScriptBody(body, rule.ScriptCode, method, rawURL, direction)
	case "自动解压", "":
		return decodeAutoBody(body)
	default:
		return nil, false
	}
}

func decodeAutoBody(body []byte) ([]byte, bool) {
	if decoded, ok := decodeGzipBody(body); ok {
		return decoded, true
	}
	if decoded, ok := decodeZlibBody(body); ok {
		return decoded, true
	}
	if decoded, ok := decodeBrotliBody(body); ok {
		return decoded, true
	}
	return nil, false
}

func decodeGzipBody(body []byte) ([]byte, bool) {
	reader, err := gzip.NewReader(bytes.NewReader(body))
	if err != nil {
		return nil, false
	}
	defer reader.Close()
	decoded, err := io.ReadAll(reader)
	return decoded, err == nil
}

func decodeZlibBody(body []byte) ([]byte, bool) {
	reader, err := zlib.NewReader(bytes.NewReader(body))
	if err != nil {
		return nil, false
	}
	defer reader.Close()
	decoded, err := io.ReadAll(reader)
	return decoded, err == nil
}

func decodeBrotliBody(body []byte) ([]byte, bool) {
	decoded, err := io.ReadAll(brotli.NewReader(bytes.NewReader(body)))
	return decoded, err == nil
}

func decodeBase64Body(body []byte) ([]byte, bool) {
	text := strings.TrimSpace(string(body))
	if text == "" {
		return nil, false
	}
	decoded, err := base64.StdEncoding.DecodeString(text)
	if err == nil {
		return decoded, true
	}
	decoded, err = base64.RawStdEncoding.DecodeString(text)
	return decoded, err == nil
}

func decodeURLBody(body []byte) ([]byte, bool) {
	decoded, err := url.QueryUnescape(string(body))
	if err != nil {
		decoded, err = url.PathUnescape(string(body))
	}
	return []byte(decoded), err == nil
}

func decodeHexBody(body []byte) ([]byte, bool) {
	text := strings.TrimSpace(string(body))
	text = strings.ReplaceAll(text, "0x", "")
	text = strings.ReplaceAll(text, "0X", "")
	replacer := strings.NewReplacer(" ", "", "\r", "", "\n", "", "\t", "-", "")
	text = replacer.Replace(text)
	if text == "" || len(text)%2 != 0 {
		return nil, false
	}
	decoded, err := hex.DecodeString(text)
	return decoded, err == nil
}

func decodeScriptBody(body []byte, scriptCode string, method string, rawURL string, direction string) (decoded []byte, ok bool) {
	fn, ok := getDecodeScriptFunc(scriptCode)
	if !ok || fn == nil {
		return nil, false
	}
	defer func() {
		if recover() != nil {
			decoded = nil
			ok = false
		}
	}()
	decoded = fn(method, rawURL, direction, body)
	return decoded, len(decoded) > 0
}

func getDecodeScriptFunc(scriptCode string) (func(string, string, string, []byte) []byte, bool) {
	source := normalizeDecodeScriptSource(scriptCode)
	if strings.TrimSpace(source) == "" {
		return nil, false
	}

	_TmpLock.Lock()
	entry, exists := decodeScriptCache[source]
	_TmpLock.Unlock()
	if exists {
		return entry.fn, entry.err == nil
	}

	fn, err := compileDecodeScript(source)
	entry = decodeScriptCacheEntry{fn: fn, err: err}
	_TmpLock.Lock()
	decodeScriptCache[source] = entry
	_TmpLock.Unlock()
	return entry.fn, entry.err == nil
}

func normalizeDecodeScriptSource(scriptCode string) string {
	scriptCode = strings.TrimSpace(scriptCode)
	if scriptCode == "" {
		return ""
	}
	if strings.HasPrefix(scriptCode, "package ") {
		return scriptCode
	}
	return "package main\n\n" + scriptCode
}

func compileDecodeScript(source string) (func(string, string, string, []byte) []byte, error) {
	iEval := interp.New(interp.Options{})
	if err := iEval.Use(stdlib.Symbols); err != nil {
		return nil, err
	}
	if _, err := iEval.Eval(source); err != nil {
		return nil, err
	}
	v, err := iEval.Eval("main.Decode")
	if err != nil {
		return nil, err
	}
	fn, ok := v.Interface().(func(string, string, string, []byte) []byte)
	if !ok {
		return nil, strconv.ErrSyntax
	}
	return fn, nil
}

func getSortedRewriteRules() []ConfigRequestRewriteRule {
	_TmpLock.Lock()
	rules := append([]ConfigRequestRewriteRule(nil), GlobalConfig.RuleCenter.RewriteRules...)
	_TmpLock.Unlock()
	sort.SliceStable(rules, func(i, j int) bool {
		return rules[i].Priority < rules[j].Priority
	})
	return rules
}

func rewriteRuleMatches(rule ConfigRequestRewriteRule, method string, u *url.URL, request bool) bool {
	if !rule.Enable || u == nil {
		return false
	}
	if !rewriteDirectionMatches(rule.Direction, request) {
		return false
	}
	return mappingMethodMatches(rule.Method, method) && mappingURLMatches(rule.UrlMatchType, rule.UrlPattern, u.String())
}

func rewriteDirectionMatches(direction string, request bool) bool {
	switch strings.TrimSpace(direction) {
	case "请求", "上行":
		return request
	case "响应", "下行":
		return !request
	case "全部", "双向", "上下行":
		return true
	default:
		return request
	}
}

func applyRequestRewriteOperation(rewriteOperation requestRewriteOperation, method string, u *url.URL, header http.Header, body []byte) (string, *url.URL, []byte) {
	target := strings.TrimSpace(rewriteOperation.Target)
	operation := strings.TrimSpace(rewriteOperation.Operation)
	key := strings.TrimSpace(rewriteOperation.Key)
	value := strings.ReplaceAll(rewriteOperation.Value, "\\\\", "\\")
	value = strings.ReplaceAll(value, "\\\"", "\"")

	switch target {
	case "请求方法", "Method":
		if !isDeleteOperation(operation) && strings.TrimSpace(value) != "" {
			method = strings.ToUpper(strings.TrimSpace(value))
		}
	case "URL", "完整URL":
		if isDeleteOperation(operation) || strings.TrimSpace(value) == "" {
			break
		}
		if parsed, err := url.Parse(strings.TrimSpace(value)); err == nil {
			if !parsed.IsAbs() {
				parsed = u.ResolveReference(parsed)
			}
			u = parsed
		}
	case "Path", "路径":
		if isDeleteOperation(operation) {
			u.Path = ""
			u.RawPath = ""
		} else {
			u.Path = value
			u.RawPath = ""
		}
	case "参数", "URL参数", "Query":
		if key == "" {
			break
		}
		query := u.Query()
		if isDeleteOperation(operation) {
			query.Del(key)
		} else if isAddOperation(operation) {
			query.Add(key, value)
		} else {
			query.Set(key, value)
		}
		u.RawQuery = query.Encode()
	case "协议头", "请求头", "Header":
		applyHeaderRewrite(header, operation, key, value)
	case "Body", "请求体":
		if isDeleteOperation(operation) {
			body = nil
			break
		}
		if decoded, err := decodeMappingBody(value, rewriteOperation.ValueType); err == nil {
			body = decoded
		}
	}

	return method, u, body
}

func applyResponseRewriteOperation(rewriteOperation requestRewriteOperation, response *http.Response, body []byte) []byte {
	target := strings.TrimSpace(rewriteOperation.Target)
	operation := strings.TrimSpace(rewriteOperation.Operation)
	key := strings.TrimSpace(rewriteOperation.Key)
	value := strings.ReplaceAll(rewriteOperation.Value, "\\\\", "\\")
	value = strings.ReplaceAll(value, "\\\"", "\"")

	switch target {
	case "状态码", "StatusCode":
		if isDeleteOperation(operation) {
			break
		}
		if statusCode, err := strconv.Atoi(strings.TrimSpace(value)); err == nil && statusCode > 0 {
			response.StatusCode = statusCode
			statusText := http.StatusText(statusCode)
			if statusText == "" {
				response.Status = strconv.Itoa(statusCode)
			} else {
				response.Status = strconv.Itoa(statusCode) + " " + statusText
			}
		}
	case "协议头", "响应头", "Header":
		applyHeaderRewrite(response.Header, operation, key, value)
	case "Body", "响应体":
		if isDeleteOperation(operation) {
			body = nil
		} else if decoded, err := decodeMappingBody(value, rewriteOperation.ValueType); err == nil {
			body = decoded
		}
		if response.Header != nil {
			delete(response.Header, "Content-Encoding")
			delete(response.Header, "content-encoding")
			delete(response.Header, "Transfer-Encoding")
		}
	}

	return body
}

func getRewriteOperations(rule ConfigRequestRewriteRule) []requestRewriteOperation {
	operations := make([]requestRewriteOperation, 0)
	operationsJSON := strings.TrimSpace(rule.OperationsJson)
	if operationsJSON != "" && operationsJSON != "[]" {
		_ = json.Unmarshal([]byte(operationsJSON), &operations)
	}

	filtered := make([]requestRewriteOperation, 0, len(operations))
	for _, operation := range operations {
		operation.Target = strings.TrimSpace(operation.Target)
		operation.Operation = strings.TrimSpace(operation.Operation)
		if operation.Target == "" {
			continue
		}
		if operation.Operation == "" {
			operation.Operation = "设置"
		}
		if strings.TrimSpace(operation.ValueType) == "" {
			operation.ValueType = rule.ValueType
		}
		if strings.TrimSpace(operation.ValueType) == "" {
			operation.ValueType = "String(UTF8)"
		}
		filtered = append(filtered, operation)
	}
	if len(filtered) > 0 {
		return filtered
	}

	target := strings.TrimSpace(rule.Target)
	if target == "" {
		return nil
	}
	operation := strings.TrimSpace(rule.Operation)
	if operation == "" {
		operation = "设置"
	}
	valueType := strings.TrimSpace(rule.ValueType)
	if valueType == "" {
		valueType = "String(UTF8)"
	}
	return []requestRewriteOperation{{
		Target:    target,
		Operation: operation,
		Key:       rule.Key,
		Value:     rule.Value,
		ValueType: valueType,
	}}
}

func applyHeaderRewrite(header http.Header, operation string, key string, value string) {
	if header == nil || key == "" {
		return
	}
	if isDeleteOperation(operation) {
		header.Del(key)
		return
	}
	if isAddOperation(operation) {
		header.Add(key, value)
		return
	}
	header.Set(key, value)
}

func buildRewriteAction(operations []requestRewriteOperation) string {
	if len(operations) > 1 {
		return "动作链×" + strconv.Itoa(len(operations))
	}
	if len(operations) == 0 {
		return "命中"
	}
	rewriteOperation := operations[0]
	target := strings.TrimSpace(rewriteOperation.Target)
	operation := strings.TrimSpace(rewriteOperation.Operation)
	if target == "" {
		target = "内容"
	}
	if operation == "" {
		operation = "设置"
	}
	if strings.TrimSpace(rewriteOperation.Key) == "" {
		return operation + target
	}
	return operation + target + ":" + strings.TrimSpace(rewriteOperation.Key)
}

func directionText(request bool) string {
	if request {
		return "请求"
	}
	return "响应"
}

func recordTrafficRuleHit(theology int, ruleType string, rule ConfigTrafficRuleBase, action string, direction string, rawURL string) {
	if theology < 1 {
		return
	}
	hit := MapHash.TrafficRuleHit{
		Time:      time.Now().Format("15:04:05.000"),
		Theology:  theology,
		RuleType:  ruleType,
		RuleHash:  rule.Hash,
		RuleName:  rule.Name,
		Action:    strings.TrimSpace(action),
		Direction: strings.TrimSpace(direction),
		URL:       rawURL,
	}
	if hit.RuleName == "" {
		hit.RuleName = "未命名规则"
	}
	if hit.Action == "" {
		hit.Action = "命中"
	}
	HashMap.RecordRuleHit(theology, hit)
	CallJs("规则命中", hit)
}

func isDeleteOperation(operation string) bool {
	return strings.TrimSpace(operation) == "删除"
}

func isAddOperation(operation string) bool {
	return strings.TrimSpace(operation) == "添加"
}

func cloneURL(u *url.URL) *url.URL {
	if u == nil {
		return nil
	}
	copied := *u
	return &copied
}

func mappingMethodMatches(ruleMethod string, method string) bool {
	ruleMethod = strings.TrimSpace(strings.ToUpper(ruleMethod))
	if ruleMethod == "" || ruleMethod == "ANY" {
		return true
	}
	return ruleMethod == strings.TrimSpace(strings.ToUpper(method))
}

func mappingURLMatches(matchType string, pattern string, requestURL string) bool {
	pattern = strings.TrimSpace(pattern)
	if pattern == "" || pattern == "*" {
		return true
	}

	switch strings.TrimSpace(matchType) {
	case "等于":
		return strings.EqualFold(requestURL, pattern)
	case "正则":
		regex, err := regexp.Compile(pattern)
		return err == nil && regex.MatchString(requestURL)
	case "通配":
		regexText := "^" + strings.ReplaceAll(regexp.QuoteMeta(pattern), "\\*", ".*") + "$"
		regex, err := regexp.Compile(regexText)
		return err == nil && regex.MatchString(requestURL)
	default:
		return strings.Contains(strings.ToLower(requestURL), strings.ToLower(pattern))
	}
}

func decodeMappingBody(value string, valueType string) ([]byte, error) {
	value = strings.ReplaceAll(value, "\\\\", "\\")
	value = strings.ReplaceAll(value, "\\\"", "\"")
	switch strings.TrimSpace(valueType) {
	case "Base64":
		return base64.StdEncoding.DecodeString(value)
	case "HEX":
		return hex.DecodeString(value)
	case "String(GBK)":
		return Utf8ToGBK([]byte(value)), nil
	default:
		return []byte(value), nil
	}
}
func ReplaceHeader(header http.Header) {
	if header == nil {
		return
	}
	for key, values := range header {
		for i, value := range values {
			header[key][i] = string(ReplaceBody([]byte(value)))
		}
	}
}

func ReplaceBody(b []byte) []byte {
	ur := string(b)
	_TmpLock.Lock()
	defer _TmpLock.Unlock()
	for i := 0; i < len(_ReplaceRules); i++ {
		if _ReplaceRules[i].Type == ReplaceRulesType_Bytes {
			if strings.Contains(ur, string(_ReplaceRules[i].source)) {
				ur = strings.ReplaceAll(ur, string(_ReplaceRules[i].source), string(_ReplaceRules[i].target))
			}
		}
	}
	return []byte(ur)
}
