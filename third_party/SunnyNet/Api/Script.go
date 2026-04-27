package Api

import "C"
import (
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/JsCall"
)

func ScriptCall(i int, Request string) uintptr {
	b := JsCall.JsCall(int32(i), Request)
	return public.PointerPtr(b)
}

func SetScript(Request string) uintptr {
	return JsCall.JsInit(Request)
}

func SetScriptLogCallAddress(i int) {
	JsCall.ConsoleLogCall = i
}
