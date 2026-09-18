# 只狼 MOD 管理器 — 开发与维护指南

> 本文档面向后续接手开发的开发者 / AI 编码工具（Antigravity 等）。
> 目标：读完本文即可安全地继续开发，不破坏既有设计契约。
> 最后更新：2026-09-08，与最新修复对齐（39/39 测试全绿）。

---

## 1. 项目概述与当前状态

一个 Windows 桌面工具：在独立仓库中管理《只狼》MOD，一键启用/禁用，按优先级把 MOD 文件挂载到游戏 `mods/` 目录（基于 Sekiro Mod Engine 的散文件加载机制）。带 WPF 图形界面（MVVM），支持白天/黑夜双主题。

| 项 | 状态 |
|---|---|
| 核心库 `SekiroModManager.Core` | ✅ 稳定，UI 无关，所有业务逻辑都在这里 |
| WPF 前端 `SekiroModManager.App` | ✅ 完成（MVVM + 双主题 + 部署计划窗） |
| 自动化测试 | ✅ 39/39 通过（`dotnet test`），连续运行稳定 |
| 一轮系统性 bug 审查 | ✅ 已完成（悬空链接清理、zip-slip 等已修复并有回归测试） |
| Git | ✅ 已推送 GitHub：`https://github.com/Duruo0815/SekiroModManager.git`（main 分支） |

---

## 2. 环境准备（重要：本机的特殊性）

- .NET SDK：**8.0.424，用户级安装**，位置 `C:\Users\cao12\AppData\Local\Microsoft\dotnet\`。
  ⚠️ **`dotnet` 不在系统 PATH 里**（官方 `dotnet-install.ps1` 安装，非 winget）。Git Bash 下：
  ```bash
  /c/Users/cao12/AppData/Local/Microsoft/dotnet/dotnet.exe --version
  ```
  根目录的 `启动管理器.bat` 已自动处理该路径；新脚本也应兼容它。
- 目标框架 `net8.0-windows`（注册表 API + kernel32 硬链接 P/Invoke，Windows-only 是有意为之）。
- NuGet 依赖：`SharpCompress 0.38.0`（zip/7z/rar）、`System.Text.Encoding.CodePages 8.0.0`（GBK）；测试侧 `xunit 2.9.2` + `Microsoft.NET.Test.Sdk 17.11.1`。
- ⚠️ Git Bash 会把 MSBuild 风格参数 `/p:Foo=Bar` 当路径转换，必须写 `-p:Foo=Bar`（见 §8）。

---

## 3. 解决方案结构

```
SekiroModManager/
├── SekiroModManager.sln               # 三个项目：Core / App / Tests
├── README.md / LICENSE / AGENTS.md    # 根目录保留（GitHub 首页 + AI 工具指引）
├── docs/
│   └── DEVELOPMENT.md                 # 本文档（开发与维护指南）
├── src/
│   ├── SekiroModManager.Core/         # 【核心引擎库】零 UI 依赖
│   │   ├── ModManager.cs              # ★ 门面：UI 唯一入口
│   │   ├── ConfigStore.cs             # config.json 读写
│   │   ├── Json.cs                    # 全局 JSON 选项（camelCase/中文不转义/缩进）
│   │   ├── Models/                    # ModItem / AppConfig / DeployPlan / DeployResult
│   │   └── Services/
│   │       ├── GameLocator.cs         # 注册表 + Steam libraryfolders.vdf 定位（AppID 814380）
│   │       ├── ModEngineService.cs    # Mod Engine 检测 + modengine.ini 温和改写（自动 .bak）
│   │       ├── ModArchiveService.cs   # 解压（zip/7z/rar）+ 路径归一化 + zip-slip 防御
│   │       └── DeployEngine.cs        # ★ 冲突计划 + 硬链接挂载 + 清单驱动清理
│   └── SekiroModManager.App/          # 【WPF 桌面端】MVVM
│       ├── App.xaml(.cs)              # 启动入口，初始化 ThemeManager
│       ├── Assets/                    # app.ico / app.png 图标资源
│       ├── Common/                    # ObservableObject / RelayCommand / Converters / FormatHelper
│       ├── Theme/                     # DarkTheme / LightTheme / Styles 资源字典
│       │   ├── ThemeManager.cs        # 主题切换（theme.json 持久化）+ 静态事件 ThemeChanged
│       │   └── WindowTitleBarHelper.cs# DWM API 沉浸式标题栏
│       ├── ViewModels/                # MainViewModel（主界面全部逻辑）/ ModItemViewModel
│       └── Views/                     # MainWindow / PlanWindow（部署计划确认窗）
└── tests/SekiroModManager.Tests/
    ├── Core/                          # 引擎测试：归一化 / 计划 / 部署 / 清理 / 加固回归（24 用例）
    │   ├── ModRootNormalizationTests.cs
    │   ├── DeployPlanTests.cs
    │   ├── DeployEngineTests.cs
    │   ├── ImportTests.cs
    │   └── HardeningTests.cs          # 隐藏文件 / 悬空链接 / zip-slip / 只读文件 / 前缀目录
    └── App/AppViewModelTests.cs       # VM / 主题 / 转换器 / STA 窗口冒烟 / 回归测试（15 用例）
