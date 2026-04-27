package JsCall

var ConsoleLogCall = 0

const JavaScript = `
var SyJsonOBJ={};
function Call(type, arg) {
    if ((typeof type) == "string") {
        type = parseInt(type);
    }

    switch (type) {
    case 1:
        try { 
            var s = HttpRequest(SyJsonOBJ); 
			var str='';
			for (var c in s.heads){
				str+='"'+c+'":"'+_SytH_(s.heads[c])+'",'
			}
			if (str.length>1){
				str=str.substring(0,str.length-1)
			}
            return '{"Display":'+s.Display+',"url":"'+_SytH_(s.url)+'","ResponseFile":"'+_SytH_(s.ResponseFile)+'","ResponseHeads":"'+_SytH_(s.ResponseHeads)+'","heads":{'+ str+'},"ProxyUrl":"'+_SytH_(s.ProxyUrl)+'"}'; 
        } catch (e) {
            Log("HTTP发送请求时 脚本中有错误 "+e);
			return "SunnyNetJS[error]:"+e;
        }
    case 2:
        try {
            var s = HttpResponse(SyJsonOBJ);
            s.heads = ClearBr(s.heads); 
            return '{"code":'+s.code+',"heads":"'+ _SytH_(s.heads)+'","ResponseFile":"'+ _SytH_(s.ResponseFile)+'"}'; 
        } catch (e) {
            Log("HTTP请求完成时 脚本中有错误 "+e);
			return "SunnyNetJS[error]:"+e;
        }
    case 3:
        try { 
            var s = TCPWillConnection(SyJsonOBJ);
            return '{"Display":'+s.Display+',"address":"'+s.address+'","ProxyUrl":"'+_SytH_(s.ProxyUrl)+'"}'; 
        } catch (e) {
        Log("TCP请求即将连接 脚本中有错误 "+e);
		return "SunnyNetJS[error]:"+e;
        }
    case 4:
         return "";
    case 5:
        return "";
    default:
        return "";
    }

    return "";
}

`

