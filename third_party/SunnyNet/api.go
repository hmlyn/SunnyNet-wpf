/*
本类为所有动态库导出函数集合
*/
package main

import "C"
import (
	"github.com/qtgolang/SunnyNet/Api"
	"github.com/qtgolang/SunnyNet/public"
)

// 获取SunnyNet版本
//
//export GetSunnyVersion
func GetSunnyVersion() uintptr {
	return Api.GetSunnyVersion()
}

// 释放指针
//
//export Free
func Free(ptr uintptr) {
	public.Free(ptr)
}

// 创建Sunny中间件对象,可创建多个
//
//export CreateSunnyNet
func CreateSunnyNet() int {
	return Api.CreateSunnyNet()
}

// ReleaseSunnyNet 释放SunnyNet
//
//export ReleaseSunnyNet
func ReleaseSunnyNet(SunnyContext int) bool {
	return Api.ReleaseSunnyNet(SunnyContext)
}

// 启动Sunny中间件 成功返回true
//
//export SunnyNetStart
func SunnyNetStart(SunnyContext int) bool {
	return Api.SunnyNetStart(SunnyContext)
}

// 设置指定端口 Sunny中间件启动之前调用
//
//export SunnyNetSetPort
func SunnyNetSetPort(SunnyContext, Port int) bool {
	return Api.SunnyNetSetPort(SunnyContext, Port)
}

// 关闭停止指定Sunny中间件
//
//export SunnyNetClose
func SunnyNetClose(SunnyContext int) bool {
	return Api.SunnyNetClose(SunnyContext)
}

// 设置自定义证书
//
//export SunnyNetSetCert
func SunnyNetSetCert(SunnyContext, CertificateManagerId int) bool {
	return Api.SunnyNetSetCert(SunnyContext, CertificateManagerId)
}

// 安装证书 将证书安装到Windows系统内
//
//export SunnyNetInstallCert
func SunnyNetInstallCert(SunnyContext int) uintptr {
	return Api.SunnyNetInstallCert(SunnyContext)
}

// 设置中间件回调地址 httpCallback
//
//export SunnyNetSetCallback
func SunnyNetSetCallback(SunnyContext, httpCallback, tcpCallback, wsCallback, udpCallback int) bool {
	return Api.SunnyNetSetCallback(SunnyContext, httpCallback, tcpCallback, wsCallback, udpCallback)
}

// 添加 S5代理需要验证的用户名
//
//export SunnyNetSocket5AddUser
func SunnyNetSocket5AddUser(SunnyContext int, User, Pass *C.char) bool {
	return Api.SunnyNetSocket5AddUser(SunnyContext, C.GoString(User), C.GoString(Pass))
}

// 开启身份验证模式
//
//export SunnyNetVerifyUser
func SunnyNetVerifyUser(SunnyContext int, open bool) bool {
	return Api.SunnyNetVerifyUser(SunnyContext, open)
}

// 删除 S5需要验证的用户名
//
//export SunnyNetSocket5DelUser
func SunnyNetSocket5DelUser(SunnyContext int, User *C.char) bool {
	return Api.SunnyNetSocket5DelUser(SunnyContext, C.GoString(User))
}

// 开启身份验证模式后 获取授权的S5账号,注意UDP请求无法获取到授权的s5账号
//
//export SunnyNetGetSocket5User
func SunnyNetGetSocket5User(Theology int) uintptr {
	return Api.SunnyNetGetSocket5User(Theology)
}

// 设置中间件是否开启强制走TCP
//
//export SunnyNetMustTcp
func SunnyNetMustTcp(SunnyContext int, open bool) {
	Api.SunnyNetMustTcp(SunnyContext, open)
}

// 设置中间件上游代理使用规则
//
//export CompileProxyRegexp
func CompileProxyRegexp(SunnyContext int, Regexp *C.char) bool {
	return Api.CompileProxyRegexp(SunnyContext, C.GoString(Regexp))
}

// 设置强制走TCP规则,如果 打开了全部强制走TCP状态,本功能则无效
//
//export SetMustTcpRegexp
func SetMustTcpRegexp(SunnyContext int, Regexp *C.char) bool {
	return Api.SetMustTcpRegexp(SunnyContext, C.GoString(Regexp))
}

// 获取中间件启动时的错误信息
//
//export SunnyNetError
func SunnyNetError(SunnyContext int) uintptr {
	return Api.SunnyNetError(SunnyContext)
}

// 设置全局上游代理 仅支持Socket5和http 例如 socket5://admin:123456@127.0.0.1:8888 或 http://admin:123456@127.0.0.1:8888
//
//export SetGlobalProxy
func SetGlobalProxy(SunnyContext int, ProxyAddress *C.char) bool {
	return Api.SetGlobalProxy(SunnyContext, C.GoString(ProxyAddress))
}

// 导出已设置的证书
//
//export ExportCert
func ExportCert(SunnyContext int) uintptr {
	return Api.ExportCert(SunnyContext)
}

