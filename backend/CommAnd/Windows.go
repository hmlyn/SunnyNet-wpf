//go:build windows
// +build windows

package CommAnd

import (
	"bufio"
	"bytes"
	"fmt"
	"github.com/Trisia/gosysproxy"
	gops "github.com/mitchellh/go-ps"
	"github.com/qtgolang/SunnyNet/public"
	"golang.org/x/text/encoding/simplifiedchinese"
	"io"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"unicode/utf16"
	"unsafe"
)

var UserSelectPath = ""

func GetDesktopPath() (string, error) {
	if UserSelectPath != "" {
		return UserSelectPath, nil
	}
	dir, E := filepath.Abs(filepath.Dir(os.Args[0]))
	return strings.Replace(dir, "\\", "/", -1), E
}

var pidLock sync.Mutex

func GetPidName(pid int) string {
	if pid < 1 {
		return "代理"
	}
	pidLock.Lock()
	defer pidLock.Unlock()
	process, err := gops.FindProcess(pid)
	if err != nil {
		return strconv.Itoa(pid)
	}
	if process == nil {
		return strconv.Itoa(pid)
	}
	return strconv.Itoa(pid) + ":" + process.Executable()
}

func SetIEProxy(Set bool, Port int) bool {
	if !Set {
		_ = gosysproxy.Off()
		return true
	}
	ies := "127.0.0.1:" + strconv.Itoa(Port)
	_ = gosysproxy.SetGlobalProxy("http="+ies+";https="+ies, "")
	return true
}

func EnumerateProcesses() map[int]string {
	res := make(map[int]string)
	processes, err := gops.Processes()
	if err != nil {
		return res
	}

	// 遍历每个进程并输出 PID 和进程名
	for _, process := range processes {
		res[process.Pid()] = process.Executable()
	}
	return res
}

// InstallCert 安装证书 将证书安装到Windows系统内
func InstallCert(certificates []byte) string {
	path, err := os.Getwd()
	if err != nil {
		return err.Error()
	}
	err = public.WriteBytesToFile(certificates, path+"\\ca.crt")
	if err != nil {
		return err.Error()
	}
	var args []string
	args = append(args, "-addstore")
	args = append(args, "root")
	args = append(args, path+"\\ca.crt")
	defer func() { _ = public.RemoveFile(path + "\\ca.crt") }()
	cmd := exec.Command("certutil", args...)
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return err.Error()
	}
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	_ = cmd.Start()
	var Buff bytes.Buffer
	reader := bufio.NewReader(stdout)
	for {
		line, err2 := reader.ReadBytes('\n')
		if err2 != nil || io.EOF == err2 {
			break
		}
		Buff.Write(line)
	}
	utf8Bytes, err := simplifiedchinese.GBK.NewDecoder().Bytes(Buff.Bytes())
	if err == nil {
		return string(utf8Bytes)
	}
	return Buff.String()
}
func GetWayArray() []string {
	var ipArray []string
	interfaces, err := net.Interfaces()
	if err != nil {
		return ipArray
	}
	for _, face := range interfaces {
		adders, err1 := face.Addrs()
		if err1 != nil {
			continue
		}
		for _, addr := range adders {
			ipNet, ok := addr.(*net.IPNet)
			if ok && !ipNet.IP.IsLoopback() && ipNet.IP.To4() != nil {
				ipArray = append(ipArray, ipNet.IP.String())
			}
		}
	}
	return ipArray
}
func ClipboardText(text string) error {
	return setClipboardTextByWinAPI(text)
}

const (
	cfUnicodeText = 13
	gmemMoveable  = 0x0002
	gmemZeroInit  = 0x0040
)

var (
	user32             = syscall.NewLazyDLL("user32.dll")
	kernel32           = syscall.NewLazyDLL("kernel32.dll")
	procOpenClipboard  = user32.NewProc("OpenClipboard")
	procEmptyClipboard = user32.NewProc("EmptyClipboard")
	procSetClipboard   = user32.NewProc("SetClipboardData")
	procCloseClipboard = user32.NewProc("CloseClipboard")
	procGlobalAlloc    = kernel32.NewProc("GlobalAlloc")
	procGlobalLock     = kernel32.NewProc("GlobalLock")
	procGlobalUnlock   = kernel32.NewProc("GlobalUnlock")
	procGlobalFree     = kernel32.NewProc("GlobalFree")
)

func setClipboardTextByWinAPI(text string) error {
	ok, _, openErr := procOpenClipboard.Call(0)
	if ok == 0 {
		return winAPICallError("OpenClipboard", openErr)
	}

	var memory uintptr
	transferred := false
	defer func() {
		procCloseClipboard.Call()
		if !transferred && memory != 0 {
			procGlobalFree.Call(memory)
		}
	}()

	ok, _, emptyErr := procEmptyClipboard.Call()
	if ok == 0 {
		return winAPICallError("EmptyClipboard", emptyErr)
	}

	utf16Text := utf16.Encode([]rune(text + "\x00"))
	size := uintptr(len(utf16Text) * 2)
	memory, _, allocErr := procGlobalAlloc.Call(gmemMoveable|gmemZeroInit, size)
	if memory == 0 {
		return winAPICallError("GlobalAlloc", allocErr)
	}

	pointer, _, lockErr := procGlobalLock.Call(memory)
	if pointer == 0 {
		return winAPICallError("GlobalLock", lockErr)
	}
	copy(unsafe.Slice((*uint16)(unsafe.Pointer(pointer)), len(utf16Text)), utf16Text)
	procGlobalUnlock.Call(memory)

	result, _, setErr := procSetClipboard.Call(cfUnicodeText, memory)
	if result == 0 {
		return winAPICallError("SetClipboardData", setErr)
	}
	transferred = true
	return nil
}

func winAPICallError(apiName string, err error) error {
	if err == syscall.Errno(0) {
		return fmt.Errorf("%s 失败", apiName)
	}
	return fmt.Errorf("%s 失败: %w", apiName, err)
}
