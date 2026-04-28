# SunnyNet WPF

SunnyNet WPF 是基于 SunnyNet Go 核心的 Windows 抓包分析工具。项目使用 `C# + WPF` 重绘桌面界面，核心抓包、代理、脚本、证书与协议处理仍由 Go 后端提供，并以 `c-shared DLL` 方式供 WPF 直接调用。

仓库地址：https://github.com/hmlyn/SunnyNet-wpf

作者：`hmlyn`

仓库描述：

> SunnyNet WPF 是基于 SunnyNet Go 核心的 Windows 抓包分析工具，使用 C# + WPF 重绘界面，支持 HTTP/HTTPS、WebSocket、TCP/UDP、断点拦截、重放、请求规则、请求构造器、MCP 与常用调试工具。

## 功能概览

- Windows x64 桌面应用，界面层使用 WPF，运行产物为 `SunnyNet.exe`。
- Go 后端编译为 `backend/SunnyNetBridge.dll`，WPF 通过 P/Invoke 直接调用 SunnyNet 核心能力。
- 仓库已随项目分发 SunnyNet 核心兼容源码，后端通过本地 `replace` 引用，不再依赖额外手动准备核心源码。
- 支持 HTTP/HTTPS、WebSocket、TCP、UDP 会话捕获、筛选、查看和分析。
- 支持进程筛选、域名筛选、会话收藏、备注、标记颜色、列宽记忆和窗口布局记忆。
- 支持断点拦截、普通重发、运行到结束、断开连接，以及请求/响应内容修改。
- 支持请求规则，包括 HTTP/WebSocket/TCP/UDP 屏蔽、请求重写、请求映射。
- 支持请求构造器，可自行构造 GET/POST 等请求，并支持从会话列表回填重放和历史记录缓存。
- 支持会话右键复制请求代码，包含 C#、Java、Go、Python、JavaScript、易语言、火山等模板。
- 支持请求/响应文本查找和高亮，查找窗口命中后可联动右侧详情视图。
- 支持 WebSocket 消息流、主动发送、消息重放、HEX/原文/JSON/Protobuf 视图。
- 支持 TCP/UDP 专用数据视图，避免复用 HTTP/WebSocket 视图造成信息冗余。
- 支持脚本扩展，可在脚本中处理、解密或转换数据，并写入显示层明文，不破坏原始请求/响应数据。
- 内置文本工具、JSON 结构编辑器、加密/解密工具、开源协议与赞赏页面。
- 支持 SunnyNet MCP，可让 AI 会话读取、搜索、标记、收藏、备注和分析抓包会话。

## 最新版本

当前发布版本：`v0.1.3`

发布包命名：

```text
SunnyNet-wpf-v0.1.3-win-x64.zip
```

Release 包含 Windows x64 可运行程序、WPF 主程序、Go 后端 DLL、随程序运行所需资源，以及可选 MCP 桥接程序。

## 本次核心更新

- 移除内置“请求解密”规则功能，避免规则体系过重；复杂解密建议通过脚本扩展完成。
- 保留脚本显示层明文能力，可将解密结果用于右侧请求/响应查看，同时不破坏原始网络数据。
- 精简请求规则入口和配置结构，降低规则中心维护成本。

## 目录结构

- `backend/`：Go 后端桥接层，编译为 `backend/SunnyNetBridge.dll`。
- `third_party/SunnyNet/`：随仓库分发的 SunnyNet Go 核心兼容源码。
- `src/SunnyNet.Wpf/`：WPF 桌面程序源码。
- `docs/`：项目文档，例如 MCP 工具清单。
- `sunnymcptool/`：可选的上游 MCP 工具源码目录，默认不随仓库提交，需自行克隆。
- `_toolchain/`：可选本地工具链目录，例如 Zig。
- `build-debug.bat`：Debug 构建脚本，输出到 `artifacts/Debug/`。
- `build-release.bat`：Release 构建脚本，输出到 `artifacts/Release/`，并移除 pdb。

## 编译环境

必须环境：

- Windows 10/11 x64
- .NET SDK 8.0+
- Go 1.24.x
- Zig 0.15.2 或兼容版本，用于让 Go 生成 Windows 可加载的 `c-shared DLL`
- Git

推荐环境：

- Visual Studio 2022，用于打开、调试 WPF 项目。
- PowerShell 7 或 Windows PowerShell，用于运行构建脚本和 MCP 构建脚本。

可选环境：

- `sunnymcptool`，用于打包 MCP 桥接程序。