// 设置IE代理 Off=true 取消 反之 设置 在中间件设置端口后调用
//
//export SetIeProxy
func SetIeProxy(SunnyContext int, Off bool) bool {
	return Api.SetIeProxy(SunnyContext, Off)
}

// 修改、设置 HTTP/S当前请求数据中指定Cookie
//
//export SetRequestCookie
func SetRequestCookie(MessageId int, name, val *C.char) {
	Api.SetRequestCookie(MessageId, C.GoString(name), C.GoString(val))
}

// 修改、设置 HTTP/S当前请求数据中的全部Cookie
//
//export SetRequestAllCookie
func SetRequestAllCookie(MessageId int, val *C.char) {
	Api.SetRequestAllCookie(MessageId, C.GoString(val))
}

// 获取 HTTP/S当前请求数据中指定的Cookie
//
//export GetRequestCookie
func GetRequestCookie(MessageId int, name *C.char) uintptr {
	return Api.GetRequestCookie(MessageId, C.GoString(name))
}

// 获取 HTTP/S 当前请求全部Cookie
//
//export GetRequestALLCookie
func GetRequestALLCookie(MessageId int) uintptr {
	return Api.GetRequestALLCookie(MessageId)
}

// 删除HTTP/S返回数据中指定的协议头
//
//export DelResponseHeader
func DelResponseHeader(MessageId int, name *C.char) {
	Api.DelResponseHeader(MessageId, C.GoString(name))
}

// 删除HTTP/S请求数据中指定的协议头
//
//export DelRequestHeader
func DelRequestHeader(MessageId int, name *C.char) {
	Api.DelRequestHeader(MessageId, C.GoString(name))
}

// 请求设置超时-毫秒
//
//export SetRequestOutTime
func SetRequestOutTime(MessageId int, times int) {
	Api.SetRequestOutTime(MessageId, times)
}

// 设置HTTP/S请求体中的协议头
//
//export SetRequestHeader
func SetRequestHeader(MessageId int, name, val *C.char) {
	Api.SetRequestHeader(MessageId, C.GoString(name), C.GoString(val))
}

// 修改、设置 HTTP/S当前返回数据中的指定协议头
//
//export SetResponseHeader
func SetResponseHeader(MessageId int, name *C.char, val *C.char) {
	Api.SetResponseHeader(MessageId, C.GoString(name), C.GoString(val))
}

// 获取 HTTP/S当前请求数据中的指定协议头
//
//export GetRequestHeader
func GetRequestHeader(MessageId int, name *C.char) uintptr {
	return Api.GetRequestHeader(MessageId, C.GoString(name))
}

// 获取 HTTP/S 当前返回数据中指定的协议头
//
//export GetResponseHeader
func GetResponseHeader(MessageId int, name *C.char) uintptr {
	return Api.GetResponseHeader(MessageId, C.GoString(name))
}

// 修改、设置 HTTP/S当前返回数据中的全部协议头，例如设置返回两条Cookie 使用本命令设置 使用设置、修改 单条命令无效
//
//export SetResponseAllHeader
func SetResponseAllHeader(MessageId int, value *C.char) {
	Api.SetResponseAllHeader(MessageId, C.GoString(value))
}

// 获取 HTTP/S 当前返回全部协议头
//
//export GetResponseAllHeader
func GetResponseAllHeader(MessageId int) uintptr {
	return Api.GetResponseAllHeader(MessageId)
}

// 获取 HTTP/S 当前请求数据全部协议头
//
//export GetRequestAllHeader
func GetRequestAllHeader(MessageId int) uintptr {
	return Api.GetRequestAllHeader(MessageId)
}

// 设置HTTP/S请求代理，仅支持Socket5和http 例如 socket5://admin:123456@127.0.0.1:8888 或 http://admin:123456@127.0.0.1:8888
//
//export SetRequestProxy
func SetRequestProxy(MessageId int, ProxyUrl *C.char) bool {
	return Api.SetRequestProxy(MessageId, C.GoString(ProxyUrl))
}

// 获取HTTP/S返回的状态码
//
//export GetResponseStatusCode
func GetResponseStatusCode(MessageId int) int {
	return Api.GetResponseStatusCode(MessageId)
}

// 获取当前HTTP/S请求由哪个IP发起
//
//export GetRequestClientIp
func GetRequestClientIp(MessageId int) uintptr {
	return Api.GetRequestClientIp(MessageId)
}

// 获取HTTP/S返回的状态文本 例如 [200 OK]
//
//export GetResponseStatus
func GetResponseStatus(MessageId int) uintptr {
	return Api.GetResponseStatus(MessageId)
}

// 修改HTTP/S返回的状态码
//
//export SetResponseStatus
func SetResponseStatus(MessageId, code int) {
	Api.SetResponseStatus(MessageId, code)
}

// 修改HTTP/S当前请求的URL
//
//export SetRequestUrl
func SetRequestUrl(MessageId int, URI *C.char) bool {
	return Api.SetRequestUrl(MessageId, C.GoString(URI))
}

// 获取 HTTP/S 当前请求POST提交数据长度
//
//export GetRequestBodyLen
func GetRequestBodyLen(MessageId int) int {
	return Api.GetRequestBodyLen(MessageId)
}

