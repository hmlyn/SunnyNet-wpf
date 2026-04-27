# Sunny网络中间件

---

> Sunny网络中间件 和 Fiddler 类似。 是可跨平台的网络分析组件
 ```log 
 可用于HTTP/HTTPS/WS/WSS/TCP/UDP网络分析 为二次开发量身制作
 
 支持 获取/修改 HTTP/HTTPS/WS/WSS/TCP/TLS-TCP/UDP 发送及返回数据
 
 支持 对 HTTP/HTTPS/WS/WSS 指定连接使用指定代理
 
 支持 对 HTTP/HTTPS/WS/WSS/TCP/TLS-TCP 链接重定向
 
 支持 gzip, deflate, br 解码
 
 支持 WS/WSS/TCP/TLS-TCP/UDP 主动发送数据
```

---
* # 由于代码主要是做DLL使用,部分功能未封装给Go使用(例如添加证书,添加证书使用规则),请自行探索！
* # <a href="https://github.com/jmeubank/tdm-gcc/releases/download/v10.3.0-tdm64-2/tdm64-gcc-10.3.0-2.exe">编译请使用 TDM-GCC</a> 
<center><h2><a style="color: red;">BUG 反馈</a></center></h2></center>
<center><h3>Sunny开源项目网站：https://esunny.vip</h3></center>
<center><h3>Sunny QQ交流群(一群)：751406884</h3></center>
<center><h3>Sunny QQ交流群(二群)：545120699</h3></center>
<center><h3>Sunny QQ交流群(三群)：170902713</h3></center>
<center><h3>QQ频道：https://pd.qq.com/s/cgzgyjj6u</h3></center>

---

### <center><h3>示例文件以及抓包工具 下载地址 </center>
<div style="text-align: center;"><h3>https://wwf.lanzouj.com/b02p3o56wj</h3></div>
<div style="text-align: center;"><h3>密码:4rus</h3></div>
<div style="text-align: center;"><h3></h3></div>


---
- > GoLang使用示例代码

```golang
package main

import (
	"fmt"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"time"
)

var Sunny = SunnyNet.NewSunny()

func main() {
	//绑定回调函数
	Sunny.SetGoCallback(HttpCallback, TcpCallback, WSCallback, UdpCallback)

	//绑定端口号并启动
	Sunny.SetPort(2023).Start()

	//避免程序退出
	time.Sleep(24 * time.Hour)
}
func HttpCallback(Conn *SunnyNet.HttpConn) {
	if Conn.Type == public.HttpSendRequest {
		//发起请求
		//这里可以对请求数据修改
		if Conn.Request.Body != nil {
			Body, _ := io.ReadAll(Conn.Request.Body)
			_ = Conn.Request.Body.Close()

			//这里可以对Body修改

			Body = []byte("Hello Sunny Request")

			Conn.Request.Body = io.NopCloser(bytes.NewBuffer(Body))

			//直接响应,不让其发送请求
			//Conn.StopRequest(200, "Hello Word")
		}
		fmt.Println(Conn.Request.URL.String())
	} else if Conn.Type == public.HttpResponseOK {
		//请求完成
		if Conn.Response.Body != nil {
			Body, _ := io.ReadAll(Conn.Response.Body)
			_ = Conn.Response.Body.Close()

			//这里可以对Body修改

			Body = []byte("Hello Sunny Response")

			Conn.Response.Body = io.NopCloser(bytes.NewBuffer(Body))
		}

	} else if Conn.Type == public.HttpRequestFail {
		//请求错误
	}
}
func WSCallback(Conn *SunnyNet.WsConn) {
	//捕获到数据可以修改,修改空数据,取消发送/接收
	fmt.Println(Conn.Url)
}
func TcpCallback(Conn *SunnyNet.TcpConn) {
	//捕获到数据可以修改,修改空数据,取消发送/接收
	
	fmt.Println(Conn.Pid, Conn.LocalAddr, Conn.RemoteAddr, Conn.Type, Conn.GetBodyLen())
}
func UdpCallback(Conn *SunnyNet.UDPConn) {
	//在 Windows 捕获UDP需要加载驱动,并且设置进程名
	//其他情况需要设置Socket5代理,才能捕获到UDP
	//捕获到数据可以修改,修改空数据,取消发送/接收
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
```