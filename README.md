# SunnyNet WPF

`Go` 核心保留，界面层改为 `C# + WPF`，仅面向 `Windows`，并由 WPF 直接调用 `Go DLL`。

## 结构

- `backend/`：从 `SunnyNetTools` 后端整理出的 `Go` 核心，并编译为供 WPF 直调的 `DLL`
- `src/SunnyNet.Wpf/`：WPF 桌面界面
- `_source/`：分析与兼容补丁用的上游源码副本
- `_toolchain/`：本地 Zig 工具链，用于生成可被 Windows 正常加载的 Go `c-shared DLL`

## 构建

```powershell
dotnet build SunnyNet.sln
```

构建时会自动：

- 编译 `src/SunnyNet.Wpf/`
- 用 Zig 工具链编译 `backend/`
- 将 `SunnyNetBridge.dll` 输出到 `src/SunnyNet.Wpf/bin/<Configuration>/net8.0-windows/backend/`
- WPF 通过 `P/Invoke` + 本地事件轮询直接调用 Go 核心

## 运行

直接启动：

```powershell
src\SunnyNet.Wpf\bin\Debug\net8.0-windows\SunnyNet.Wpf.exe
```

WPF 前端会直接加载同目录下的 `backend/SunnyNetBridge.dll`。

## 文档

- `docs/MCP工具清单.md`：当前 WPF 版实际支持的 MCP 工具、参数与返回字段说明