// 获取 HTTP/S 当前返回  数据长度
//
//export GetResponseBodyLen
func GetResponseBodyLen(MessageId int) int {
	return Api.GetResponseBodyLen(MessageId)
}

// 设置、修改 HTTP/S 当前请求返回数据 如果再发起请求时调用本命令，请求将不会被发送，将会直接返回 data=数据指针  dataLen=数据长度
//
//export SetResponseData
func SetResponseData(MessageId int, data uintptr, dataLen int) bool {
	return Api.SetResponseData(MessageId, data, dataLen)
}

// 设置、修改 HTTP/S 当前请求POST提交数据  data=数据指针  dataLen=数据长度
//
//export SetRequestData
func SetRequestData(MessageId int, data uintptr, dataLen int) int {
	return Api.SetRequestData(MessageId, data, dataLen)
}

// 获取 HTTP/S 当前POST提交数据 返回 数据指针
//
//export GetRequestBody
func GetRequestBody(MessageId int) uintptr {
	return Api.GetRequestBody(MessageId)
}

// 获取 HTTP/S 当前返回数据  返回 数据指针
//
//export GetResponseBody
func GetResponseBody(MessageId int) uintptr {
	return Api.GetResponseBody(MessageId)
}

// 获取 WebSocket消息长度
//
//export GetWebsocketBodyLen
func GetWebsocketBodyLen(MessageId int) int {
	return Api.GetWebsocketBodyLen(MessageId)
}

// 主动关闭Websocket
//
//export CloseWebsocket
func CloseWebsocket(Theology int) bool {
	return Api.CloseWebsocket(Theology)
}

// 获取 WebSocket消息 返回数据指针
//
//export GetWebsocketBody
func GetWebsocketBody(MessageId int) uintptr {
	return Api.GetWebsocketBody(MessageId)
}

// 修改 WebSocket消息 data=数据指针  dataLen=数据长度
//
//export SetWebsocketBody
func SetWebsocketBody(MessageId int, data uintptr, dataLen int) bool {
	return Api.SetWebsocketBody(MessageId, data, dataLen)
}

// 主动向Websocket服务器发送消息 MessageType=WS消息类型 data=数据指针  dataLen=数据长度
//
//export SendWebsocketBody
func SendWebsocketBody(Theology, MessageType int, data uintptr, dataLen int) bool {
	return Api.SendWebsocketBody(Theology, MessageType, data, dataLen)
}

// SendWebsocketClientBody 主动向Websocket客户端发送消息 MessageType=WS消息类型 data=数据指针  dataLen=数据长度
//
//export SendWebsocketClientBody
func SendWebsocketClientBody(Theology, MessageType int, data uintptr, dataLen int) bool {
	return Api.SendWebsocketClientBody(Theology, MessageType, data, dataLen)
}

// 修改 TCP消息数据 MsgType=1 发送的消息 MsgType=2 接收的消息 如果 MsgType和MessageId不匹配，将不会执行操作  data=数据指针  dataLen=数据长度
//
//export SetTcpBody
func SetTcpBody(MessageId, MsgType int, data uintptr, dataLen int) bool {
	return Api.SetTcpBody(MessageId, MsgType, data, dataLen)
}

// 给当前TCP连接设置代理 仅限 TCP回调 即将连接时使用 仅支持S5代理 例如 socket5://admin:123456@127.0.0.1:8888
//
//export SetTcpAgent
func SetTcpAgent(MessageId int, ProxyUrl *C.char) bool {
	return Api.SetTcpAgent(MessageId, C.GoString(ProxyUrl))
}

// 根据唯一ID关闭指定的TCP连接  唯一ID在回调参数中
//
//export TcpCloseClient
func TcpCloseClient(theology int) bool {
	return Api.TcpCloseClient(theology)
}

// 给指定的TCP连接 修改目标连接地址 目标地址必须带端口号 例如 baidu.com:443
//
//export SetTcpConnectionIP
func SetTcpConnectionIP(MessageId int, address *C.char) bool {
	return Api.SetTcpConnectionIP(MessageId, C.GoString(address))
}

// 指定的TCP连接 模拟客户端向服务器端主动发送数据
//
//export TcpSendMsg
func TcpSendMsg(theology int, data uintptr, dataLen int) int {
	return Api.TcpSendMsg(theology, data, dataLen)
}

// 指定的TCP连接 模拟服务器端向客户端主动发送数据
//
//export TcpSendMsgClient
func TcpSendMsgClient(theology int, data uintptr, dataLen int) int {
	return Api.TcpSendMsgClient(theology, data, dataLen)
}

//export HexDump
/*字节数组转字符串 返回格式如下
00000000  53 75 6E 6E 79 4E 65 74  54 65 73 74 45 78 61 6D  |SunnyNetTestExam|
00000010  70 6C 65                                          |ple|
*/
func HexDump(data uintptr, dataLen int) uintptr {
	return Api.HexDump(data, dataLen)
}