```

**依赖方向（必须保持）**：`App → Core`，`Tests → Core + App`。`Core` 不引用任何 UI 库。

---

## 4. 核心设计决策（改动前必读）

这些是刻意选择的设计，**不要在后续迭代中悄悄推翻**；若确需变更，先同步更新本文档与 README。

### 4.1 挂载策略：自动降级链，默认免管理员权限

`DeployEngine.CreateLink(source, target)` 按序尝试：

1. **硬链接**（同卷）—— P/Invoke `CreateHardLinkW`，免管理员、免开发者模式，性能等同物理文件；
2. **符号链接** —— `File.CreateSymbolicLink`（跨卷，需开发者模式或管理员）；
3. **物理复制** —— 兜底。

❌ **不做** `requireAdministrator` manifest。程序常态免 UAC 是核心卖点，新功能不得破坏。语义细节：
- 删除链接用 `File.Delete`（Win32 `DeleteFileW` 语义），只删链接不动源文件；
- 硬链接部署的文件在 MOD 被 remove 后依然有效（数据安全）；符号链接会悬空（见 4.2）；
- exFAT 等不支持硬链接的卷自动降级，最终以 `DeployedCounts` 报告实际方式。

### 4.2 清理：清单驱动，绝不误删

每次部署写入 `mods/.modmanager/manifest.json`（相对路径、源文件、归属 MOD、挂载方式）。清理规则：
- **只删清单内条目**，玩家手动放进 `mods/` 的文件永远不动；
- 无清单且目录有内容 → 警告并跳过；清单损坏 → 警告并跳过；
- **悬空符号链接**（`File.Exists` 探测不到）也按清单删除——修过的 bug，有回归测试 `悬空符号链接_按清单清理` 与 `部署时目标为悬空链接_重新部署成功`，勿回退；
- 空目录只沿清单条目的祖先链收缩，绝不触碰 `mods/` 根与 `.modmanager/`；
- 只读的托管目标文件可被安全覆盖/清理（有回归测试）。

### 4.3 优先级语义

- `ModItem.Priority` **数值越大越优先**，冲突时覆盖低优先级的同名文件；
- 计划按优先级**升序**遍历、同路径后来者覆盖（`OrdinalIgnoreCase` 字典，与 Windows 文件系统一致）；
- 冲突只记录不阻止部署，`DeployPlan.Conflicts` 供 UI 预览（PlanWindow 展示后用户确认才 deploy）。

### 4.4 导入归一化

`ModArchiveService.FindModRoot` 广度优先（深度 ≤8）找"直接包含特征目录（`chr/`、`parts/` 等）或 `.dcx`/`.param` 文件的最浅目录"作 MOD 根。zip 解压逐条目校验路径在解压根内（防 zip-slip），非 UTF-8 条目名按 GBK 解码。扫描统一用 `ModArchiveService.ScanOptions`（`AttributesToSkip = None`——**隐藏文件也要部署**，这也是修过的 bug）。

### 4.5 modengine.ini 温和改写

只改写明确命中的 `moddirectory`/`moddir` 键且仅当值不对时改；改前备份 `.bak`；ini 不可写时降级为警告、部署继续。**不要**改成"每次全量重写 ini"。

### 4.6 安全设计要点（新增代码同样遵守）

- 任何删除操作必须有清单或校验依据，禁止"递归删目录一了百之"；
- 压缩包条目路径必须校验在解压根内；
- 部署前必须检查 `sekiro` 进程（Core 的 `DeployEngine.IsGameRunning` 与 VM 的 `DeployModsAsync` 双重检查）；
- 面向用户的通知：Core 层放 `Warnings/Error`，App 层走 `MainViewModel.SetStatus` + `MessageBoxAction`（可注入替身，测试已利用这一点）。

---

## 5. 关键流程调用链

```
导入:  UI 拖拽/选择 → MainViewModel.ImportSingleAsync / ImportMultipleAsync（Task.Run 包裹）
       → ModManager.Import(path, name?)
         → ModArchiveService.ExtractArchive（zip 先解到 storage/.tmp，同卷 Move）或 CopyDirectory
         → ModArchiveService.FindModRoot（归一化剥嵌套）
         → 写 storage/<modId>/，登记 ModItem（Priority = 当前最大 + 10，默认启用），SaveConfig
       → 回 UI 线程 ReloadMods()

