package main

import (
	"encoding/base64"
	"fmt"
	"net/http"
	"net/url"
	"strconv"
	"strings"
)

type CreateRequest struct {
	URL      *url.URL
	Method   string
	Header   http.Header
	Body     []byte
	FuncName string
	Cookie   string
}

func CreateRequestCode(TheologyArray []int, Lang string, module string) string {
	index := 0
	code := ""
	for _, k := range TheologyArray {
		h := HashMap.GetRequest(k)
		if h != nil {
			u, e := url.Parse(h.URL)
			if e != nil {
				continue
			}
			mm := &CreateRequest{URL: u, Header: cloneHeader(h.Header), Body: h.Body, Method: h.Method}
			index++
			mm.FuncName = "SunnyNetCreateRequest" + strconv.Itoa(index)
			mm.Cookie = mm.Header.Get("Cookie")
			mm.Header.Del("Cookie")
			if Lang == "E" {
				code += mm.ELang(module)
			} else if Lang == "C#" {
				code += mm.CSharp(module)
			} else if Lang == "Go" {
				code += mm.Go(module)
			} else if Lang == "Python" {
				code += mm.Python(module)
			} else if Lang == "Java" {
				code += mm.Java(module)
			} else if Lang == "JavaScript" {
				code += mm.JavaScript(module)
			} else if Lang == "火山" {
				code += mm.Hs(module)
			}
		}
	}
	if Lang == "E" {
		code = ".版本 2\n.支持库 spec\n\n" + code
	}
	if Lang == "Java" {
		code = wrapJavaCode(code, module, index)
	}
	if Lang == "JavaScript" {
		code = wrapJavaScriptCode(code, index)
	}
	code = strings.ReplaceAll(code, "\r", "")
	code = strings.ReplaceAll(code, "\n", "\r\n")
	return code
}

