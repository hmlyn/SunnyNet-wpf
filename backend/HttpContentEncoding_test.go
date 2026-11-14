package main

import (
	"bytes"
	"io"
	"net/http"
	"strconv"
	"strings"
	"testing"

	"github.com/klauspost/compress/zstd"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
)

func TestHTTPCallbackZstdResponse(t *testing.T) {
	_TmpLock.Lock()
	savedRuleCenter := GlobalConfig.RuleCenter
	savedReplaceRules := _ReplaceRules
	GlobalConfig.RuleCenter = ConfigRuleCenter{}
	_ReplaceRules = nil
	_TmpLock.Unlock()
	t.Cleanup(func() {
		_TmpLock.Lock()
		GlobalConfig.RuleCenter = savedRuleCenter
		_ReplaceRules = savedReplaceRules
		_TmpLock.Unlock()
	})

	encoder, err := zstd.NewWriter(nil, zstd.WithEncoderConcurrency(1), zstd.WithZeroFrames(true))
	if err != nil {
		t.Fatal(err)
	}
	defer encoder.Close()

	jsonBody := []byte(`{"devices":[{"id":42,"online":true}],"total":1}`)
	compressed := encoder.EncodeAll(jsonBody, nil)
	invalid := []byte("invalid zstd response")
	truncated := compressed[:len(compressed)-2]

	for _, test := range []struct {
		name         string
		headerName   string
		encoding     string
		body         []byte
		wantBody     []byte
		wantEncoding string
	}{
		{"json", "Content-Encoding", "zstd", compressed, jsonBody, ""},
		{"lowercase header", "content-encoding", "zstd", compressed, jsonBody, ""},
		{"uppercase encoding", "Content-Encoding", "ZSTD", compressed, jsonBody, ""},
		{"empty frame", "Content-Encoding", "zstd", encoder.EncodeAll(nil, nil), nil, ""},
		{"invalid body", "Content-Encoding", "zstd", invalid, invalid, "zstd"},
		{"truncated frame", "Content-Encoding", "zstd", truncated, truncated, "zstd"},
	} {
		t.Run(test.name, func(t *testing.T) {
			request, err := http.NewRequest(http.MethodGet, "https://example.test/api/devices", nil)
			if err != nil {
				t.Fatal(err)
			}
			response := &http.Response{
				StatusCode: http.StatusOK,
				Header: http.Header{
					test.headerName:  {test.encoding},
					"Content-Type":   {"application/json; charset=utf-8"},
					"Content-Length": {strconv.Itoa(len(test.body))},
				},
				Body:          io.NopCloser(bytes.NewReader(test.body)),
				ContentLength: int64(len(test.body)),
			}
			conn := &SunnyNet.HttpConn{
				Theology: -1,
				Type:     public.HttpResponseOK,
				Request:  request,
				Response: response,
			}

			HttpCallback(conn)
			defer response.Body.Close()
			got, err := io.ReadAll(response.Body)
			if err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(got, test.wantBody) {
				t.Errorf("response body = %x, want %x", got, test.wantBody)
			}
			if got := strings.Join(response.Header[test.headerName], ","); got != test.wantEncoding {
				t.Errorf("Content-Encoding = %q, want %q", got, test.wantEncoding)
			}
			if got := response.Header.Get("Content-Length"); got != strconv.Itoa(len(test.wantBody)) {
				t.Errorf("Content-Length = %q, want %d", got, len(test.wantBody))
			}
			if response.ContentLength != int64(len(test.wantBody)) {
				t.Errorf("response ContentLength = %d, want %d", response.ContentLength, len(test.wantBody))
			}
			if got := response.Header.Get("Content-Type"); got != "application/json; charset=utf-8" {
				t.Errorf("Content-Type = %q, want application/json; charset=utf-8", got)
			}
		})
	}
}