计划:  MainViewModel.ShowPlanWindow → ModManager.Plan() → DeployEngine.Plan(启用的 MOD)
       → DeployPlan { Files(唯一生效), Conflicts(被覆盖), Warnings } → PlanWindow 展示

部署:  PlanWindow 确认 → MainViewModel.DeployModsAsync → ModManager.Deploy(plan?)
       → 校验游戏路径 / sekiro 进程
       → ModEngineService.EnsureModDir（ini 维护，失败降级为警告）
       → DeployEngine.Deploy(plan)
         → CleanInternal（清单驱动清理旧条目 + 空目录收缩）
         → 逐文件 CreateLink（硬链接→符号链接→复制）
         → 写新 manifest（只含本次成功条目，失败重试自洽）
       → DeployResult { Success, Warnings, DeployedCounts, RemovedStale, Duration } 回 UI

清理:  MainViewModel.CleanModsAsync → ModManager.Clean() → DeployEngine.Clean() → CleanInternal
```

UI 线程安全模式：所有耗时 IO（导入/部署/清理）都在 `Task.Run` 里跑，结果回 UI 线程刷新；`IsBusy/BusyText` 驱动遮罩。新功能遵循同一模式。

---

## 6. 数据文件格式

程序目录（绿色便携）：`config.json`、`storage/<modId>/`、`theme.json`；游戏目录：`mods/`、`mods/.modmanager/manifest.json`。

`config.json`（camelCase）：

```json
{
  "gamePath": "D:\\SteamLibrary\\steamapps\\common\\Sekiro",
  "storageRoot": "storage",
  "mods": [
    {
      "id": "m20260906213137xxxx",
      "name": "白发狼外观",
      "sourcePath": "D:\\Downloads\\WhiteHair.zip",
      "sourceUrl": null,
      "version": null,
      "importedAt": "2026-09-06T21:31:37.81+08:00",
      "enabled": true,
      "priority": 10
    }
  ]
}
```

`mods/.modmanager/manifest.json`（每次 deploy 全量重写，`kind` ∈ HardLink/SymbolicLink/Copy）：

```json
[
  {
    "relativePath": "chr\\am_m_9000.partsbnd.dcx",
    "sourcePath": "...\\storage\\mxxx\\chr\\am_m_9000.partsbnd.dcx",
    "ownerModId": "m20260906213137xxxx",
    "kind": "HardLink"
  }
]
```

`id` 格式：`m` + `yyyyMMddHHmmss` + 4 位 hex。`theme.json`：`{ "Theme": "Dark" | "Light" }`。

---

## 7. Core 门面 API（App 已依赖的稳定契约）

```csharp
var mm = new ModManager();                       // 可传 baseDir，默认 exe 目录（便携）
mm.Config / mm.SaveConfig()
mm.SetGamePath(path) -> bool                     // 校验 sekiro.exe 后写入
mm.LocateGame() -> string                        // 自动定位并保存，失败返回 ""
mm.FindMod(idOrPrefix) -> ModItem?               // id 或唯一前缀
mm.Import(sourcePath, displayName?) -> ImportResult   // { ModId, ModRoot, Warnings }
mm.RemoveMod(id) -> bool
mm.Plan() -> DeployPlan
mm.Deploy(plan?) -> DeployResult
mm.Clean() -> DeployResult
mm.CheckModEngine() -> ModEngineStatus           // { Dinput8Present, IniPresent, Installed }
mm.IsGameRunning() -> bool
mm.LaunchGame() -> bool
```

**稳定性承诺**：§4 的设计决策与本节签名是稳定契约。Core 内部可随意重构；改动公共 API 或语义时，必须同步更新本文档、README、`App` 调用点与全部测试。

---

## 8. 构建、测试、发布

```bash
# dotnet 见 §2；Git Bash 下 MSBuild 属性必须用 -p: 而非 /p:

