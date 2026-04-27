package Api

import (
	"github.com/qtgolang/SunnyNet/public"
	NFapi "github.com/qtgolang/SunnyNet/src/nfapi"
)

func SetUdpData(MessageId int, data []byte) bool {
	NFapi.UdpSync.Lock()
	buff := NFapi.UdpMap[MessageId]
	if buff != nil {
		buff.Reset()
		buff.Write(data)
		NFapi.UdpSync.Unlock()
		return true
	}
	NFapi.UdpSync.Unlock()
	return false
}
func GetUdpData(MessageId int) uintptr {
	NFapi.UdpSync.Lock()
	buff := NFapi.UdpMap[MessageId]
	if buff != nil {
		NFapi.UdpSync.Unlock()
		bx := buff.Bytes()
		if len(bx) < 1 {
			return 0
		}
		u := public.PointerPtr(public.BytesCombine(public.IntToBytes(len(bx)), bx))
		return u
	}
	NFapi.UdpSync.Unlock()
	return 0
}

func UdpSendToServer(tid int, data []byte) bool {
	return NFapi.UdpSendToServer(int64(tid), data)
}
func UdpSendToClient(tid int, data []byte) bool {
	return NFapi.UdpSendToClient(int64(tid), data)
}