func cloneHeader(header http.Header) http.Header {
	cloned := make(http.Header, len(header))
	for key, values := range header {
		cloned[key] = append([]string(nil), values...)
	}
	return cloned
}
func reText(string2 string) string {
	s := strings.ReplaceAll(string2, "\\", "\\\\")
	return strings.ReplaceAll(s, "\"", "\\\"")
}
func (e *CreateRequest) Hs(module string) string {
	code := "<火山程序 类型 = \"通常\" 版本 = 1 />\r\n\r\n"
	if module == "WinHttpW" {
		code += "方法 " + e.FuncName + " <注释 = \"本函数由SunnyNet网络中间件生成,请搭配精易模块使用 " + e.URL.Path + "\">\n{\n"
		BytesType := e.IsBytesType()
		code += "    变量 局_HTTP <类型 = WinHttpW>\n"

		code += "    变量 局_请求地址 <类型 = 文本型>\n"
		if BytesType {
			code += "    变量 局_请求数据 <类型 = 字节集类>\n"
		} else {
			code += "    变量 局_请求数据 <类型 = 文本型>\n"
		}
		if e.Cookie != "" {
			code += "    变量 局_请求Cookie <类型 = 文本型>\n"
		}
		code += "    变量 局_响应字节集 <类型 = 字节集类>\n"
		code += "    变量 局_响应文本 <类型 = 文本型>\n"

		code += "    局_请求地址 ＝ \"" + reText(e.URL.String()) + "\"\n\n"

		if BytesType {
			code += "    局_请求数据 ＝ BASE64文本到字节集 (\"" + base64.StdEncoding.EncodeToString(e.Body) + "\")\n\n"
		} else {
			code += "    局_请求数据 ＝ \"" + reText(string(e.Body)) + "\"\n"
		}
		if e.Cookie != "" {
			code += "    局_请求Cookie ＝ \"" + reText(e.Cookie) + "\"\n"
		}

		code += "    局_HTTP.Open (\"" + e.Method + "\", 局_请求地址)\n"

		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if k == "Accept-Encoding" {
				if len(v) < 1 {
					code += "    //局_HTTP.SetRequestHeader (\"" + k + "\",\"\")\n"
				} else {
					code += "    //局_HTTP.SetRequestHeader (\"" + k + "\",\"" + reText(v[0]) + "\")\n"
				}
				continue
			}
			if len(v) < 1 {
				code += "    局_HTTP.SetRequestHeader (\"" + k + "\",\"\")\n"
			} else {
				code += "    局_HTTP.SetRequestHeader (\"" + k + "\",\"" + reText(v[0]) + "\")\n"
			}
		}
		if e.Cookie != "" {
			code += "    局_HTTP.SetRequestHeader (\"Cookie\",局_请求Cookie)\n"
		}
		if e.Method == "GET" {
			code += "    局_HTTP.Send()\n"
		} else {
			if BytesType {
				code += "    局_HTTP.SendBin(局_请求数据)\n"
			} else {
				code += "    局_HTTP.Send(局_请求数据)\n"
			}
		}
		code += "    局_响应字节集 = 局_HTTP.GetResponseBody ()\n"
		code += "    局_响应文本 ＝ 多字节到文本 (局_响应字节集)\n"
		code += "    调试输出 (局_响应文本)\n\n"
	}
	code += "}\n"
	return code
}
func (e *CreateRequest) ELang(module string) string {
	code := ""
	if module == "WinInet" || module == "WinHttpW" || module == "WinHttpR" {
		code += ".子程序 " + e.FuncName + ", , 公开, 本子程序由Sunny中间件生成,请配合 [WinHttp模块] 使用。 " + e.URL.Path + "\n.局部变量 局_HTTP, " + module + "\n"
		BytesType := e.IsBytesType()
		if BytesType {
			code += ".局部变量 局_提交字节集, 字节集, , , \n"
		} else {
			code += ".局部变量 局_提交数据, 文本型, , , \n"
		}
		if e.Cookie != "" {
			code += ".局部变量 局_提交Cookie, 文本型, , , \n"
		}
		code += ".局部变量 局_响应字节集, 字节集, , , \n.局部变量 局_响应文本, 文本型, , , \n\n"

		if BytesType {
			code += "局_提交字节集 ＝ 编码_BASE64解码 (“" + base64.StdEncoding.EncodeToString(e.Body) + "”)\n\n"
		} else {
			ok, d := e.IsFormData()
			if ok {
				code += d + "\n"
			} else {
				code += "局_提交数据 ＝ " + convertELangFormat(string(e.Body)) + "\n"
			}
		}
		if e.Cookie != "" {
			code += "局_提交Cookie ＝ 子文本替换 (" + convertELangFormat(e.Cookie) + ", “'”, #引号, , , 真)\n\n"
		}
		code += "局_HTTP.Open (“" + e.Method + "”, “" + e.URL.String() + "”)\n"
		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if k == "Accept-Encoding" {
				if len(v) < 1 {
					code += "' 局_HTTP.SetHeader (“" + k + "”, “”)\n"
				} else {
					code += "' 局_HTTP.SetHeader (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
				}
				continue
			}
			if len(v) < 1 {
				code += "局_HTTP.SetHeader (“" + k + "”, “”)\n"
			} else {
				code += "局_HTTP.SetHeader (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
			}
		}
		if e.Cookie != "" {
			code += "局_HTTP.SetHeader (“Cookie”, 局_提交Cookie)\n\n"
		}
		if BytesType {
			code += "局_HTTP.SendBin (局_提交字节集)\n"
		} else {
			code += "局_HTTP.Send (局_提交数据)\n"
		}
		code += "局_响应字节集 ＝ 局_HTTP.GetBody ()\n"
		code += "局_响应文本 ＝ 编码_Utf8到Ansi (局_响应字节集)\n"
		code += "调试输出 (局_响应文本)\n\n"
	}
	if module == "e2ee" {
		code += ".子程序 " + e.FuncName + ", , 公开, 本子程序由Sunny中间件生成,请配合 [E2EE] 使用。 " + e.URL.Path + "\n.局部变量 局_HTTP, 网站客户端\n"
		BytesType := e.IsBytesType()
		code += ".局部变量 局_网址, 文本型, , , \n"
		if BytesType {
			code += ".局部变量 局_提交数据, 字节集, , , \n"
		} else {
			code += ".局部变量 局_提交数据, 文本型, , , \n"
		}
		if e.Cookie != "" {
			code += ".局部变量 局_提交Cookie, 文本型, , , \n"
		}
		code += ".局部变量 局_响应字节集, 字节集, , , \n.局部变量 局_响应文本, 文本型, , , \n\n"

		code += "局_网址 ＝ " + convertELangFormat(e.URL.String()) + "\n\n"
		if BytesType {
			code += "局_提交数据 ＝ 编码_BASE64解码 (“" + base64.StdEncoding.EncodeToString(e.Body) + "”)\n\n"
		} else {
			ok, d := e.IsFormData()
			if ok {
				code += d + "\n"
			} else {
				code += "局_提交数据 ＝ " + convertELangFormat(string(e.Body)) + "\n"
			}
		}
		if e.Cookie != "" {
			code += "局_提交Cookie ＝ 子文本替换 (" + convertELangFormat(e.Cookie) + ", “'”, #引号, , , 真)\n\n"
		}
		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if k == "Accept-Encoding" {
				if len(v) < 1 {
					code += "' 局_HTTP.置请求头 (“" + k + "”, “”)\n"
				} else {
					code += "' 局_HTTP.置请求头 (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
				}
				continue
			}
			if len(v) < 1 {
				code += "局_HTTP.置请求头 (“" + k + "”, “”)\n"
			} else {
				code += "局_HTTP.置请求头 (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
			}
		}
		if e.Cookie != "" {
			code += "局_HTTP.置请求头 (“Cookie”, 局_提交Cookie)\n\n"
		}
		if e.Method == "GET" {
			code += "局_HTTP.执行GET (局_网址 , 局_响应字节集, 真, )\n"
		} else {
			code += "局_HTTP.执行POST (局_网址,局_提交数据, 局_响应字节集, 真, )\n"
		}
		code += "局_响应文本 ＝ 到文本 (局_响应字节集)\n"
		code += "调试输出 (局_响应文本)\n\n"
	}
	if module == "网页_访问" || module == "网页_访问_对象" {
		code += ".子程序 " + e.FuncName + ", , 公开, 本子程序由Sunny中间件生成,请配合 [精易模块] 使用。 " + e.URL.Path + "\n"
		BytesType := e.IsBytesType()
		code += ".局部变量 局_网址, 文本型, , , \n"
		if BytesType {
			code += ".局部变量 局_提交数据, 字节集, , , \n"
		} else {
			code += ".局部变量 局_提交数据, 文本型, , , \n"
		}
		code += ".局部变量 局_协议头, 类_POST数据类, , , \n"

		if e.Cookie != "" {
			code += ".局部变量 局_提交Cookie, 文本型, , , \n"
		}
		code += ".局部变量 局_响应字节集, 字节集, , , \n.局部变量 局_响应文本, 文本型, , , \n\n"

		code += "局_网址 ＝ " + convertELangFormat(e.URL.String()) + "\n\n"

		if BytesType {
			code += "局_提交数据 ＝ 编码_BASE64解码 (“" + base64.StdEncoding.EncodeToString(e.Body) + "”)\n\n"
		} else {
			ok, d := e.IsFormData()
			if ok {
				code += d + "\n"
			} else {
				code += "局_提交数据 ＝ " + convertELangFormat(string(e.Body)) + "\n"
			}
		}
		if e.Cookie != "" {
			code += "局_提交Cookie ＝ 子文本替换 (" + convertELangFormat(e.Cookie) + ", “'”, #引号, , , 真)\n\n"
		}

		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if k == "Accept-Encoding" {
				if len(v) < 1 {
					code += "' 局_协议头.添加 (“" + k + "”, “”)\n"
				} else {
					code += "' 局_协议头.添加 (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
				}
				continue
			}
			if len(v) < 1 {
				code += "局_协议头.添加 (“" + k + "”, “”)\n"
			} else {
				code += "局_协议头.添加 (“" + k + "”, " + convertELangFormat(v[0]) + ")\n"
			}
		}
		if e.Cookie != "" {
			code += "局_协议头.添加 (“Cookie”, 局_提交Cookie)\n\n"
		}
		if e.Method == "GET" {
			code += "局_响应字节集 ＝ " + module + " (局_网址, 0, , , , 局_协议头.获取协议头数据 ())\n"
		} else {
			mod := "1"
			if e.Method == "POST" {
				mod = "1"
			} else if e.Method == "HEAD" {
				mod = "2"
			} else if e.Method == "PUT" {
				mod = "3"
			} else if e.Method == "OPTIONS" {
				mod = "4"
			} else if e.Method == "DELETE" {
				mod = "5"
			} else if e.Method == "TRACE" {
				mod = "6"
			} else if e.Method == "CONNECT" {
				mod = "7"
			}
			if BytesType {
				if module == "网页_访问_对象" {
					code += "局_响应字节集 ＝ " + module + " (局_网址, " + mod + ", , , , 局_协议头.获取协议头数据 (),,,,局_提交数据)\n"
				} else {
					code += "局_响应字节集 ＝ " + module + " (局_网址, " + mod + ", , , , 局_协议头.获取协议头数据 (),,,局_提交数据)\n"
				}

			} else {
				code += "局_响应字节集 ＝ " + module + " (局_网址, " + mod + ", 局_提交数据, , , 局_协议头.获取协议头数据 ())\n"
			}
		}
		code += "局_响应文本 ＝ 到文本 (局_响应字节集)\n"
		code += "调试输出 (局_响应文本)\n\n"
	}
	return code
}