// 将Go int的Bytes 转为int
//
//export BytesToInt
func BytesToInt(data uintptr, dataLen int) int {
	return Api.BytesToInt(data, dataLen)
}

// Gzip解压缩
//
//export GzipUnCompress
func GzipUnCompress(data uintptr, dataLen int) uintptr {
	return Api.GzipUnCompress(data, dataLen)
}

// br解压缩
//
//export BrUnCompress
func BrUnCompress(data uintptr, dataLen int) uintptr {
	return Api.BrUnCompress(data, dataLen)
}

// br压缩
//
//export BrCompress
func BrCompress(data uintptr, dataLen int) uintptr {
	return Api.BrCompress(data, dataLen)
}

// ZSTD解压缩
//
//export ZSTDDecompress
func ZSTDDecompress(data uintptr, dataLen int) uintptr {
	return Api.ZSTDDecompress(data, dataLen)
}

// ZSTD压缩
//
//export ZSTDCompress
func ZSTDCompress(data uintptr, dataLen int) uintptr {
	return Api.ZSTDCompress(data, dataLen)
}

// br压缩
//
//export BrotliCompress
func BrotliCompress(data uintptr, dataLen int) uintptr {
	return Api.BrCompress(data, dataLen)
}

// Gzip压缩
//
//export GzipCompress
func GzipCompress(data uintptr, dataLen int) uintptr {
	return Api.GzipCompress(data, dataLen)
}

// Zlib压缩
//
//export ZlibCompress
func ZlibCompress(data uintptr, dataLen int) uintptr {
	return Api.ZlibCompress(data, dataLen)
}

// Zlib解压缩
//
//export ZlibUnCompress
func ZlibUnCompress(data uintptr, dataLen int) uintptr {
	return Api.ZlibUnCompress(data, dataLen)
}

// Deflate解压缩 (可能等同于zlib解压缩)
//
//export DeflateUnCompress
func DeflateUnCompress(data uintptr, dataLen int) uintptr {
	return Api.DeflateUnCompress(data, dataLen)
}

// Deflate压缩 (可能等同于zlib压缩)
//
//export DeflateCompress
func DeflateCompress(data uintptr, dataLen int) uintptr {
	return Api.DeflateCompress(data, dataLen)
}

// Webp图片转JEG图片字节数组 SaveQuality=质量(默认75)
//
//export WebpToJpegBytes
func WebpToJpegBytes(data uintptr, dataLen int, SaveQuality int) uintptr {
	return Api.WebpToJpegBytes(data, dataLen, SaveQuality)
}

// Webp图片转Png图片字节数组
//
//export WebpToPngBytes
func WebpToPngBytes(data uintptr, dataLen int) uintptr {
	return Api.WebpToPngBytes(data, dataLen)
}

// Webp图片转JEG图片 根据文件名 SaveQuality=质量(默认75)
//
//export WebpToJpeg
func WebpToJpeg(webpPath, savePath *C.char, SaveQuality int) bool {
	return Api.WebpToJpeg(C.GoString(webpPath), C.GoString(savePath), SaveQuality)
}

// Webp图片转Png图片 根据文件名
//
//export WebpToPng
func WebpToPng(webpPath, savePath *C.char) bool {
	return Api.WebpToPng(C.GoString(webpPath), C.GoString(savePath))
}

// 适配火山PC CALL 火山直接CALL X64没有问题，X86环境下有问题，所以搞了这个命令
//
//export GoCall
func GoCall(address, a1, a2, a3, a4, a5, a6, a7, a8, a9 int) int {
	return Api.GoCall(address, a1, a2, a3, a4, a5, a6, a7, a8, a9)
}

// 执行JS代码执行前 先调用 SetScript  设置JS代码
//
//export ScriptCall
func ScriptCall(i int, Request *C.char) uintptr {
	return Api.ScriptCall(i, C.GoString(Request))
}

// 设置JS代码
//
//export SetScript
func SetScript(Request *C.char) uintptr {
	return Api.SetScript(C.GoString(Request))
}

// 设置JSLog函数回调地址
//
//export SetScriptLogCallAddress
func SetScriptLogCallAddress(i int) {
	Api.SetScriptLogCallAddress(i)
}

// 开启进程代理 加载 nf api 驱动
//
//export StartProcess
func StartProcess(SunnyContext int) bool {
	return Api.StartProcess(SunnyContext)
}

// 进程代理 添加进程名
//
//export ProcessAddName
func ProcessAddName(SunnyContext int, Name *C.char) {
	Api.ProcessAddName(SunnyContext, C.GoString(Name))
}

// 进程代理 删除进程名
//
//export ProcessDelName
func ProcessDelName(SunnyContext int, Name *C.char) {
	Api.ProcessDelName(SunnyContext, C.GoString(Name))
}

// 进程代理 添加PID
//
//export ProcessAddPid
func ProcessAddPid(SunnyContext, pid int) {
	Api.ProcessAddPid(SunnyContext, pid)
}

// 进程代理 删除PID
//
//export ProcessDelPid
func ProcessDelPid(SunnyContext, pid int) {
	Api.ProcessDelPid(SunnyContext, pid)
}

