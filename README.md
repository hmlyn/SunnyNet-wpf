# SunnyNet WPF

基于 SunnyNet Go 核心的 Windows 抓包分析工具。项目使用 `C# + WPF` 重绘桌面界面，核心抓包、代理、脚本与协议处理仍由 Go 后端提供，并以 `c-shared DLL` 方式供 WPF 直接调用。

GitHub 仓库描述建议：

> SunnyNet WPF 是基于 SunnyNet Go 核心的 Windows 抓包分析工具，使用 C# + WPF 重绘界面，支持 HTTP/HTTPS、WebSocket、TCP/UDP、断点拦截、重放、脚本解密、MCP 与常用调试工具。

## 功能概览

- 纯 Windows 桌面应用，界面层使用 WPF，运行产物为 `SunnyNet.exe`。
- Go 后端编译为 `SunnyNetBridge.dll`，WPF 通过 P/Invoke 与本地事件轮询直接调用。
- 支持 HTTP/HTTPS、WebSocket、TCP、UDP 抓包展示。
- 支持会话筛选、进程筛选、域名筛选、收藏、备注、列宽与布局记忆。
- 支持断点拦截、请求/响应修改、重放、替换规则、请求证书、Hosts 规则。
- 支持 WebSocket 消息流、消息重放、HEX/原文/Protobuf 视图。
- 支持脚本扩展，并提供显示层解密能力，不破坏原始请求/响应数据。
- 内置文本工具、JSON 结构编辑器、加密/解密工具。
- 支持 SunnyNet MCP，可让 AI 会话读取、搜索、标记和分析抓包会话。

## 目录结构

- `backend/`：Go 后端桥接层，编译为 `backend/SunnyNetBridge.dll`。
- `src/SunnyNet.Wpf/`：WPF 桌面程序。
- `docs/`：项目文档与脚本示例。
- `build-debug.bat`：Debug 构建脚本，输出到 `artifacts/Debug/`。
- `build-release.bat`：Release 构建脚本，输出到 `artifacts/Release/`，不生成 pdb。

## 编译环境

必须环境：

- Windows 10/11 x64
- .NET SDK 8.0+
- Go 1.24.x
- Zig 0.15.2 或兼容版本，用于让 Go 生成 Windows 可加载的 `c-shared DLL`
- Git

可选环境：

- Visual Studio 2022，用于打开和调试 WPF 项目。
- `sunnymcptool`，用于打包 MCP 桥接程序。

## 上游依赖准备

当前 `backend/go.mod` 使用本地 replace：

```text
replace github.com/qtgolang/SunnyNet => ../_source/SunnyNet-v1.0.3-patched
```

因此首次编译前，需要准备上游 SunnyNet 兼容源码到：

```text
_source/SunnyNet-v1.0.3-patched
```

该目录默认不提交到仓库，避免把第三方源码副本和临时分析文件直接打进项目。若缺少该目录，Go 后端编译会失败。后续如果维护公开 fork/tag，可以把 `backend/go.mod` 的 `replace` 改成对应公开地址。

## Zig 准备

两种方式任选一种：

1. 安装 `zig` 并加入 `PATH`。
2. 将 Zig 解压到项目本地路径：

```text
_toolchain/zig-x86_64-windows-0.15.2/zig.exe
```

项目会优先使用本地 `_toolchain`，如果不存在则使用 `PATH` 中的 `zig`。

## MCP 准备

MCP 是可选项。若需要把 MCP 桥接程序一起打包，请在项目根目录旁准备：

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

## 编译

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

运行时 WPF 会加载同目录下的：

```text
backend/SunnyNetBridge.dll
```

## 常见问题

如果提示找不到 Zig：

- 确认 `zig version` 可以在 PowerShell 中正常执行。
- 或确认本地路径 `_toolchain/zig-x86_64-windows-0.15.2/zig.exe` 存在。

如果提示找不到 SunnyNet patched source：

- 确认 `_source/SunnyNet-v1.0.3-patched` 已准备好。
- 或修改 `backend/go.mod` 中的 `replace` 指向你自己的 SunnyNet 兼容源码。

如果 MCP 文件没有出现在编译产物里：

- 先编译 `sunnymcptool` 的 MCP 目标。
- 确认 `sunnymcptool/build/bin/sunnynet-mcp.exe` 存在。
- 重新运行 `build-debug.bat` 或 `build-release.bat`。

## 文档

- `docs/MCP工具清单.md`：当前 WPF 版支持的 MCP 工具、参数与返回字段说明。
