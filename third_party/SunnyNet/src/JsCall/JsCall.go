package JsCall

import (
	"crypto"
	"encoding/hex"
	"fmt"
	"github.com/qtgolang/SunnyNet/Call"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/GoWinHttp"
	"github.com/robertkrimen/otto"
	"golang.org/x/text/encoding/simplifiedchinese"
	"io"
	"io/ioutil"
	"os"
	"strconv"
	"strings"
	"sync"
	"unicode/utf8"
)

func ClearBr(a string) string {
	aa := strings.ReplaceAll(a, "\r", public.NULL)
	aa = strings.ReplaceAll(aa, "\n", public.CRLF)
	for i := 0; i < len(aa); i++ {
		aa = strings.ReplaceAll(aa, public.CRLF+public.CRLF, public.CRLF)
	}
	if len(aa) >= 2 && aa[0:2] == public.CRLF {
		aa = aa[2:]
	}
	if len(aa) >= 2 && aa[:len(aa)-2] == public.CRLF {
		aa = aa[:len(aa)-2]
	}
	return aa
}
func ReadFile(Filename string) string {
	name := strings.ReplaceAll(Filename, "\"", public.NULL)
	b, err := ioutil.ReadFile(name)
	if err != nil {
		return ""
	}
	return string(b)
}
func CheckFileIsExist(filename string) bool {
	var exist = true
	if _, err := os.Stat(filename); os.IsNotExist(err) {
		exist = false
	}
	return exist
}
func WriteFile(Filename, b string) bool {
	var f *os.File
	var err error
	//文件是否存在
	if CheckFileIsExist(Filename) {
		//存在 删除
		err = os.Remove(Filename)
		if err != nil {
			return false
		}
	}
	//创建文件
	f, err = os.Create(Filename)
	if err != nil {
		return false
	}
	defer func() { _ = f.Close() }()
	// 写入
	_, err = f.Write([]byte(b))
	if err != nil {
		return false
	}
	return true
}
func MD5(a string) string {
	hash := crypto.MD5.New()
	hash.Write([]byte(a))
	return hex.EncodeToString(hash.Sum(nil))
}

var JSlog sync.Mutex

