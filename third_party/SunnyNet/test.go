package main

import "C"
import (
	"fmt"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"time"
)

func Test() {
	s := SunnyNet.NewSunny()
	//i := CreateCertificate()
	//ok := LoadP12Certificate(i, C.CString("C:\\Users\\qinka\\Desktop\\74fe394a37757545d8cfbd2ea264c7c3.p12"), C.CString("qyrhudhZ"))
	//ok := AddCertPoolPath(i, C.CString("C:\\Users\\qinka\\Desktop\\P12\\certificate.pem"))
	//fmt.Println("载入P12:", ok)
	//c := Certificate.LoadCertificateContext(i)
	//fmt.Println("证书名称：", c.GetCommonName())
	//AddHttpCertificate(C.CString("ws-gateway-odis.volkswagenag.com"), i, 1)

	//如果在Go中使用 设置Go的回调地址
	//s.SetGlobalProxy("http://127.0.0.1:2025")
	s.SetGoCallback(HttpCallback, TcpCallback, WSCallback, UdpCallback)
	//s.SetIeProxy(false)
	//s.MustTcp(true)
	Port := 2024
	//s.SetMustTcpRegexp("*.baidu.com")
	s = s.SetPort(Port).Start()
	//开启随机TLS指纹
	s.SetRandomTLS(true)

	//fmt.Println(s.StartProcess())
	// 请注意GoLang调试时候，请不要使用此(ProcessALLName)命令，因为不管开启或关闭，都会将当前所有TCP链接断开一次
	// 因为如果不断开的一次的话,已经建立的TCP链接无法抓包。
	// Go程序调试，是通过TCP连接的，若使用此命令将无法调试。
	// s.ProcessALLName(true)

	//s.ProcessAddName("WeChat.exe")
	//s.ProcessAddName("msedge.exe")
	//s.ProcessAddName("pop_dd_workbench.exe")
	err := s.Error
	if err != nil {
		panic(err)
	}
	fmt.Println("Run Port=", Port)
	time.Sleep(24 * time.Hour)
}
func HttpCallback(Conn *SunnyNet.HttpConn) {

	if Conn.Type == public.HttpSendRequest {
		//fmt.Println(Conn.Request.URL.String())
		//发起请求

		//直接响应,不让其发送请求
		//Conn.StopRequest(200, "Hello Word")

	} else if Conn.Type == public.HttpResponseOK {
		//请求完成
	} else if Conn.Type == public.HttpRequestFail {
		//请求错误
	}
}
func WSCallback(Conn *SunnyNet.WsConn) {

	//fmt.Println(Conn.Url)
}
func TcpCallback(Conn *SunnyNet.TcpConn) {
	//fmt.Println(Conn.Pid, Conn.LocalAddr, Conn.RemoteAddr, Conn.Type, Conn.GetBodyLen())
}
func UdpCallback(Conn *SunnyNet.UDPConn) {
	if public.SunnyNetUDPTypeReceive == Conn.Type {
		fmt.Println("接收UDP", Conn.LocalAddress, Conn.RemoteAddress, len(Conn.Data))
	}
	if public.SunnyNetUDPTypeSend == Conn.Type {
		fmt.Println("发送UDP", Conn.LocalAddress, Conn.RemoteAddress, len(Conn.Data))
	}
	if public.SunnyNetUDPTypeClosed == Conn.Type {
		fmt.Println("关闭UDP", Conn.LocalAddress, Conn.RemoteAddress)
	}

}