var JSCode = `
/*
 HttpRequest HTTP发送请求时需要处理的函数
 参数 结构如下
	Sunny =
		{ 
			"Display":true,
			"ClientIP":"127.0.0.1:1234",
			"url":"....",
			"heads":
				{
					"User-Agent":"...",
					"Accept":"..."
				},
			"method":"POST",
			"pid":0,
		}
--------------------------------------
*/
function HttpRequest(Sunny) {

    /*
    Sunny.pid 			类型 0=代理 其他=pid   			属性：(整数型)不可更改
    Sunny.Display  		是否在列表中显示(默认眿   			属性：(逻辑型)可更政
    Sunny.ClientIP 		客户端来源地址  					属性：(文本型)不可更改
    Sunny.url      		请求URL地址    					属性： (文本垿可更政
    Sunny.heads    		请求协议头	   					属性： (对象)可更政
    Sunny.method   		请求方法       					属性：(文本型)不可更改
    Sunny.ProxyUrl		代理设置						属性：(文本型)可更改
   */
    //delete Sunny.heads["Referer"]; //删除一个Head 
    //Sunny.heads["TokenSt"] = "123456"; //新增一个Head 

    /*
        if (Sunny.url.indexOf("appkey") != -1) {
            //对指定的链接使用指定的代理...... 
            Sunny.ProxyUrl = "http://127.0.0.1:8888"; 					//设置Http类型代理无账号
            Sunny.ProxyUrl = "http://admin:123456@127.0.0.1:8888";  	//设置Http类型代理账号admin密码123456
            Sunny.ProxyUrl = "socket5://127.0.0.1:8888";				//设置s5类型代理无账号
            Sunny.ProxyUrl = "socket5://admin:123456@127.0.0.1:8888";   //设置s5类型代理账号admin密码123456
    }
    */
    /*
    if (Sunny.url.indexOf("unify_logs") != -1) {
        //对指定的连接进行拦截,直接返回本地文件//直接回复文件 请求将不会显示到列表中
        //-------------  设置返回Head --------------------
        Sunny.ResponseHeads = "Content-Type: text/html; charset=utf-8" + "\r\n";
        Sunny.ResponseHeads += "Server: wServer" + "\r\n";
        //-------------  设置返回Head --------------------
        Sunny.ResponseFile = "C:\\Users\\report.html";
        return Sunny;
    }
    */
    /*
        if (Sunny.url.indexOf("123456") != -1) {
            //对指定的连接进行拦截不让数据发送出去
            Sunny.url = "http://127.0.0.1:1234"
            return Sunny;
        }
        if (Sunny.url.indexOf(".png") != -1 || Sunny.url.indexOf(".jpg") != -1) {
            //对指定条件的连接 不在列表中显示
            Sunny.Display = false;
        }
    */
    if (Sunny.url.indexOf("SunnyJS") != -1) {
        //对指定的链接进行替换
        Sunny.url = Sunny.url.replace("SunnyJS", "SunnyNet");
        return Sunny;
    }
    return Sunny;
}


/*
 HttpResponse HTTP请求完成时需要处理的函数
 参数 结构如下
	Sunny =
		{ 
			"ClientIP":"127.0.0.1:1234",
			"url":"....",
			"heads":"Server: xx\r\nDate: xxx\r\n....."
			"method":"POST",
			”code":200,
			"pid":0,
		}
--------------------------------------
*/
function HttpResponse(Sunny) {
    /*
    Sunny.pid 				类型 0=代理 其他=pid   			属性：(整数型)不可更改
    Sunny.ClientIP 			客户端来源地址  					属性：(文本型)不可更改
    Sunny.url      			请求URL地址    					属性：(文本型)不可更改
    Sunny.heads    			请求返回协议头 					属性：(文本型)可更政
    Sunny.method   			请求方法       					属性：(文本型)不可更改
    Sunny.code    			返回状态码     					属性：(整数型)可更政
    Sunny.ResponseFile    		要返回的文件     					属性：(文本型)可更政
*/

    //Sunny.heads = Sunny.heads.replace("Server: Tengine", ""); //从返回Heads中删除一个head 
    if (Sunny.method == "POST") {
        //==============================
		//直接返回本地文件
        //Sunny.ResponseFile = "C:\\Users\\report.html";
    }
    // 可以根据 ClientIP 进行一些判断操使
    return Sunny;
}

/*
 TCPWillConnection TCP请求即将连接
 参数 结构如下
	Sunny =
		{ 
			"ClientIP":"127.0.0.1:1234",
			"type":"TCP",
			"pid":0,
			"address":"8.8.8.8:8080",
		}
--------------------------------------
*/

function TCPWillConnection(Sunny) {
    //Sunny.pid 			类型 0=代理 其他=pid   										属性：(整数型)不可更改
    //Sunny.type 		连接类型 TCP、TLS-TCP、TCP-S5(TCP-S5为设置中开启了强制走TCP) 		属性：(文本型)不可更改
    //Sunny.ClientIP 		客户端来源地址    											属性：(文本型)不可更改
    //Sunny.address 		即将连接的地址     											属性：(文本型)可更政
    //Sunny.Display  		是否在列表中显示(默认真) 										属性：(逻辑型)可更政
    //Sunny.ProxyUrl		代理设置													属性：(文本型)可更改
    /*
        给这个连接设置S5代理。代理类型必须是S5类型
		//这里仅支持S5代理
		Sunny.ProxyUrl = "socket5://127.0.0.1:8888";				 //设置s5类型代理无账号
		Sunny.ProxyUrl = "socket5://admin:123456@127.0.0.1:8888";  	 //设置s5类型代理账号admin密码123456
    */
    if (Sunny.address == "192.168.31.154:443") {
        //重定向远程IP地址
        Sunny.address = "203.119.144.10:443";
    }
    return Sunny;
}

//请注意在自定义脚本中的中文都是UTF8编码,处理数据时，需要注意！ 
//-----------------------------------------------------------------------------------
/*
	额外命令: 
		Log()				日志 使用后在，设罿>脚本日志 中查眿支持任意数量参数
		MD5()					取MD5
		ReadFile(arg1)				读文件					arg1=读取文件路径 
		WriteFile(arg1,arg2)			写文件					arg1=保存文件路径	arg2=要写出的内容
		HttpPost(arg1,arg2,arg3)		发送POST请求				arg1=请求地址		arg2=协议头arg3=提交数据
		HttpGet(arg1,arg2)			发送GET 请求				arg1=请求地址		arg2=协议头
		THttpPost(arg1,arg2,arg3)	(无返回值)后台发送POST请求		arg1=请求地址		arg2=协议头arg3=提交数据
		THttpGet(arg1,arg2)		(无返回值)后台发送GET 请求			arg1=请求地址		arg2=协议头
*/
//-----------------------------------------------------------
/*
	发送POST请求 用法介绍 例如   (发送GET请求 同理)
	THttpPost ( "https://sdkapp.uve.weibo.com/interface/sdk/sdkad.php" , "Content-Type:application/x-www-form-urlencoded\r\nHost: sdkapp.uve.weibo.com" , "ad_token=200759445" )
	可以将指定数据发送到自己服务器
	使用 Log  可在日志中查看返回值   
	THttpGet THttpPost 由于是在后台提交无返回值，所以不能套Log查看返回值
*/

`
