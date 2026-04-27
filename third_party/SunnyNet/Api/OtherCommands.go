package Api

import (
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"strconv"
)

const (
	//OtherCommandDisable_TCP 禁用TCP 返回0失败 返回1成功  [参数1 SunnyNetContext 参数2 是否禁用]
	OtherCommandDisable_TCP = uintptr(1001)
	// OtherCommandRandomTLSSet  是否使用随机TLS指纹 注意如果关闭后将会同时取消设置的固定TLS指纹[参数1 SunnyNetContext 参数2 是否打开]
	OtherCommandRandomTLSSet = uintptr(1002)
	//OtherCommandRandomFixedTLSSet 使用固定的TLS指纹 [参数1 SunnyNetContext 参数2 RandomTLSList]
	OtherCommandRandomFixedTLSSet = uintptr(1003)
	//OtherCommandRandomFixedTLSGet 是否使用固定的TLS指纹 [参数1 SunnyNetContext] 返回 String
	OtherCommandRandomFixedTLSGet = uintptr(1004)
	//OtherCommandHttpClientRandomTLS  HTTP 客户端 设置随机使用TLS指纹 [参数1 Context]
	OtherCommandHttpClientRandomTLS = uintptr(1005)
)

func OtherCommands(Cmd uintptr, Command ...uintptr) uintptr {
	switch Cmd {
	case OtherCommandHttpClientRandomTLS:
		{
			Context := int(Command[0])
			open := int(Command[1]) == 1
			if HTTPSetRandomTLS(Context, open) {
				return 1
			}
		}
		return 0
	case OtherCommandDisable_TCP, OtherCommandRandomTLSSet, OtherCommandRandomFixedTLSSet, OtherCommandRandomFixedTLSGet:
		{
			if len(Command) < 1 {
				return 0
			}
			SunnyContext := int(Command[0])
			state := int(Command[1]) == 1
			SunnyNet.SunnyStorageLock.Lock()
			w := SunnyNet.SunnyStorage[SunnyContext]
			SunnyNet.SunnyStorageLock.Unlock()
			if w == nil {
				return 0
			}
			switch Cmd {
			case OtherCommandDisable_TCP:
				w.DisableTCP(state)
				return 1
			case OtherCommandRandomTLSSet:
				w.SetRandomTLS(state)
				return 1
			case OtherCommandRandomFixedTLSSet:
				w.SetRandomFixedTLS(string(public.CStringToBytes(Command[1], int(Command[2]))))
				return 1
			case OtherCommandRandomFixedTLSGet:
				r := w.GetTLSTestValues()
				s := ""
				for _, v := range r {
					if s == "" {
						s = strconv.Itoa(int(v))
					} else {
						s += "," + strconv.Itoa(int(v))
					}
				}
				return public.PointerPtr(s)
			}
		}
		return 0

	}
	return 0
}
