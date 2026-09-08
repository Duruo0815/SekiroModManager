# AGENTS.md — AI 编码工具工作区指引

本项目是"只狼 MOD 管理器"（Windows，C# / .NET 8 WPF）。**开始任何工作前，先完整阅读 [DEVELOPMENT.md](DEVELOPMENT.md)**，尤其注意：

1. **§2 环境准备**：本机 `dotnet` 是用户级安装、不在 PATH（Git Bash 下用
   `/c/Users/cao12/AppData/Local/Microsoft/dotnet/dotnet.exe`）；Git Bash 下 MSBuild 属性必须写 `-p:Key=Value` 而不是 `/p:Key=Value`。
2. **§4 核心设计决策**：免管理员权限的硬链接优先挂载链、清单驱动的安全清理、优先级语义（数值大者胜出）、zip-slip 防御、modengine.ini 温和改写。这些是稳定契约，不得悄悄推翻。
3. **§9 WPF/STA 测试铁律**：STA 线程里创建的 `Application`/`Window`，线程退出前必须逐个 `Close()`、`Application.Current.Shutdown()`、`Dispatcher.InvokeShutdown()`，否则测试宿主直接崩溃。

工作规则：

- 依赖方向固定：`App → Core`、`Tests → Core + App`；Core 不得引入任何 UI 依赖，UI 只通过 `ModManager` 门面调业务。
- 每次修改后运行 `dotnet test SekiroModManager.sln`（当前 34 个用例必须全绿、连续运行稳定）。
- 修 bug 先写回归测试（模式参考 `tests/SekiroModManager.Tests/Core/HardeningTests.cs`）。
- 新建 xUnit 测试文件记得 `using Xunit;`（本项目已三次踩坑）。
- 测试不得写死机器相关绝对路径。
- 删除类操作必须有清单或校验依据；用户可见消息：Core 层放 `DeployResult.Warnings/Error`，App 层走 `SetStatus` + 可注入的 `MessageBoxAction`。
- Git：远程 `github.com/Duruo0815/SekiroModManager`（main），conventional commit 风格；提交前确认测试全绿。
- 当前路线（DEVELOPMENT.md §10）：预设/配置档 Profiles → MOD 元数据 → i18n → SoulsFormats 参数合并。