## SunnyNet 核心分发说明

项目已随仓库分发 SunnyNet Go 核心兼容源码：

```text
third_party/SunnyNet
```

当前 `backend/go.mod` 使用本地 replace：

```text
replace github.com/qtgolang/SunnyNet => ../third_party/SunnyNet
```

这样首次拉取仓库后即可直接编译后端，也方便项目维护必要的核心层补丁。后续如果 SunnyNet 核心需要增强或修复，建议在 `third_party/SunnyNet` 内维护兼容补丁，并同步验证 WPF 调用链。

## 获取源码

```powershell
git clone https://github.com/hmlyn/SunnyNet-wpf.git
cd SunnyNet-wpf
```

仓库当前不依赖 Git submodule，`third_party/SunnyNet` 已在仓库内分发。

## Zig 准备

两种方式任选一种。

方式一：安装 `zig` 并加入 `PATH`：

```powershell
zig version
```

方式二：将 Zig 解压到项目本地路径：

```text
_toolchain/zig-x86_64-windows-0.15.2/zig.exe
```

项目会优先使用本地 `_toolchain`，如果不存在则使用 `PATH` 中的 `zig`。

## MCP 准备

MCP 是可选项。若需要把 MCP 桥接程序一起打包，请在项目根目录旁准备上游 `sunnymcptool`：

```powershell
git clone https://github.com/a121400/sunnymcptool.git
cd sunnymcptool
.\scripts\build.ps1 -Target mcp
```

编译后应存在：

```text
sunnymcptool/build/bin/sunnynet-mcp.exe
```

WPF 构建时会自动复制到运行目录：

```text
mcp/sunnynet-mcp.exe
```

如果没有该文件，主程序仍可编译运行，只是 MCP 功能不可用。

## 编译方式

Debug：

```bat
build-debug.bat
```

Release：

```bat
build-release.bat
```

也可以直接使用：

```powershell
dotnet build .\SunnyNet.sln -c Debug
dotnet build .\SunnyNet.sln -c Release
```

建议发布包使用 `build-release.bat`，因为该脚本会输出到固定目录，并删除 pdb 文件。

## 输出位置

使用脚本构建后：

```text
artifacts/Debug/SunnyNet.exe
artifacts/Release/SunnyNet.exe
```

使用 `dotnet build` 默认构建后：

```text
src/SunnyNet.Wpf/bin/Debug/net8.0-windows/SunnyNet.exe
src/SunnyNet.Wpf/bin/Release/net8.0-windows/SunnyNet.exe
```

运行时主程序会加载同目录下的 Go 后端 DLL：

```text
backend/SunnyNetBridge.dll
```

如果启用了 MCP，还会加载：

```text
mcp/sunnynet-mcp.exe
```

## 发布打包

推荐流程：

```bat
build-release.bat
```

然后将下面目录压缩为 zip：

```text
artifacts/Release/
```

推荐命名：

```text
SunnyNet-wpf-v0.1.3-win-x64.zip
```

## 常见问题

如果提示找不到 Zig：

- 确认 `zig version` 可以在 PowerShell 中正常执行。
- 或确认本地路径 `_toolchain/zig-x86_64-windows-0.15.2/zig.exe` 存在。

如果 Go 后端 DLL 没有生成：

- 确认已经安装 Go 1.24.x。
- 确认 Zig 可用。
- 确认 `third_party/SunnyNet` 目录存在。
- 重新运行 `build-debug.bat` 或 `build-release.bat`。

如果 MCP 文件没有出现在编译产物里：

- 先编译 `sunnymcptool` 的 MCP 目标。
- 确认 `sunnymcptool/build/bin/sunnynet-mcp.exe` 存在。
- 重新运行 `build-debug.bat` 或 `build-release.bat`。

如果需要对加密请求显示明文：

- 推荐使用内置脚本能力处理。
- 脚本可以写入显示层明文，不需要修改原始请求/响应数据。
- 不建议使用固定规则堆叠复杂解密流程，灵活性和可维护性都不如脚本。

## 文档

- `docs/MCP工具清单.md`：当前 WPF 版支持的 MCP 工具、参数与返回字段说明。

## 开源协议

SunnyNet WPF 版采用 MIT 协议开源。你可以自由使用、复制、修改、合并、发布、分发或二次开发本项目，但需要保留原始版权声明和协议声明。

本项目仅用于合法的网络调试、接口分析、测试与技术研究场景。请勿将本工具用于未授权抓包、绕过访问控制、攻击或其它违法用途。