dotnet build SekiroModManager.sln
dotnet test SekiroModManager.sln            # 39 个用例必须全绿

# 运行桌面端（开发调试）
dotnet run --project src/SekiroModManager.App

# 发布（框架依赖单文件，产物输出到根目录 release/）
dotnet publish src/SekiroModManager.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishDir=../../../release/
# 产物: release/SekiroModManager.App.exe（双击直接启动）
```

---

## 9. 测试注意事项（含已踩过的坑）

- 全部测试不依赖真实游戏，用 `Path.GetTempPath()` 临时目录 + `IDisposable` 清理；
- 新测试文件必须 `using Xunit;`（本项目已三次因漏写编译失败）；
- **WPF/STA 测试铁律**：在自建 STA 线程里创建 `Application`/`Window` 后，线程退出前**必须**关闭全部窗口（`Application.Current.Windows` 逐个 `Close()`）、`Application.Current.Shutdown()` 并 `Dispatcher.CurrentDispatcher.InvokeShutdown()`。否则线程清理时 USER32 向已死亡的托管线程回调 WndProc，**测试宿主直接崩溃**（`MS.Win32.HwndSubclass` NRE）——已修复过一次，模式见 `AppViewModelTests.WpfWindows_CanInstantiateAndRenderOnStaThread` 的 finally 块，勿回退；
- 测试不得写死绝对路径（曾有人写死 `D:\Zcode WorkSpace\...`，已改为 `FindAppAsset` 从 BaseDirectory 向上定位 sln）；
- 无权限环境（创建符号链接失败）时相关用例**主动 return 跳过**——主路径硬链接不依赖该权限，属有意设计；
- 修 bug 先写回归测试，组织在 `HardeningTests.cs`。

---

## 10. 路线图（建议接手顺序）

1. **预设/配置档（Profiles）**：一组 `enabled + priority` 快照，一键整套切换（"全动作包"/"纯外观"/"难度增强"）。建议 `AppConfig.Profiles: List<Profile>`，Profile 持有 modId → (enabled, priority) 映射；UI 加方案下拉框。
2. **MOD 元数据**：`ModItem.SourceUrl/Version` 字段已预留，UI 补录入口即可；可扩展 NexusMods 信息展示与更新检查。
3. **i18n**：简/繁/英（README 路线图已列）。
4. **SoulsFormats 参数合并**（低优先级）：`gameparam.parambnd.dcx` 字段级合并，引入 `SoulsFormats`，建议新增 `Services/ParamMergeService.cs`，投入产出比低放最后。

---

## 11. 已知限制（现状如实记录）

- 跨卷部署且无符号链接权限时降级为物理复制（占磁盘但可用）；
- 移除 MOD 后：硬链接部署的文件继续有效，符号链接悬空、复制文件残留——由下次 `deploy`/`clean` 按清单处理；
- `FindMod` 前缀歧义时返回 null（不提示歧义）；
- `MainViewModel` 构造时订阅 `ThemeManager.ThemeChanged`，实例销毁时不退订（长生命周期下轻微泄漏，VM 与应用同生命周期时可接受；做 Profiles 时顺手改为 IDisposable）；
- `ConfigStore.Load` 在 config.json 损坏时静默回退默认配置（storage 的 MOD 文件不受影响，但列表需重导）；
- 游戏运行检测与实际写盘之间存在 TOCTOU 窗口（MVP 接受）；
- `LaunchGame` 直接启动 `sekiro.exe`，依赖 Mod Engine 的 dinput8.dll 注入机制，未处理 Steam DRM 弹窗场景。

---

## 12. Git 与修改约定

- 远程：`https://github.com/Duruo0815/SekiroModManager.git`（main）。提交信息用 conventional 风格（`feat:` / `fix:` / `test:`，参考 initial commit）；
- 代码注释用中文，风格与现有一致——只在**代码本身表达不了的约束或设计原因**处注释；
- 命名空间：`SekiroModManager.Core(.Models/.Services)`、`SekiroModManager.App(.Common/.Theme/.ViewModels/.Views)`；
- JSON 一律走 `Json.Options`；新模型不要自建序列化选项；
- 异常原则：Core 预期内失败返回结果对象（`DeployResult.Error`），App 层 catch 后走 `SetStatus` + `MessageBoxAction`；
- 每次改动跑 `dotnet test`；涉及挂载/清理的改动同时跑一次真机验证（`启动管理器.bat` → plan → deploy → 打开游戏目录核对 `mods/`）。