func preNUm(data byte) int {
	str := fmt.Sprintf("%b", data)
	var i = 0
	for i < len(str) {
		if str[i] != '1' {
			break
		}
		i++
	}
	return i
}
func isUtf8(data []byte) bool {
	var f = true
	var dl = len(data)
	for i := 0; i < dl; {
		if data[i]&0x80 == 0x00 {
			i++
			if f {
				f = false
			}
			continue
		} else if num := preNUm(data[i]); num > 2 {
			i++
			for j := 0; j < num-1; j++ {
				if i >= dl {
					return false
				}
				//判断后面的 num - 1 个字节是不是都是 10 开头
				if data[i]&0xc0 != 0x80 {
					return false
				}
				i++

			}
		} else {
			//其他情况说明不是utf-8
			//
			return utf8.Valid(data)
		}
	}
	if !f {
		return utf8.Valid(data)
	}
	return f
}
func AuthGBK(s string) string {
	if isUtf8([]byte(s)) {
		return ToGBK(s)
	}
	return s
}
func Log(arg ...string) {
	//fmt.Println(arg)
	if ConsoleLogCall > 0 {
		Str := public.NULL
		for _, name := range arg {
			Str += AuthGBK(name) + ","
		}
		if len(Str) > 0 {
			Str = Str[0 : len(Str)-1]
		}
		JSlog.Lock()
		Call.Call(ConsoleLogCall, Str)
		JSlog.Unlock()
	}
}
func ToUTF8(a string) string {
	r, e := simplifiedchinese.GBK.NewDecoder().Bytes([]byte(a))
	if e != nil {
		return a
	}
	return string(r)
}
func ToGBK(a string) string {
	r, e := simplifiedchinese.GBK.NewEncoder().Bytes([]byte(a))
	if e != nil {
		return a
	}
	return string(r)
}
func HttpPost(url, heads, data string) string {
	return httpSend(public.HttpMethodPOST, url, heads, data)
}
func HttpGet(url, heads string) string {
	return httpSend(public.HttpMethodGET, url, heads, public.NULL)
}
func httpSend(mod, url, heads, data string) string {
	win := GoWinHttp.NewGoWinHttp()
	win.Open(mod, url)
	s1 := heads
	arr := strings.Split(s1, "\r\n")
	if len(arr) > 0 {
		for _, v := range arr {
			arr2 := strings.Split(v, ": ")
			if len(arr2) >= 1 {
				if len(v) >= len(arr2[0])+1 {
					win.SetHeader(arr2[0], strings.TrimSpace(v[len(arr2[0])+1:]))
				}
			}
		}
	}

	var b []byte
	r, _ := win.Send(data)
	if r.Body != nil {
		b, _ = io.ReadAll(r.Body)
		_ = r.Body.Close()
	}
	return string(b)
}
func THttpPost(url, heads, data string) {
	go httpSend(public.HttpMethodPOST, url, heads, data)
}
func THttpGet(url, heads string) {
	go httpSend(public.HttpMethodGET, url, heads, public.NULL)
}
func replace(s string) string {
	f := strings.ReplaceAll(s, "\\", "\\\\")
	f = strings.ReplaceAll(f, "/", "\\/")
	f = strings.ReplaceAll(f, "\r", "")
	f = strings.ReplaceAll(f, "\n", "\\r\\n")
	f = strings.ReplaceAll(f, "\"", "\\\"")
	f = strings.ReplaceAll(f, "\t", "\\t")
	f = strings.ReplaceAll(f, "\f", "\\f")
	f = strings.ReplaceAll(f, "\b", "\\b")
	return f
}

var JSLock sync.Mutex
var JsVm *otto.Otto

func init() {
	JsInit(JSCode)
}
func JsInit(jsCode string) uintptr {
	JSLock.Lock()
	defer JSLock.Unlock()
	vm := otto.New()

	_, err := vm.Run(JavaScript + jsCode)
	if err != nil {
		r := err.Error()
		s := public.SubString(r, "Line ", ":")
		if s == "" {
			return public.PointerPtr(r)
		}
		line, e := strconv.Atoi(s)
		if e != nil {
			return public.PointerPtr(r)
		}
		z := public.SubString(r, "Line "+s+":", " ")
		if z == "" {
			return public.PointerPtr(r)
		}
		line2, e := strconv.Atoi(s)
		if e != nil {
			return public.PointerPtr(r)
		}
		return public.PointerPtr("在第" + strconv.Itoa(line-51) + "行," + strconv.Itoa(line2) + "字符处有错误,请检查！")
	}
	_ = vm.Set("ClearBr", ClearBr)
	_ = vm.Set("ReadFile", ReadFile)
	_ = vm.Set("MD5", MD5)
	_ = vm.Set("Log", Log)
	_ = vm.Set("ToUtf8", ToUTF8)
	_ = vm.Set("ToGbk", ToGBK)
	_ = vm.Set("WriteFile", WriteFile)
	_ = vm.Set("HttpPost", HttpPost)
	_ = vm.Set("HttpGet", HttpGet)
	_ = vm.Set("THttpPost", THttpPost)
	_ = vm.Set("THttpGet", THttpGet)
	_ = vm.Set("_SytH_", replace)
	if vm == nil {
		return public.PointerPtr("Js vm初始化失败")
	}
	JsVm = vm
	return 0
}
func JsCall(_type int32, Request string) string {
	JSLock.Lock()
	defer JSLock.Unlock()
	_, _ = JsVm.Run("SyJsonOBJ=" + Request)
	value, err := JsVm.Call("Call", nil, _type, Request)
	if err != nil {
		return ""
	}
	return value.String()
}