// 进程代理 取消全部已设置的进程名
//
//export ProcessCancelAll
func ProcessCancelAll(SunnyContext int) {
	Api.ProcessCancelAll(SunnyContext)
}

// 进程代理 设置是否全部进程通过
//
//export ProcessALLName
func ProcessALLName(SunnyContext int, open bool) {
	Api.ProcessALLName(SunnyContext, open)
}

//================================================================================================

// 证书管理器 获取证书 CommonName 字段
//
//export GetCommonName
func GetCommonName(Context int) uintptr {
	return public.PointerPtr(Api.GetCommonName(Context))
}

// 证书管理器 导出为P12
//
//export ExportP12
func ExportP12(Context int, path, pass *C.char) bool {
	return Api.ExportP12(Context, C.GoString(path), C.GoString(pass))
}

// 证书管理器 导出公钥
//
//export ExportPub
func ExportPub(Context int) uintptr {
	return Api.ExportPub(Context)
}

// 证书管理器 导出私钥
//
//export ExportKEY
func ExportKEY(Context int) uintptr {
	return public.PointerPtr(Api.ExportKEY(Context))
}

// 证书管理器 导出证书
//
//export ExportCA
func ExportCA(Context int) uintptr {
	return public.PointerPtr(Api.ExportCA(Context))
}

// 证书管理器 创建证书
//
//export CreateCA
func CreateCA(Context int, Country, Organization, OrganizationalUnit, Province, CommonName, Locality *C.char, bits, NotAfter int) bool {
	return Api.CreateCA(Context, C.GoString(Country), C.GoString(Organization), C.GoString(OrganizationalUnit), C.GoString(Province), C.GoString(CommonName), C.GoString(Locality), bits, NotAfter)
}

// 证书管理器 设置ClientAuth
//
//export AddClientAuth
func AddClientAuth(Context, val int) bool {
	return Api.AddClientAuth(Context, val)
}

// 证书管理器 设置信任的证书 从 文本
//
//export AddCertPoolText
func AddCertPoolText(Context int, cer *C.char) bool {
	return Api.AddCertPoolText(Context, C.GoString(cer))
}

// 证书管理器 设置信任的证书 从 文件
//
//export AddCertPoolPath
func AddCertPoolPath(Context int, cer *C.char) bool {
	return Api.AddCertPoolPath(Context, C.GoString(cer))
}

// 证书管理器 取ServerName
//
//export GetServerName
func GetServerName(Context int) uintptr {
	return public.PointerPtr(Api.GetServerName(Context))
}

// 证书管理器 设置ServerName
//
//export SetServerName
func SetServerName(Context int, name *C.char) bool {
	return Api.SetServerName(Context, C.GoString(name))
}

// 证书管理器 设置跳过主机验证
//
//export SetInsecureSkipVerify
func SetInsecureSkipVerify(Context int, b bool) bool {
	return Api.SetInsecureSkipVerify(Context, b)
}

// 证书管理器 载入X509证书
//
//export LoadX509Certificate
func LoadX509Certificate(Context int, Host, CA, KEY *C.char) bool {
	return Api.LoadX509Certificate(Context, C.GoString(Host), C.GoString(CA), C.GoString(KEY))
}

// 证书管理器 载入X509证书2
//
//export LoadX509KeyPair
func LoadX509KeyPair(Context int, CaPath, KeyPath *C.char) bool {
	return Api.LoadX509KeyPair(Context, C.GoString(CaPath), C.GoString(KeyPath))
}

// 证书管理器 载入p12证书
//
//export LoadP12Certificate
func LoadP12Certificate(Context int, Name, Password *C.char) bool {
	return Api.LoadP12Certificate(Context, C.GoString(Name), C.GoString(Password))
}

// 释放 证书管理器 对象
//
//export RemoveCertificate
func RemoveCertificate(Context int) {
	Api.RemoveCertificate(Context)
}

// 创建 证书管理器 对象
//
//export CreateCertificate
func CreateCertificate() int {
	return Api.CreateCertificate()
}

//================================================ go map 相关 ==========================================================

// GoMap 写字符串
//
//export KeysWriteStr
func KeysWriteStr(KeysHandle int, name *C.char, val uintptr, len int) {
	Api.KeysWriteStr(KeysHandle, C.GoString(name), val, len)
}

// GoMap 转为JSON字符串
//
//export KeysGetJson
func KeysGetJson(KeysHandle int) uintptr {
	return Api.KeysGetJson(KeysHandle)
}

// GoMap 取数量
//
//export KeysGetCount
func KeysGetCount(KeysHandle int) int {
	return Api.KeysGetCount(KeysHandle)
}

// GoMap 清空
//
//export KeysEmpty
func KeysEmpty(KeysHandle int) {
	Api.KeysEmpty(KeysHandle)
}

// GoMap 读整数
//
//export KeysReadInt
func KeysReadInt(KeysHandle int, name *C.char) int {
	return Api.KeysReadInt(KeysHandle, C.GoString(name))
}

