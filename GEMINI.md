# GEMINI.md — 拉片助手（PianAssistant）工程规范真源

本文件是拉片助手（PianAssistant）唯一的工程规范来源。
README/AGENTS/PRD 与本文件冲突时，以本文件为准。
本规范与 StellarHub（千星汇图）GEMINI.md 同构，跨项目保持一致。

## 1. 项目身份

- 产品名：拉片助手（PianAssistant）—— 面向编导的视频拉片辅助工具
- 技术栈：C# / .NET 10 (`net10.0-windows`) / WPF，纯 MVVM
- 形态：Windows 10 21H2+ / 11 原生桌面应用；**禁止** WebView2、Electron、任何 HTML UI
- 核心功能：① 多格式视频播放（LibVLC 引擎） ② 静帧快速截图（TakeSnapshot）
  ③ 视频片段截取（ffmpeg.exe 子进程） ④ 拉片专业快捷键（达芬奇风格 + 可自定义）
  ⑤ 时间轴拖动/点击跳转 ⑥ 侧边栏面板切换（播放列表 / 笔记）
- 产品基线：`PRD/拉片助手产品需求.md`（M0–M4 基线）
- 里程碑状态：见 `CHANGELOG.md`；当前 **M0 v0.1.0 MVP 起步**

## 2. 解决方案布局

```
PianAssistant.sln
nuget.config                      # 只允许 nuget.org
src/PianAssistant/                # 主程序（WinExe）
  Models/                         # 纯数据/契约，无 UI 类型
  Services/                       # 业务逻辑，不引用任何 UI 类型
  Helpers/                        # MVVM 基元、纯工具
  Interop/                        # 所有 P/Invoke 隔离在此
  ViewModels/                     # 每个窗口/分区一个 ViewModel
  Views/                          # Window 与分区 UserControl
    Controls/                     # 自定义控件（TimelineControl 等）
  Themes/                         # Dark.xaml / Light.xaml / Generic.xaml
  Assets/                         # 图标、Logo
  Tools/ffmpeg/                   # ffmpeg.exe + ffprobe.exe（Content PreserveNewest）
  Data/                           # SQLite、Schema、迁移（M3+ 起）
tests/PianAssistant.Tests/        # xUnit，命名 {Subject}Tests.cs
tools/
  release/                        # 发布 PowerShell 逻辑
  smoke/                          # 烟雾脚本
docs/                             # UI_NUMBERING.md、USER_GUIDE.md
PRD/                              # 产品需求基线
samples/                          # 样例视频（.gitignore 大文件）
run.bat / run-hidden.vbs / run.ps1
build.bat / build.ps1
```

## 3. 命名与代码约定

- 命名空间一律 `PianAssistant.{Folder}`（如 `PianAssistant.Services`）
- 类/方法/属性 PascalCase；私有字段 `_camelCase`；常量 UPPER_SNAKE_CASE
- 文件编码 UTF-8（无 BOM）；启用 `Nullable` 与 `ImplicitUsings`
- MVVM：View 只绑定，不写业务逻辑；ViewModels 继承 `Helpers.ObservableObject`，
  命令使用 `RelayCommand` / `AsyncRelayCommand`
- **不引入**第三方 MVVM 框架或 DI 容器；服务聚合使用手写 `ServiceRegistry`（构造注入）
- Services 不引用 `System.Windows.*`；系统交互通过自有结构或 Interop 封装
- 所有 P/Invoke 放在 `Interop/`，不得散落在业务代码中

## 4. 数据与状态