func (e *CreateRequest) Python(module string) string {
	code := ""
	if module == "requests" {
		_header := ""
		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if len(v) < 1 {
				_header += `        '` + k + `': "",` + "\n"
			} else {
				_header += `        '` + k + `': "` + strReplaceAll([]byte(v[0])) + `",` + "\n"
			}
		}
		if e.Cookie != "" {
			_header += `        'Cookie': "` + strReplaceAll([]byte(e.Cookie)) + `",` + "\n"
		}
		BytesType := e.IsBytesType()
		payload := ""
		if BytesType {
			payload = `encoded_data = "` + base64.StdEncoding.EncodeToString(e.Body) + `"
    payload = base64.b64decode(encoded_data)`
		} else {
			payload = `payload = "` + strReplaceAll(e.Body) + `"`
		}

		_t := `def ` + e.FuncName + `():
    """ 
    [ ` + e.URL.Path + ` ]
    本函数由SunnyNet网络中间件生成   
    """
    url = "` + e.URL.String() + `"
    ` + payload + `
    headers = {
` + _header + `
    }
    response = requests.request("` + e.Method + `", url, data=payload, headers=headers)

    print(response.text)
`
		return _t
	}
	if module == "Flurl" {
		return code
	}
	return code
}

func wrapJavaCode(code string, module string, count int) string {
	if count == 0 || code == "" {
		return ""
	}

	imports := `import java.nio.charset.StandardCharsets;
import java.util.Base64;
`
	if module == "OkHttp" {
		imports += `import okhttp3.MediaType;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;
`
	} else {
		imports += `import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
`
	}

	main := "    public static void main(String[] args) throws Exception {\n"
	for index := 1; index <= count; index++ {
		main += "        SunnyNetCreateRequest" + strconv.Itoa(index) + "();\n"
	}
	main += "    }\n\n"
	return imports + "\npublic class SunnyNetRequestCode {\n" + main + code + "}\n"
}

