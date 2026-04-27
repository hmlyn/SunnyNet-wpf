package Api

import "C"
import (
	"encoding/json"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/protobuf"
	"strings"
	"unsafe"
)

func PbToJson(bin uintptr, binLen int) uintptr {
	data := public.CStringToBytes(bin, binLen)
	defer func() {
		if err := recover(); err != nil {
		}
	}()
	var msg protobuf.Message
	msg.Unmarshal(data)
	b, e := json.Marshal(msg)
	if e != nil {
		return uintptr(unsafe.Pointer(C.CString("")))
	}
	PJson, _ := protobuf.ParseJson(string(b), "")
	s, _ := json.MarshalIndent(PJson, "", "\t")
	ss := string(s)
	ss = strings.ReplaceAll(ss, "\n", "\r\n")
	n := C.CString(ss)
	return uintptr(unsafe.Pointer(n))
}

func JsonToPB(bin uintptr, binLen int) uintptr {
	data := string(public.CStringToBytes(bin, binLen))
	defer func() {
		if err := recover(); err != nil {
		}
	}()
	b := protobuf.Marshal(data)
	if len(b) < 1 {
		return 0
	}
	c := public.BytesCombine(public.Int64ToBytes(int64(len(b))), b)
	return public.PointerPtr(c)
}
