//go:build cshared

package main

/*
#include <stdlib.h>
*/
import "C"

import (
	"encoding/json"
	"fmt"
	"time"
	"unsafe"
)

type bridgeResponse struct {
	OK   bool `json:"ok"`
	Data any  `json:"data,omitempty"`
	Err  any  `json:"err,omitempty"`
}

func makeCString(value string) *C.char {
	if value == "" {
		return nil
	}
	return C.CString(value)
}

func encodeResponse(ok bool, data any, err any) *C.char {
	payload, marshalErr := json.Marshal(bridgeResponse{
		OK:   ok,
		Data: data,
		Err:  err,
	})
	if marshalErr != nil {
		fallback := fmt.Sprintf(`{"ok":false,"err":%q}`, marshalErr.Error())
		return C.CString(fallback)
	}
	return C.CString(string(payload))
}

//export SunnyNetInvoke
func SunnyNetInvoke(requestJSON *C.char) (result *C.char) {
	defer func() {
		if rec := recover(); rec != nil {
			result = encodeResponse(false, nil, fmt.Sprintf("%v", rec))
		}
	}()

	if requestJSON == nil {
		return encodeResponse(false, nil, "request is empty")
	}

	raw := C.GoString(requestJSON)
	if raw == "" {
		return encodeResponse(false, nil, "request is empty")
	}

	var request map[string]any
	if err := json.Unmarshal([]byte(raw), &request); err != nil {
		return encodeResponse(false, nil, err.Error())
	}

	return encodeResponse(true, app.Do(request), nil)
}

//export SunnyNetPollEvent
func SunnyNetPollEvent(timeoutMs C.int) *C.char {
	timeout := time.Duration(timeoutMs) * time.Millisecond
	data := pollBridgeMessage(timeout)
	if len(data) == 0 {
		return nil
	}
	return C.CString(string(data))
}

//export SunnyNetShutdown
func SunnyNetShutdown() {
	shutdownApp()
}

//export SunnyNetFreeString
func SunnyNetFreeString(value *C.char) {
	if value == nil {
		return
	}
	C.free(unsafe.Pointer(value))
}