func wrapJavaScriptCode(code string, count int) string {
	if count == 0 || code == "" {
		return ""
	}

	main := "(async () => {\n"
	for index := 1; index <= count; index++ {
		main += "    await SunnyNetCreateRequest" + strconv.Itoa(index) + "();\n"
	}
	main += "})();\n\n"
	return main + code
}

func (e *CreateRequest) Java(module string) string {
	switch module {
	case "OkHttp":
		return e.JavaOkHttp()
	case "HttpURLConnection":
		return e.JavaHttpURLConnection()
	default:
		return e.JavaHttpClient()
	}
}

func (e *CreateRequest) JavaHttpClient() string {
	headers := ""
	for k, v := range e.Header {
		if skipGeneratedHeader(k) {
			continue
		}
		headers += "        builder.header(" + codeString(k) + ", " + codeString(firstHeaderValue(v)) + ");\n"
	}
	if e.Cookie != "" {
		headers += "        builder.header(\"Cookie\", " + codeString(e.Cookie) + ");\n"
	}

	bodyPublisher := "HttpRequest.BodyPublishers.noBody()"
	if e.AllowsRequestBody() {
		bodyPublisher = "HttpRequest.BodyPublishers.ofByteArray(body)"
	}

	return `    public static void ` + e.FuncName + `() throws Exception {
        String url = ` + codeString(e.URL.String()) + `;
        byte[] body = Base64.getDecoder().decode(` + codeString(base64.StdEncoding.EncodeToString(e.Body)) + `);

        HttpClient client = HttpClient.newBuilder()
                .followRedirects(HttpClient.Redirect.NORMAL)
                .build();

        HttpRequest.Builder builder = HttpRequest.newBuilder()
                .uri(URI.create(url))
                .method(` + codeString(e.Method) + `, ` + bodyPublisher + `);
` + headers + `
        HttpResponse<byte[]> response = client.send(builder.build(), HttpResponse.BodyHandlers.ofByteArray());
        System.out.println("Status: " + response.statusCode());
        System.out.println(new String(response.body(), StandardCharsets.UTF_8));
    }

`
}