- 数据根目录：`%APPDATA%\Qianlan_App\PianAssistant\`
  - `config.json` + `config.json.backup`（含输出目录、快捷键映射、最近文件）
  - `screenshots\` 截图输出目录
  - `clips\` 片段输出目录
  - `pianassistant.db`（SQLite，M3+ 起：项目/片段/截图/笔记历史）
  - `logs\error.log`（1MB 自动截断保留后半）
- 小文本状态（配置/导出元数据）必须经 `AtomicTextFile`：
  同目录临时文件 → 原子替换 → 保留上一完整副本为 `.backup`；
  主文件损坏时只允许从可解析的备份恢复，禁止静默覆盖唯一副本

## 5. Debug 隔离环境变量（仅 DEBUG 编译生效）

| 变量 | 作用 |
|---|---|
| `PIAN_ASSISTANT_DATA_ROOT` | 重定向数据根目录（测试/开发隔离） |
| `PIAN_ASSISTANT_UI_TEST_MODE` | `1`/`true` 时使用独立单实例互斥体与关机事件 |

Release 构建必须完全忽略这些变量。

## 6. 启动（run 三件套）

`run.bat` → `run-hidden.vbs`（无控制台）→ `run.ps1`：

1. 检查 .NET SDK
2. 若有已启动实例：置位命名事件请求**优雅退出**，等待最多 8 秒；失败则停止，不强杀
3. `dotnet build -c Debug`
4. 启动窗口程序；启动日志 `%TEMP%\PianAssistant.run.log`；PID 文件 `%TEMP%\PianAssistant.dev.pid`

单实例互斥体：`Global\PianAssistant_SingleInstance`（UI 测试态 `Local\PianAssistant_UiTest_SingleInstance`）；
关机事件：`Local\PianAssistant_GracefulShutdown`（UI 测试态带 `UiTest`）。

## 7. 构建与发布

- 版本号唯一来源：`src/PianAssistant/PianAssistant.csproj` 的 `<Version>`，格式 major.minor.patch；
  脚本与 UI 均从程序集/csproj 读取，**禁止在任何脚本中硬编码版本号**
- 日常构建：`build.bat`（= `build.ps1 -Mode Prepare`）：
  Release 编译（**0 警告**）→ `dotnet test` 全绿 → Self-Contained win-x64 + ReadyToRun 发布到 `artifacts/staging/`
- 版本号编码规则（与用户偏好一致，所有项目通用）：
  ① 每轮小修改 +0.0.1（修订号）；② 较大推进 +0.1.0（次版本号，修订号归零）；
  ③ 大版本 1.0.0 仅在全部需求功能实现完毕后**必须询问用户确认**。
  指令代号：NO.V = 交付版本（→1.0.0）；NO.M = 功能里程碑（+0.1.0）；NO.P = 补丁（+0.0.1）

## 8. 测试门禁

- `dotnet build -c Release`：0 错误 **0 警告**
- `dotnet test`：全绿；xUnit，断言用 xUnit 原生 Assert
- 核心 Service 必须有 `{Service}Tests.cs`，不依赖网络/显卡/真实模型：
  LibVLC 调用经接口假对象测试
- 破坏性场景（断电/损坏/断网）在里程碑验收时实测并记录

## 9. UI 编号体系

- W = 窗口；A = 顶部横区/页签；B = 主分区；C = 底部区；编号可继续下钻到控件（B3-3）
- 编号是位置锚点：改功能必须核对编号；删除即作废永不复用；新增顺延
- 唯一真源：`docs/UI_NUMBERING.md`，每次 UI 变更同步

## 10. 文档四件套

| 文件 | 读者 | 职责 |
|---|---|---|
| GEMINI.md | 工程/AI | 规范真源（本文件） |
| AGENTS.md | AI 代理 | 开工指令入口，指向本文件 |
| README.md | 人类 | 项目介绍与快速开始 |
| CHANGELOG.md | 人类 | 版本变更记录 |

## 11. 视频处理契约（M0 起）

- **播放引擎**：LibVLCSharp.WPF + VideoLAN.LibVLC.Windows（libvlc.dll + 插件 x64）
- **截图**：优先 `MediaPlayer.TakeSnapshot(0, path, 0, 0)`（同帧精度、零开销）；
  高画质重编码场景降级 `ffmpeg.exe -ss {t} -frames:v 1 {out}`
- **片段截取**：ffmpeg.exe 子进程。默认 `-c copy` 流复制（秒级、无损、关键帧对齐）；
  "精确剪切"选项走 `-c:v libx264 -preset fast -crf 18 -c:a aac` 重编码（毫秒精度、有损）
- **ffmpeg 部署**：`src/PianAssistant/Tools/ffmpeg/` 下作 `<Content PreserveNewest>` 随包携带，
  不依赖 PATH；embedded resource 不可行（exe 无法直接执行）
- **快捷键**：达芬奇风格默认映射 + W3 设置页可自定义；M0 走 `KeyBinding`（窗口级），
  M2+ 提供系统级热键选项
