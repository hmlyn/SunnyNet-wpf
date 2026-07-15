package main

import (
	"encoding/json"
	"strconv"
	"strings"
)

func applyJSONBodyRewrite(body []byte, operation string, jsonPath string, value string) ([]byte, bool) {
	segments := splitJSONPath(jsonPath)
	if len(body) == 0 || len(segments) == 0 {
		return body, false
	}

	parentStart := skipJSONSpaces(body, 0)
	if parentStart >= len(body) || body[parentStart] != '{' {
		return body, false
	}

	for _, segment := range segments[:len(segments)-1] {
		_, _, valueStart, _, found := findJSONObjectMember(body, parentStart, segment)
		if !found {
			return body, false
		}

		parentStart = skipJSONSpaces(body, valueStart)
		if parentStart >= len(body) || body[parentStart] != '{' {
			return body, false
		}
	}

	memberStart, memberEnd, valueStart, valueEnd, found := findJSONObjectMember(body, parentStart, segments[len(segments)-1])
	if !found {
		return body, false
	}

	if isJSONDeleteOperation(operation) {
		deleteStart, deleteEnd := expandJSONMemberDeleteRange(body, memberStart, memberEnd)
		rewritten := make([]byte, 0, len(body)-(deleteEnd-deleteStart))
		rewritten = append(rewritten, body[:deleteStart]...)
		rewritten = append(rewritten, body[deleteEnd:]...)
		return rewritten, true
	}

	rawValue := normalizeJSONRewriteValue(value)
	rewritten := make([]byte, 0, len(body)-(valueEnd-valueStart)+len(rawValue))
	rewritten = append(rewritten, body[:valueStart]...)
	rewritten = append(rewritten, rawValue...)
	rewritten = append(rewritten, body[valueEnd:]...)
	return rewritten, true
}

func splitJSONPath(path string) []string {
	parts := strings.Split(strings.TrimSpace(path), ".")
	segments := make([]string, 0, len(parts))
	for _, part := range parts {
		part = strings.TrimSpace(part)
		if part != "" {
			segments = append(segments, part)
		}
	}
	return segments
}

func normalizeJSONRewriteValue(value string) []byte {
	trimmed := strings.TrimSpace(value)
	if trimmed != "" && json.Valid([]byte(trimmed)) {
		return []byte(trimmed)
	}

	encoded, err := json.Marshal(value)
	if err != nil {
		return []byte(`""`)
	}
	return encoded
}

func findJSONObjectMember(source []byte, objectStart int, key string) (int, int, int, int, bool) {
	position := skipJSONSpaces(source, objectStart)
	if position >= len(source) || source[position] != '{' {
		return 0, 0, 0, 0, false
	}

	position++
	for {
		position = skipJSONSpaces(source, position)
		if position >= len(source) || source[position] == '}' {
			return 0, 0, 0, 0, false
		}

		memberStart := position
		parsedKey, keyEnd, ok := parseJSONString(source, position)
		if !ok {
			return 0, 0, 0, 0, false
		}

		position = skipJSONSpaces(source, keyEnd)
		if position >= len(source) || source[position] != ':' {
			return 0, 0, 0, 0, false
		}

		valueStart := skipJSONSpaces(source, position+1)
		valueEnd, ok := skipJSONValue(source, valueStart)
		if !ok {
			return 0, 0, 0, 0, false
		}

		if parsedKey == key {
			return memberStart, valueEnd, valueStart, valueEnd, true
		}

		position = skipJSONSpaces(source, valueEnd)
		if position < len(source) && source[position] == ',' {
			position++
			continue
		}

		if position < len(source) && source[position] == '}' {
			return 0, 0, 0, 0, false
		}

		return 0, 0, 0, 0, false
	}
}

func parseJSONString(source []byte, start int) (string, int, bool) {
	end, ok := findJSONStringEnd(source, start)
	if !ok {
		return "", 0, false
	}

	unquoted, err := strconv.Unquote(string(source[start:end]))
	if err != nil {
		return "", 0, false
	}

	return unquoted, end, true
}

func findJSONStringEnd(source []byte, start int) (int, bool) {
	if start >= len(source) || source[start] != '"' {
		return 0, false
	}

	escaped := false
	for index := start + 1; index < len(source); index++ {
		current := source[index]
		if escaped {
			escaped = false
			continue
		}

		if current == '\\' {
			escaped = true
			continue
		}

		if current == '"' {
			return index + 1, true
		}
	}

	return 0, false
}

func skipJSONValue(source []byte, start int) (int, bool) {
	position := skipJSONSpaces(source, start)
	if position >= len(source) {
		return 0, false
	}

	switch source[position] {
	case '"':
		return findJSONStringEnd(source, position)
	case '{', '[':
		return skipJSONContainer(source, position)
	default:
		for position < len(source) {
			switch source[position] {
			case ',', '}', ']':
				return position, true
			case ' ', '\t', '\r', '\n':
				return position, true
			default:
				position++
			}
		}
		return position, true
	}
}

func skipJSONContainer(source []byte, start int) (int, bool) {
	depth := 0
	for position := start; position < len(source); position++ {
		switch source[position] {
		case '"':
			end, ok := findJSONStringEnd(source, position)
			if !ok {
				return 0, false
			}
			position = end - 1
		case '{', '[':
			depth++
		case '}', ']':
			depth--
			if depth == 0 {
				return position + 1, true
			}
			if depth < 0 {
				return 0, false
			}
		}
	}

	return 0, false
}

func skipJSONSpaces(source []byte, start int) int {
	for start < len(source) {
		switch source[start] {
		case ' ', '\t', '\r', '\n':
			start++
		default:
			return start
		}
	}
	return start
}

func expandJSONMemberDeleteRange(source []byte, memberStart int, memberEnd int) (int, int) {
	afterMember := skipJSONSpaces(source, memberEnd)
	if afterMember < len(source) && source[afterMember] == ',' {
		return memberStart, afterMember + 1
	}

	beforeMember := memberStart - 1
	for beforeMember >= 0 {
		switch source[beforeMember] {
		case ' ', '\t', '\r', '\n':
			beforeMember--
			continue
		case ',':
			return beforeMember, memberEnd
		default:
			return memberStart, memberEnd
		}
	}

	return memberStart, memberEnd
}

func isJSONDeleteOperation(operation string) bool {
	switch strings.TrimSpace(operation) {
	case "删除", "Delete", "delete", "Remove", "remove":
		return true
	default:
		return false
	}
}