func (e *CreateRequest) JavaOkHttp() string {
	headers := ""
	for k, v := range e.Header {
		if skipGeneratedHeader(k) {
			continue
		}
		headers += "                .addHeader(" + codeString(k) + ", " + codeString(firstHeaderValue(v)) + ")\n"
	}
	if e.Cookie != "" {
		headers += "                .addHeader(\"Cookie\", " + codeString(e.Cookie) + ")\n"
	}

	contentType := e.Header.Get("Content-Type")
	if contentType == "" {
		contentType = "application/octet-stream"
	}

	bodyLine := "        RequestBody requestBody = null;\n"
	if e.AllowsRequestBody() {
		bodyLine = "        RequestBody requestBody = RequestBody.create(MediaType.parse(" + codeString(contentType) + "), body);\n"
	}

	return `    public static void ` + e.FuncName + `() throws Exception {
        String url = ` + codeString(e.URL.String()) + `;
        byte[] body = Base64.getDecoder().decode(` + codeString(base64.StdEncoding.EncodeToString(e.Body)) + `);

        OkHttpClient client = new OkHttpClient();
` + bodyLine + `
        Request request = new Request.Builder()
                .url(url)
                .method(` + codeString(e.Method) + `, requestBody)
` + headers + `                .build();

        try (Response response = client.newCall(request).execute()) {
            System.out.println("Status: " + response.code());
            System.out.println(response.body() == null ? "" : response.body().string());
        }
    }

`
}

func (e *CreateRequest) JavaHttpURLConnection() string {
	headers := ""
	for k, v := range e.Header {
		if skipGeneratedHeader(k) {
			continue
		}
		headers += "        connection.setRequestProperty(" + codeString(k) + ", " + codeString(firstHeaderValue(v)) + ");\n"
	}
	if e.Cookie != "" {
		headers += "        connection.setRequestProperty(\"Cookie\", " + codeString(e.Cookie) + ");\n"
	}

	writeBody := ""
	if e.AllowsRequestBody() {
		writeBody = `        connection.setDoOutput(true);
        try (OutputStream output = connection.getOutputStream()) {
            output.write(body);
        }
`
	}

	return `    public static void ` + e.FuncName + `() throws Exception {
        URL url = new URL(` + codeString(e.URL.String()) + `);
        byte[] body = Base64.getDecoder().decode(` + codeString(base64.StdEncoding.EncodeToString(e.Body)) + `);

        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod(` + codeString(e.Method) + `);
        connection.setInstanceFollowRedirects(true);
` + headers + writeBody + `
        int status = connection.getResponseCode();
        byte[] response = (status >= 400 ? connection.getErrorStream() : connection.getInputStream()).readAllBytes();
        System.out.println("Status: " + status);
        System.out.println(new String(response, StandardCharsets.UTF_8));
        connection.disconnect();
    }

`
}

func (e *CreateRequest) JavaScript(module string) string {
	if module != "fetch" {
		return ""
	}

	headers := ""
	for k, v := range e.Header {
		if strings.ToUpper(k) == "CONTENT-LENGTH" {
			continue
		}
		headers += "        " + codeString(k) + ": " + codeString(firstHeaderValue(v)) + ",\n"
	}
	if e.Cookie != "" {
		headers += "        \"Cookie\": " + codeString(e.Cookie) + ",\n"
	}

	bodyOption := ""
	if e.AllowsRequestBody() {
		bodyOption = "        body,\n"
	}

	return `async function ` + e.FuncName + `() {
    const url = ` + codeString(e.URL.String()) + `;
    const body = Buffer.from(` + codeString(base64.StdEncoding.EncodeToString(e.Body)) + `, "base64");
    const response = await fetch(url, {
        method: ` + codeString(e.Method) + `,
        headers: {
` + headers + `        },
` + bodyOption + `    });
    console.log("Status:", response.status);
    console.log(await response.text());
}

`
}

func (e *CreateRequest) AllowsRequestBody() bool {
	method := strings.ToUpper(e.Method)
	return method != "" && method != "GET" && method != "HEAD"
}

func skipGeneratedHeader(name string) bool {
	switch strings.ToUpper(name) {
	case "CONTENT-LENGTH", "HOST":
		return true
	default:
		return false
	}
}

func firstHeaderValue(values []string) string {
	if len(values) == 0 {
		return ""
	}
	return values[0]
}