// GoMap 写整数
//
//export KeysWriteInt
func KeysWriteInt(KeysHandle int, name *C.char, val int) {
	Api.KeysWriteInt(KeysHandle, C.GoString(name), val)
}

// GoMap 读长整数
//
//export KeysReadLong
func KeysReadLong(KeysHandle int, name *C.char) int64 {
	return Api.KeysReadLong(KeysHandle, C.GoString(name))
}

// GoMap 写长整数
//
//export KeysWriteLong
func KeysWriteLong(KeysHandle int, name *C.char, val int64) {
	Api.KeysWriteLong(KeysHandle, C.GoString(name), val)
}

// GoMap 读浮点数
//
//export KeysReadFloat
func KeysReadFloat(KeysHandle int, name *C.char) float64 {
	return Api.KeysReadFloat(KeysHandle, C.GoString(name))
}

// GoMap 写浮点数
//
//export KeysWriteFloat
func KeysWriteFloat(KeysHandle int, name *C.char, val float64) {
	Api.KeysWriteFloat(KeysHandle, C.GoString(name), val)
}

// GoMap 写字节数组
//
//export KeysWrite
func KeysWrite(KeysHandle int, name *C.char, val uintptr, length int) {
	Api.KeysWrite(KeysHandle, C.GoString(name), val, length)
}

// GoMap 写读字符串/字节数组
//
//export KeysRead
func KeysRead(KeysHandle int, name *C.char) uintptr {
	return Api.KeysRead(KeysHandle, C.GoString(name))
}

// GoMap 删除
//
//export KeysDelete
func KeysDelete(KeysHandle int, name *C.char) {
	Api.KeysDelete(KeysHandle, C.GoString(name))
}

// GoMap 删除GoMap
//
//export RemoveKeys
func RemoveKeys(KeysHandle int) {
	Api.RemoveKeys(KeysHandle)
}

// GoMap 创建
//
//export CreateKeys
func CreateKeys() int {
	return Api.CreateKeys()
}

//===================================================== go win http ====================================================

// HTTP 客户端 设置重定向
//
//export HTTPSetRedirect
func HTTPSetRedirect(Context int, Redirect bool) bool {
	return Api.HTTPSetRedirect(Context, Redirect)
}

// HTTP 客户端 返回响应状态码
//
//export HTTPGetCode
func HTTPGetCode(Context int) int {
	return Api.HTTPGetCode(Context)
}

// HTTP 客户端 设置证书管理器
//
//export HTTPSetCertManager
func HTTPSetCertManager(Context, CertManagerContext int) bool {
	return Api.HTTPSetCertManager(Context, CertManagerContext)
}

// HTTP 客户端 返回响应内容
//
//export HTTPGetBody
func HTTPGetBody(Context int) uintptr {
	return Api.HTTPGetBody(Context)
}

// HTTP 客户端 返回响应全部Heads
//
//export HTTPGetHeads
func HTTPGetHeads(Context int) uintptr {
	return Api.HTTPGetHeads(Context)
}

// HTTP 客户端 返回响应长度
//
//export HTTPGetBodyLen
func HTTPGetBodyLen(Context int) int {
	return Api.HTTPGetBodyLen(Context)
}

// HTTP 客户端 发送Body
//
//export HTTPSendBin
func HTTPSendBin(Context int, body uintptr, bodyLength int) {
	Api.HTTPSendBin(Context, body, bodyLength)
}

// HTTP 客户端 设置超时 毫秒
//
//export HTTPSetTimeouts
func HTTPSetTimeouts(Context int, t1, t2, t3 int) {
	Api.HTTPSetTimeouts(Context, t1, t2, t3)
}

// HTTP 客户端 设置代理IP 仅支持Socket5和http 例如 socket5://admin:123456@127.0.0.1:8888 或 http://admin:123456@127.0.0.1:8888
//
//export HTTPSetProxyIP
func HTTPSetProxyIP(Context int, ProxyUrl *C.char) {
	Api.HTTPSetProxyIP(Context, C.GoString(ProxyUrl))
}

// HTTP 客户端 设置协议头
//
//export HTTPSetHeader
func HTTPSetHeader(Context int, name, value *C.char) {
	Api.HTTPSetHeader(Context, C.GoString(name), C.GoString(value))
}

// HTTP 客户端 Open
//
//export HTTPOpen
func HTTPOpen(Context int, Method, URL *C.char) {
	Api.HTTPOpen(Context, C.GoString(Method), C.GoString(URL))
}

// HTTP 客户端 取错误
//
//export HTTPClientGetErr
func HTTPClientGetErr(Context int) uintptr {
	return Api.HTTPClientGetErr(Context)
}

// 释放 HTTP客户端
//
//export RemoveHTTPClient
func RemoveHTTPClient(Context int) {
	Api.RemoveHTTPClient(Context)
}

// 创建 HTTP 客户端
//
//export CreateHTTPClient
func CreateHTTPClient() int {
	return Api.CreateHTTPClient()
}

//===========================================================================================

// JSON格式的protobuf数据转为protobuf二进制数据
//
//export JsonToPB
func JsonToPB(bin uintptr, binLen int) uintptr {
	return Api.JsonToPB(bin, binLen)
}

