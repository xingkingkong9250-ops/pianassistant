# CHANGELOG — 拉片助手（PianAssistant）

本文件记录所有版本变更。版本号遵循三段式 `主版本.次版本.修订号`，
唯一来源为 `src/PianAssistant/PianAssistant.csproj` 的 `<Version>`。

---

## [Unreleased]

### v0.1.1 — 启动崩溃修复（NO.P，2026-10-12）

#### 修复

- **SxS 并行配置错误**：`app.manifest` 根元素属性名 `manifest` 应为 `manifestVersion`，
  导致 Windows SxS 系统无法解析清单，应用完全无法启动。移除 csproj 中
  `<ApplicationManifest>` 引用，由 SDK 自动生成默认清单（PerMonitorV2 DPI 由
  csproj `<ApplicationHighDpiMode>` 处理，不再在 manifest 中重复声明）。
- **VideoView.MediaPlayer 未绑定**：`App.xaml` 的 `StartupUri` 让窗口在
  `base.OnStartup()` 时就创建并触发 `Loaded`，但此时 `DataContext` 尚未设置，
  导致 `VideoView.MediaPlayer` 从未绑定到 LibVLC 播放器，视频无法显示。
  改为手动创建窗口、先设 `DataContext` 再 `Show()`。
- **TimelineControl 渲染崩溃**：`OnRender` 在控件初始布局阶段
  `ActualWidth/ActualHeight` 为 0 时直接创建 `Rect`，抛出
  `ArgumentException: 宽度和高度必须为非负值`。加入 `<= 0` 提前返回保护。
- **打开视频后不自动播放**：`ShellViewModel.OpenMedia` 只调用 `Open(path)`
  设置媒体但未调用 `Play()`。末尾追加 `PlayPauseCommand.Execute(null)` 自动播放。

### M0 v0.1.0 — MVP 起步（NO.M，2026-10-12）

**首版拉片助手**：基于 LibVLCSharp + WPF 的桌面视频拉片工具，覆盖编导核心拉片工作流。

#### 新增

- 项目骨架：`PianAssistant.sln` + `src/PianAssistant` + `tests/PianAssistant.Tests`
  + `tools` + `docs` + `PRD`，与 StellarHub 同构（`net10.0-windows` + 纯 MVVM）
- 工程规范四件套：`GEMINI.md` / `AGENTS.md` / `README.md` / `CHANGELOG.md`
  + `nuget.config`（仅 nuget.org）+ `.gitignore`
- 构建脚本：`build.ps1`（-Mode Prepare 门禁：Release 0 警告 + 测试 + self-contained 发布）
  + `build.bat` + `run.bat` + `run-hidden.vbs` + `run.ps1`（单实例 + 优雅关机）
- MVVM 基元：`ObservableObject` / `RelayCommand` / `AsyncRelayCommand` / `AtomicTextFile`
- 模型层：`MediaFile` / `ClipRange` / `ScreenshotEntry` / `NoteEntry` / `ProjectFile`
- 服务层：
  - `VideoPlaybackService`（LibVLC 封装：Open/Play/Pause/NextFrame/SetRate/Seek/TakeSnapshot）
  - `ScreenshotService`（TakeSnapshot → PNG，文件名含时间码）
  - `ClipExtractionService`（入出点 + ffmpeg.exe 调用，流复制默认+重编码可选）
  - `FfmpegRunner`（Process.Start 包装、stderr 重定向、可取消）
  - `HotkeyService`（达芬奇风格默认映射 + W3 设置页可自定义，KeyBinding 窗口级）
  - `AppSettingsService`（持久化输出目录、快捷键映射、最近文件，DPAPI 加密可选）
  - `ServiceRegistry`（手写 DI，构造注入）
- ViewModel 层：`ShellViewModel` / `PlayerViewModel` / `PlaylistViewModel` /
  `NotesViewModel` / `ClipListViewModel` / `ScreenshotGalleryViewModel` /
  `SettingsViewModel`
- 视图层：`MainWindow` + `TimelineControl`（自定义控件：刻度+拖动+点击跳转+
  入出点手柄+播放头）+ 侧边栏面板切换（播放列表 / 笔记）+ W3 设置窗
- 快捷键映射表（达芬奇风格）：
  | 键 | 动作 | 键 | 动作 |
  |---|---|---|---|
  | Space | 播放/暂停 | I / O | 标记入/出点 |
  | ← / → | 前/后一帧 | S | 截图 |
  | Shift+← / → | -/+1 秒 | C | 截取片段 |
  | Ctrl+← / → | -/+10 秒 | L | 循环播放 |
  | Home / End | 跳头/尾 | F | 全屏 |
  | + / - | 变速 0.25×~4× | Esc | 退出全屏 |
- 工具：`tools/download-ffmpeg.ps1`（下载 ffmpeg.exe/ffprobe.exe 到 `Tools/ffmpeg/`）
- 测试：`VideoPlaybackServiceTests` / `ScreenshotServiceTests` /
  `ClipExtractionServiceTests` / `HotkeyServiceTests` / `TimeCodeFormatterTests` /
  `AppSettingsServiceTests`（共 N 项，LibVLC 经接口 stub 测试）

#### 依赖

- `LibVLCSharp` 3.8.2 + `LibVLCSharp.WPF` 3.8.2（VideoView 控件）
- `VideoLAN.LibVLC.Windows` 3.0.21（libvlc.dll + 插件 x64）
- `Microsoft.Data.Sqlite` 10.0.12（M3+ 元数据）
- `xunit` 2.9.2 + `xunit.runner.visualstudio` 2.8.2

#### 验证

- 门禁：`build.ps1 -Mode Prepare` = Release **0 警告 0 错误** + `dotnet test` 全绿
- 手测：打开样例视频 → Space 暂停 → ←→ 逐帧 → 时间轴拖动 → S 截图 →
  I/O 标记 → C 截取片段