func codeString(value string) string {
	var builder strings.Builder
	builder.WriteByte('"')
	for _, r := range value {
		switch r {
		case '\\':
			builder.WriteString("\\\\")
		case '"':
			builder.WriteString("\\\"")
		case '\r':
			builder.WriteString("\\r")
		case '\n':
			builder.WriteString("\\n")
		case '\t':
			builder.WriteString("\\t")
		default:
			if r < 0x20 || r == 0x2028 || r == 0x2029 {
				builder.WriteString(fmt.Sprintf("\\u%04X", r))
			} else {
				builder.WriteRune(r)
			}
		}
	}
	builder.WriteByte('"')
	return builder.String()
}
func strReplaceAll(abody []byte) string {
	ss := strings.ReplaceAll(string(abody), "\\", "\\\\")
	ss = strings.ReplaceAll(ss, "\"", "\\\"")
	return ss
}
func (e *CreateRequest) CSharp(module string) string {
	code := ""

	if module == "RestSharp" {
		templateData := ""
		templateData1 := ""
		if len(e.Body) > 0 {
			if e.IsBytesType() {
				templateData = `string base64String = "` + base64.StdEncoding.EncodeToString(e.Body) + `";  
            byte[] body = Convert.FromBase64String(base64String); `
			} else {
				templateData = `string String = "` + strReplaceAll(e.Body) + `";  
            byte[] body = Encoding.Default.GetBytes(String); `
			}
		}

		mod := "Method.Post"
		s := strings.ToUpper(e.Method)
		{
			if s == "POST" {
				mod = "Method.Post"
			}
			if s == "GET" {
				mod = "Method.Get"
			}
			if s == "PUT" {
				mod = "Method.Put"
			}
			if s == "DELETE" {
				mod = "Method.Delete"
			}
			if s == "HEAD" {
				mod = "Method.Head"
			}
			if s == "OPTIONS" {
				mod = "Method.Options"
			}
			if s == "PATCH" {
				mod = "Method.Patch"
			}
			if s == "MERGE" {
				mod = "Method.Merge"
			}
			if s == "COPY" {
				mod = "Method.Copy"
			}
			if s == "SEARCH" {
				mod = "Method.Search"
			}
		}
		header := ""
		CONTENTMENT := "application/x-www-form-urlencoded"
		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if strings.ToUpper(k) == "ACCEPT-ENCODING" {
				if len(v) > 0 {
					header += `            //request.AddHeader("` + k + `","` + strReplaceAll([]byte(v[0])) + `")` + ";\n"
				}
				continue
			}
			if strings.ToUpper(k) == "CONTENT-TYPE" {
				if len(v) > 0 {
					ss := strings.Split(v[0]+";", ";")
					if len(ss) > 0 {
						CONTENTMENT = ss[0]
					}
				}

			}
			if len(v) < 1 {
				//request.AddHeader("cookie", "xxxxx");
				header += `            request.AddHeader("` + k + `","")` + ";\n"
			} else {
				header += `            request.AddHeader("` + k + `","` + strReplaceAll([]byte(v[0])) + `")` + ";\n"
			}
		}
		if e.Cookie != "" {
			header += `            request.AddHeader("Cookie","` + strReplaceAll([]byte(e.Cookie)) + `")` + ";\n"
		}
		if len(e.Body) > 0 {
			templateData1 = `            request.AddParameter("` + CONTENTMENT + `", body, ParameterType.RequestBody); 
            `
		}
		_tmp := `/// <summary> 
        /// ` + e.FuncName + ` ` + e.URL.Path + `
        ///<para>本函数由SunnyNet网络中间件生成</para> 
        /// </summary> 
        public static void ` + e.FuncName + `()
        {
            string url = "` + e.URL.String() + `";
            ` + templateData + ` 
            var client = new RestClient(url);
            var request = new RestRequest("",` + mod + `);
` + header + templateData1 + `var response = client.Execute(request); 
            Trace.WriteLine("Response StateCode:" + ((int)response.StatusCode)); 
            Trace.WriteLine("Response Text:\n" + response.Content); 
        }
`
		return _tmp
	}
	if module == "HttpClient" {
		templateData := `byte[] data = Encoding.Default.GetBytes(""); `
		if len(e.Body) > 0 {
			if e.IsBytesType() {
				templateData = `string base64String = "` + base64.StdEncoding.EncodeToString(e.Body) + `";  
            byte[] data = Convert.FromBase64String(base64String); `
			} else {
				templateData = `string String = "` + strReplaceAll(e.Body) + `";  
            byte[] data = Encoding.Default.GetBytes(String); `
			}
		}
		mod := "Method.Post"
		CONTENTMENT := "application/x-www-form-urlencoded"
		s := strings.ToUpper(e.Method)
		{
			if s == "POST" {
				mod = "HttpMethod.Post"
			}
			if s == "GET" {
				mod = "HttpMethod.Get"
			}
			if s == "PUT" {
				mod = "HttpMethod.Put"
			}
			if s == "DELETE" {
				mod = "HttpMethod.Delete"
			}
			if s == "HEAD" {
				mod = "HttpMethod.Head"
			}
			if s == "OPTIONS" {
				mod = "HttpMethod.Options"
			}
			if s == "PATCH" {
				mod = "HttpMethod.Patch"
			}
			if s == "MERGE" {
				mod = "HttpMethod.Merge"
			}
			if s == "COPY" {
				mod = "HttpMethod.Copy"
			}
			if s == "SEARCH" {
				mod = "HttpMethod.Search"
			}
		}
		header := ""
		for k, v := range e.Header {
			if strings.ToUpper(k) == "CONTENT-LENGTH" {
				continue
			}
			if strings.ToUpper(k) == "CONTENT-TYPE" {
				if len(v) > 0 {
					ss := strings.Split(v[0]+";", ";")
					if len(ss) > 0 {
						CONTENTMENT = ss[0]
					}
				}
				continue
			}
			if strings.ToUpper(k) == "ACCEPT-ENCODING" {
				if len(v) > 0 {
					header += `            //client.DefaultRequestHeaders.Add("` + k + `","` + strReplaceAll([]byte(v[0])) + `")` + ";\n"
				}
				continue
			}
			if len(v) < 1 {
				header += `            client.DefaultRequestHeaders.Add("` + k + `","")` + ";\n"
			} else {
				header += `            client.DefaultRequestHeaders.Add("` + k + `","` + strReplaceAll([]byte(v[0])) + `")` + ";\n"
			}
		}
		if e.Cookie != "" {
			header += `            client.DefaultRequestHeaders.Add("Cookie","` + strReplaceAll([]byte(e.Cookie)) + `")` + ";\n"
		}
		_tmp := `/// <summary> 
        /// ` + e.FuncName + ` ` + e.URL.Path + `
        ///<para>本函数由SunnyNet网络中间件生成</para> 
        /// </summary> 
        public static async void ` + e.FuncName + `()
        {
            string url = "` + e.URL.String() + `";
            ` + templateData + `
            using (HttpClient client = new HttpClient())
            {
                ` + header + `
                HttpContent content = new ByteArrayContent(data);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("` + CONTENTMENT + `");
                HttpRequestMessage request = new HttpRequestMessage(` + mod + `, url);
                request.Content = content;
                HttpResponseMessage response = client.SendAsync(request).Result;
 
                Trace.WriteLine("Response Status code: " + response.StatusCode);
                byte[] responseBytes = response.Content.ReadAsByteArrayAsync().Result;
                Trace.WriteLine("Response bytes: " + BitConverter.ToString(responseBytes));
                Trace.WriteLine("Response Content: " + response.Content.ReadAsStringAsync().Result);
                 
            }
        }
`
		return _tmp
	}

	return code
}