// protobuf数据转为JSON格式
//
//export PbToJson
func PbToJson(bin uintptr, binLen int) uintptr {
	return Api.PbToJson(bin, binLen)
}

//===========================================================================================

// 队列弹出
//
//export QueuePull
func QueuePull(name *C.char) uintptr {
	return Api.QueuePull(C.GoString(name))
}

// 加入队列
//
//export QueuePush
func QueuePush(name *C.char, val uintptr, valLen int) {
	Api.QueuePush(C.GoString(name), val, valLen)
}

// 取队列长度
//
//export QueueLength
func QueueLength(name *C.char) int {
	return Api.QueueLength(C.GoString(name))
}

// 清空销毁队列
//
//export QueueRelease
func QueueRelease(name *C.char) {
	Api.QueueRelease(C.GoString(name))
}

// 队列是否为空
//
//export QueueIsEmpty
func QueueIsEmpty(name *C.char) bool {
	return Api.QueueIsEmpty(C.GoString(name))
}

// 创建队列
//
//export CreateQueue
func CreateQueue(name *C.char) {
	Api.CreateQueue(C.GoString(name))
}

//=========================================================================================================

// TCP客户端 发送数据
//
//export SocketClientWrite
func SocketClientWrite(Context, OutTimes int, val uintptr, valLen int) int {
	return Api.SocketClientWrite(Context, OutTimes, val, valLen)
}

// TCP客户端 断开连接
//
//export SocketClientClose
func SocketClientClose(Context int) {
	Api.SocketClientClose(Context)
}

// TCP客户端 同步模式下 接收数据
//
//export SocketClientReceive
func SocketClientReceive(Context, OutTimes int) uintptr {
	return Api.SocketClientReceive(Context, OutTimes)
}

// TCP客户端 连接
//
//export SocketClientDial
func SocketClientDial(Context int, addr *C.char, call int, isTls, synchronous bool, ProxyUrl *C.char, CertificateConText int, ProxyOutTime int) bool {
	return Api.SocketClientDial(Context, C.GoString(addr), call, isTls, synchronous, C.GoString(ProxyUrl), CertificateConText, ProxyOutTime)
}

// TCP客户端 置缓冲区大小
//
//export SocketClientSetBufferSize
func SocketClientSetBufferSize(Context, BufferSize int) bool {
	return Api.SocketClientSetBufferSize(Context, BufferSize)
}

// TCP客户端 取错误
//
//export SocketClientGetErr
func SocketClientGetErr(Context int) uintptr {
	return Api.SocketClientGetErr(Context)
}

// 释放 TCP客户端
//
//export RemoveSocketClient
func RemoveSocketClient(Context int) {
	Api.RemoveSocketClient(Context)
}

// 创建 TCP客户端
//
//export CreateSocketClient
func CreateSocketClient() int {
	return Api.CreateSocketClient()
}

//==================================================================================================

// Websocket客户端 同步模式下 接收数据 返回数据指针 失败返回0 length=返回数据长度
//
//export WebsocketClientReceive
func WebsocketClientReceive(Context, OutTimes int) uintptr {
	return Api.WebsocketClientReceive(Context, OutTimes)
}

// Websocket客户端  发送数据
//
//export WebsocketReadWrite
func WebsocketReadWrite(Context int, val uintptr, valLen int, messageType int) bool {
	return Api.WebsocketReadWrite(Context, val, valLen, messageType)
}

// Websocket客户端 断开
//
//export WebsocketClose
func WebsocketClose(Context int) {
	Api.WebsocketClose(Context)
}

// Websocket客户端 连接
//
//export WebsocketDial
func WebsocketDial(Context int, URL, Heads *C.char, call int, synchronous bool, ProxyUrl *C.char, CertificateConText, outTime int) bool {
	return Api.WebsocketDial(Context, C.GoString(URL), C.GoString(Heads), call, synchronous, C.GoString(ProxyUrl), CertificateConText, outTime)
}

// Websocket客户端 获取错误
//
//export WebsocketGetErr
func WebsocketGetErr(Context int) uintptr {
	return Api.WebsocketGetErr(Context)
}

// 释放 Websocket客户端 对象
//
//export RemoveWebsocket
func RemoveWebsocket(Context int) {
	Api.RemoveWebsocket(Context)
}

// 创建 Websocket客户端 对象
//
//export CreateWebsocket
func CreateWebsocket() int {
	return Api.CreateWebsocket()
}

//==================================================================================================

// 创建 Http证书管理器 对象 实现指定Host使用指定证书
//
//export AddHttpCertificate
func AddHttpCertificate(host *C.char, CertManagerId, Rules int) bool {
	return Api.AddHttpCertificate(C.GoString(host), CertManagerId, uint8(Rules))
}

// 删除 Http证书管理器 对象
//
//export DelHttpCertificate
func DelHttpCertificate(host *C.char) {
	Api.DelHttpCertificate(C.GoString(host))
}

//==================================================================================================

