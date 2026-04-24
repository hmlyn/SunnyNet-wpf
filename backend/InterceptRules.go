package main

import (
	"changeme/MapHash"
	"github.com/qtgolang/SunnyNet/src/protobuf/JSON"
	"net/http"
	"net/url"
	"regexp"
	"strconv"
	"strings"
)

func InterceptRulesEvent(command string, args *JSON.SyJson) any {
	switch command {
	case "保存拦截规则":
		_TmpLock.Lock()
		defer _TmpLock.Unlock()

		rules := make([]ConfigInterceptRule, 0, args.GetNum("Data"))
		for i := 0; i < args.GetNum("Data"); i++ {
			prefix := "Data[" + strconv.Itoa(i) + "]."
			rule := ConfigInterceptRule{
				Hash:      args.GetData(prefix + "Hash"),
				Enable:    args.GetData(prefix+"Enabled") == "true" || args.GetData(prefix+"Enable") == "true",
				Name:      args.GetData(prefix + "Name"),
				Direction: args.GetData(prefix + "Direction"),
				Target:    args.GetData(prefix + "Target"),
				Operator:  args.GetData(prefix + "Operator"),
				Value:     args.GetData(prefix + "Value"),
			}
			if rule.Hash == "" {
				rule.Hash = strconv.Itoa(i + 1)
			}
			if rule.Direction == "" {
				rule.Direction = "上行"
			}
			if rule.Target == "" {
				rule.Target = "URL"
			}
			if rule.Operator == "" {
				rule.Operator = "包含"
			}
			rules = append(rules, rule)
		}

		GlobalConfig.InterceptRules = rules
		_ = GlobalConfig.saveToFile()
		return true
	}
	return false
}

func MatchInterceptRequest(h *MapHash.Request) bool {
	return matchInterceptRules(h, true)
}

func MatchInterceptResponse(h *MapHash.Request) bool {
	return matchInterceptRules(h, false)
}

func matchInterceptRules(h *MapHash.Request, request bool) bool {
	if h == nil {
		return false
	}

	_TmpLock.Lock()
	rules := append([]ConfigInterceptRule(nil), GlobalConfig.InterceptRules...)
	_TmpLock.Unlock()

	for _, rule := range rules {
		if !rule.Enable || strings.TrimSpace(rule.Value) == "" || !interceptDirectionMatches(rule.Direction, request) {
			continue
		}
		if interceptRuleMatches(h, rule, request) {
			return true
		}
	}
	return false
}

func interceptDirectionMatches(direction string, request bool) bool {
	switch strings.TrimSpace(direction) {
	case "上行":
		return request
	case "下行":
		return !request
	case "上下行", "双向", "全部":
		return true
	default:
		return request
	}
}

func interceptRuleMatches(h *MapHash.Request, rule ConfigInterceptRule, request bool) bool {
	text := interceptRuleText(h, rule.Target, request)
	value := strings.TrimSpace(rule.Value)
	switch strings.TrimSpace(rule.Operator) {
	case "等于":
		return strings.EqualFold(strings.TrimSpace(text), value)
	case "正则":
		regex, err := regexp.Compile(value)
		return err == nil && regex.MatchString(text)
	default:
		return strings.Contains(strings.ToLower(text), strings.ToLower(value))
	}
}

func interceptRuleText(h *MapHash.Request, target string, request bool) string {
	parsed, _ := url.Parse(h.URL)
	switch strings.TrimSpace(target) {
	case "Host":
		if parsed != nil {
			return parsed.Host
		}
		return ""
	case "Method":
		return h.Method
	case "Header":
		if request {
			return headerToText(h.Header)
		}
		return headerToText(h.Response.Header)
	case "Query", "参数":
		if parsed != nil {
			return parsed.RawQuery
		}
		return ""
	case "Cookie":
		if request {
			return h.Header.Get("Cookie")
		}
		return strings.Join(h.Response.Header.Values("Set-Cookie"), "\n")
	case "Body", "包体":
		if request {
			return string(h.Body)
		}
		return string(h.Response.Body)
	case "StatusCode", "状态码":
		if request || h.Response.StateCode == 0 {
			return ""
		}
		return strconv.Itoa(h.Response.StateCode)
	default:
		return h.URL
	}
}

func headerToText(header http.Header) string {
	if len(header) == 0 {
		return ""
	}
	var builder strings.Builder
	for name, values := range header {
		for _, value := range values {
			builder.WriteString(name)
			builder.WriteString(": ")
			builder.WriteString(value)
			builder.WriteByte('\n')
		}
	}
	return builder.String()
}