func (e *CreateRequest) Go(module string) string {
	templateData := ""
	if e.IsBytesType() {
		templateData = `	_Base64 := "` + base64.StdEncoding.EncodeToString(e.Body) + `"
	_Data, _ := base64.StdEncoding.DecodeString(_Base64)
	Body := io.NopCloser(bytes.NewBuffer(_Data))`
	} else {
		templateData = `	Body := io.NopCloser(bytes.NewBuffer([]byte("` + strReplaceAll(e.Body) + `")))`
	}
	header := ""

	for k, v := range e.Header {
		if strings.ToUpper(k) == "CONTENT-LENGTH" {
			continue
		}
		if len(v) < 1 {
			header += `	req.Header.Set("` + k + `","")` + "\n"
		} else {
			header += `	req.Header.Set("` + k + `","` + strReplaceAll([]byte(v[0])) + `")` + "\n"
		}
	}
	if e.Cookie != "" {
		header += `	req.Header.Set("Cookie","` + strReplaceAll([]byte(e.Cookie)) + `")` + "\n"
	}
	template := `// ` + e.FuncName + ` 本函数由SunnyNet网络中间件生成  //` + e.URL.Path + `
func ` + e.FuncName + `() {
` + templateData + `
	defer func() { _ = Body.Close() }()
	req, err := http.NewRequest("` + e.Method + `", "` + e.URL.String() + `", Body)
	if err != nil {
		panic(err)
	}
	` + header + `
	res, err := http.DefaultClient.Do(req)
	if err != nil {
		panic(err)
	}
	defer func() { _ = res.Body.Close() }()
	body, err := io.ReadAll(res.Body)
	if err != nil {
		panic(err)
	}
	fmt.Println("响应状态码", res.StatusCode)
	for k, v := range res.Header {
		fmt.Println(k, v)
	}
	fmt.Println(string(body))
}
`
	return template
}
func (e *CreateRequest) IsBytesType() bool {
	for _, v := range e.Body {
		if v < 32 && v != 10 && v != 13 {
			return true
		}
	}
	return false
}
func (e *CreateRequest) IsFormData() (bool, string) {
	p := string(e.Body)
	if !strings.Contains(p, "&") {
		return false, ""
	}
	if !strings.Contains(p, "=") {
		return false, ""
	}
	if strings.HasPrefix(p, "{") || strings.HasPrefix(p, "[") {
		return false, ""
	}
	Array := strings.Split(p, "&")
	Code := ""
	for index, v := range Array {
		Array1 := strings.Split(v, "=")
		if len(Array1) == 1 {
			Code += "局_提交数据 ＝ “" + Array1[0] + "=”\n"
		} else if len(Array1) == 2 {
			value, ex := url.QueryUnescape(Array1[1])
			if ex != nil {
				if index == 0 {
					Code += "局_提交数据 ＝ “" + Array1[0] + "=" + Array1[1] + "”\n"
				} else {
					Code += "局_提交数据 ＝ 局_提交数据 ＋ “&" + Array1[0] + "=" + Array1[1] + "”\n"
				}
				continue
			}
			if index == 0 {
				if isChinese(value) || value != Array1[1] {
					Code += "局_提交数据 ＝ “" + Array1[0] + "=” ＋ 编码_URL编码 (" + convertELangFormat(value) + ",真,真)\n"
				} else {
					Code += "局_提交数据 ＝ “" + Array1[0] + "=” ＋ " + convertELangFormat(value) + "\n"
				}
			} else {
				if isChinese(value) || value != Array1[1] {
					Code += "局_提交数据 ＝ 局_提交数据 ＋ “&" + Array1[0] + "=” ＋ 编码_URL编码 (" + convertELangFormat(value) + ",真,真)\n"
				} else {
					Code += "局_提交数据 ＝ 局_提交数据 ＋ “&" + Array1[0] + "=” ＋ " + convertELangFormat(value) + "\n"
				}
			}
		}
	}

	return true, Code
}
func isChinese(str string) bool {
	for _, v := range str {
		if v > 255 {
			return true
		}
	}
	return false
}