// Redis 订阅消息
//
//export RedisSubscribe
func RedisSubscribe(Context int, scribe *C.char, call int, nc bool) {
	Api.RedisSubscribe(Context, C.GoString(scribe), call, nc)
}

// Redis 删除
//
//export RedisDelete
func RedisDelete(Context int, key *C.char) bool {
	return Api.RedisDelete(Context, C.GoString(key))
}

// Redis 清空当前数据库
//
//export RedisFlushDB
func RedisFlushDB(Context int) {
	Api.RedisFlushDB(Context)
}

// Redis 清空redis服务器
//
//export RedisFlushAll
func RedisFlushAll(Context int) {
	Api.RedisFlushAll(Context)
}

// Redis 关闭
//
//export RedisClose
func RedisClose(Context int) {
	Api.RedisClose(Context)
}

// Redis 取整数值
//
//export RedisGetInt
func RedisGetInt(Context int, key *C.char) int64 {
	return Api.RedisGetInt(Context, C.GoString(key))
}

// Redis 取指定条件键名
//
//export RedisGetKeys
func RedisGetKeys(Context int, key *C.char) uintptr {
	return Api.RedisGetKeys(Context, C.GoString(key))
}

// Redis 自定义 执行和查询命令 返回操作结果可能是值 也可能是JSON文本
//
//export RedisDo
func RedisDo(Context int, args *C.char, error uintptr) uintptr {
	return Api.RedisDo(Context, C.GoString(args), error)
}

// Redis 取文本值
//
//export RedisGetStr
func RedisGetStr(Context int, key *C.char) uintptr {
	return Api.RedisGetStr(Context, C.GoString(key))
}

// Redis 取Bytes值
//
//export RedisGetBytes
func RedisGetBytes(Context int, key *C.char) uintptr {
	return Api.RedisGetBytes(Context, C.GoString(key))
}

// Redis 检查指定 key 是否存在
//
//export RedisExists
func RedisExists(Context int, key *C.char) bool {
	return Api.RedisExists(Context, C.GoString(key))
}

// Redis 设置NX 【如果键名存在返回假】
//
//export RedisSetNx
func RedisSetNx(Context int, key, val *C.char, expr int) bool {
	return Api.RedisSetNx(Context, C.GoString(key), C.GoString(val), expr)
}

// Redis 设置值
//
//export RedisSet
func RedisSet(Context int, key, val *C.char, expr int) bool {
	return Api.RedisSet(Context, C.GoString(key), C.GoString(val), expr)
}

// Redis 设置Bytes值
//
//export RedisSetBytes
func RedisSetBytes(Context int, key *C.char, val uintptr, valLen int, expr int) bool {
	data := public.CStringToBytes(val, valLen)
	return Api.RedisSetBytes(Context, C.GoString(key), data, expr)
}

// Redis 连接
//
//export RedisDial
func RedisDial(Context int, host, pass *C.char, db, PoolSize, MinIdleCons, DialTimeout, ReadTimeout, WriteTimeout, PoolTimeout, IdleCheckFrequency, IdleTimeout int, error uintptr) bool {
	return Api.RedisDial(Context, C.GoString(host), C.GoString(pass), db, PoolSize, MinIdleCons, DialTimeout, ReadTimeout, WriteTimeout, PoolTimeout, IdleCheckFrequency, IdleTimeout, error)
}

// 释放 Redis 对象
//
//export RemoveRedis
func RemoveRedis(Context int) {
	Api.RemoveRedis(Context)
}

// 创建 Redis 对象
//
//export CreateRedis
func CreateRedis() int {
	return Api.CreateRedis()
}

// 设置修改UDP数据
//
//export SetUdpData
func SetUdpData(MessageId int, val uintptr, valLen int) bool {
	data := public.CStringToBytes(val, valLen)
	return Api.SetUdpData(MessageId, data)
}

// 获取UDP数据
//
//export GetUdpData
func GetUdpData(MessageId int) uintptr {
	return Api.GetUdpData(MessageId)
}

// 指定的UDP连接 模拟服务器端向客户端主动发送数据
//
//export UdpSendToClient
func UdpSendToClient(theology int, data uintptr, dataLen int) bool {
	bs := public.CStringToBytes(data, dataLen)
	return Api.UdpSendToClient(theology, bs)
}

// 指定的UDP连接 模拟客户端向服务器端主动发送数据
//
//export UdpSendToServer
func UdpSendToServer(theology int, data uintptr, dataLen int) bool {
	bs := public.CStringToBytes(data, dataLen)
	return Api.UdpSendToServer(theology, bs)
}

// OtherCommands 2024-05-31 预留命令,后续版本如果有可能新增函数，可以直接使用这个命令新增命令,而不用每次更新DLL导出表
//
//export OtherCommands
func OtherCommands(cmd uintptr, Command1 uintptr, Command2 uintptr, Command3 uintptr, Command4 uintptr, Command5 uintptr, Command6 uintptr, Command7 uintptr) uintptr {
	return Api.OtherCommands(cmd, Command1, Command2, Command3, Command4, Command5, Command6, Command7)
}
