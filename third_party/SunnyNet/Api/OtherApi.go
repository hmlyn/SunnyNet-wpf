package Api

import (
	"bytes"
	"encoding/binary"
	"github.com/qtgolang/SunnyNet/Call"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/encoding/hex"
	"strings"
)

//HexDump
/*  字节数组转字符串 返回格式如下
00000000  53 75 6E 6E 79 4E 65 74  54 65 73 74 45 78 61 6D  |SunnyNetTestExam|
00000010  70 6C 65                                          |ple|
*/
func HexDump(data uintptr, dataLen int) uintptr {
	bin := public.CStringToBytes(data, dataLen)
	hexStr := strings.ReplaceAll(hex.Dump(bin), "\n", "\r\n")
	return public.PointerPtr(hexStr)
}

//BytesToInt 将Go int的Bytes 转为int
func BytesToInt(data uintptr, dataLen int) int {
	bys := public.CStringToBytes(data, dataLen)
	buff := bytes.NewBuffer(bys)
	var B int64
	_ = binary.Read(buff, binary.BigEndian, &B)
	return int(B)
}

//GoCall 适配火山PC CALL 火山直接CALL X64没有问题，X86环境下有问题，所以搞了这个命令
func GoCall(address, a1, a2, a3, a4, a5, a6, a7, a8, a9 int) int {
	if a1 == -1 {
		return Call.Call(address)
	}
	if a2 == -1 {
		return Call.Call(address, a1)
	}
	if a3 == -1 {
		return Call.Call(address, a1, a2)
	}
	if a4 == -1 {
		return Call.Call(address, a1, a2, a3)
	}
	if a5 == -1 {
		return Call.Call(address, a1, a2, a3, a4)
	}
	if a6 == -1 {
		return Call.Call(address, a1, a2, a3, a4, a5)
	}
	if a7 == -1 {
		return Call.Call(address, a1, a2, a3, a4, a5, a6)
	}
	if a8 == -1 {
		return Call.Call(address, a1, a2, a3, a4, a5, a6, a7)
	}
	if a9 == -1 {
		return Call.Call(address, a1, a2, a3, a4, a5, a6, a7, a8)
	}
	return Call.Call(address, a1, a2, a3, a4, a5, a6, a7, a8, a9)
}