func convertELangFormat(v string) string {
	str := v
	str = strings.ReplaceAll(str, "“", "\"")
	str = strings.ReplaceAll(str, "”", "\"")
	str = strings.ReplaceAll(str, "\r\n", "\n")
	str = strings.ReplaceAll(str, "\n", "\r\n")
	if v == "\r\n" || v == "\n" || v == "\r" {
		return "#换行符"
	}
	if !strings.Contains(str, "\"") && !strings.Contains(str, "\r\n") {
		return "“" + str + "”"
	}
	if strings.Contains(str, "\r\n") {
		arr := strings.Split(str, "\r\n")
		str = ""
		a := false
		for _, va := range arr {
			if !strings.Contains(va, "gzip, ") && !strings.Contains(va, "Content-Length:") && !strings.Contains(va, "Accept-Encoding: gzip") {
				str += va + "\r\n"
				a = true
			}
		}
		if a && strings.HasSuffix(str, "\r\n") {
			str = str[:len(str)-2]
		}
	}
	fh := ""
	issc := false
	if strings.Contains(str, "\"") {
		if !strings.Contains(str, "'") {
			fh = "'"
		} else if !strings.Contains(str, "#") {
			fh = "#"
		} else if !strings.Contains(str, "~") {
			fh = "~"
		} else if !strings.Contains(str, "!") {
			fh = "!"
		} else if !strings.Contains(str, "|") {
			fh = "|"
		} else if !strings.Contains(str, "/") {
			fh = "/"
		} else if !strings.Contains(str, "\\") {
			fh = "\\"
		} else if !strings.Contains(str, "&") {
			fh = "&"
		} else if !strings.Contains(str, "*") {
			fh = "*"
		} else {
			issc = true
			str = "\"" + strings.ReplaceAll(str, "\"", "\"+#引号+\"") + "\""
			fh = ""
		}
		if fh != "" {
			str = strings.ReplaceAll(str, "\"", fh)
		}
	}

	if strings.Contains(str, "\r\n") {
		if issc {
			str = strings.ReplaceAll(str, "\r\n", "\"+#换行符+\"")
		} else {
			str = "\"" + strings.ReplaceAll(str, "\r\n", "\"+#换行符+\"") + "\""
		}
	}
	str = strings.ReplaceAll(str, "+\"\"", "")
	str = strings.ReplaceAll(str, "\"\"", "")
	if strings.HasPrefix(str, "+") {
		str = str[1:]
	}
	if strings.HasPrefix(str, "+") {
		str = str[1:]
	}
	if strings.Contains(str, "++") {
		str = strings.ReplaceAll(str, "++", "+")
	}
	if fh != "" {
		if strings.HasPrefix(str, "\"") {
			str = "子文本替换 (" + str + ", \"" + fh + "\",#引号, , , 真)"
		} else {
			str = "子文本替换 (“" + str + "”, \"" + fh + "\",#引号, , , 真)"
		}
	}
	return str
}
