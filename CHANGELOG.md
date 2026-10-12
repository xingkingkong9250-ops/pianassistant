# CHANGELOG — 拉片助手（PianAssistant）

本文件记录所有版本变更。版本号遵循三段式 `主版本.次版本.修订号`，
唯一来源为 `src/PianAssistant/PianAssistant.csproj` 的 `<Version>`。

---

## [Unreleased]

### v0.3.0 — PR 风格时间轴裁切 + UI 重设计（NO.M，2026-10-12）

#### 新增

- **PR 风格时间轴裁切**：
  - C 键标记裁切点（原 C=ExtractClip 改为 Ctrl+Shift+E）
  - 时间轴渲染红色裁切线 + 顶部三角标记（PR 剃刀风格）
  - 点击两线之间选中段，半透明红色高亮
  - Ctrl+E 导出选中段，Backspace 撤销，Ctrl+Shift+C 清空
  - 工具栏新增裁切/撤销/清空/导出 4 个按钮
  - HotkeyAction 枚举新增 4 项，HotkeyService 默认映射更新
- **UI 重设计**：
  - 顶部导航栏：圆形 Logo + 标题 + 导航标签 + 工具按钮组 + 右侧操作
  - 视频区：无视频叠加层（大圆形播放按钮 + 提示文字）
  - 底部控制栏：时间码 + 中央播放控制（圆形蓝色播放按钮）+ 音量/倍速/全屏
  - 自定义按钮模板：圆角、hover/pressed 状态
  - 自定义滑块模板：4px 轨道 + 蓝色进度 + 圆形 Thumb
  - 配色：主背景 #0F0F0F、面板 #161616/#1A1A1A、强调色 #1A6B8A

#### 变更

- PlayerViewModel：新增 CutPoints/SelectedSegment 属性和命令
- TimelineControl：新增 CutPoints/SelectedSegment 依赖属性和渲染
- MainWindow.xaml：四行布局（导航栏+视频+时间轴+控制栏）
- Converters.cs：新增 InverseBoolToVisibilityConverter

### v0.2.0 — 音量调节 + 倍速UI + 片段截取修复（NO.M，2026-10-12）

#### 新增

- **音量调节**：工具栏音量滑块（0-100%），M 键静音，Ctrl+Up/Down 调节。
  `IVideoPlayback` 新增 `Volume`/`IsMuted` 接口，`VideoPlaybackService` 映射到
  LibVLC `MediaPlayer.Volume`/`MediaPlayer.Mute`。
- **倍速选择器**：工具栏 ComboBox（0.25×~4.0×），双向绑定 `Player.Rate`，
  与现有 + / - 快捷键同步。

#### 修复

- **片段截取死锁**：`FfmpegRunner.RunWithProgress` 同步读 stderr 可能缓冲区满死锁，
  改为 `ReadToEndAsync` 异步读取。`Run` 方法同样修复。
- **片段截取错误信息**：失败时现在包含 stderr 输出便于诊断。

### v0.1.2 — 启动器修复（NO.P，2026-10-12）

#### 修复

- **run.ps1 exe 路径错误**：`PlatformTarget=x64` 导致输出在 `bin\x64\Debug\`
  而非 `bin\Debug\`，启动器找不到 exe。修正路径为 `bin\x64\Debug\`。
- **run.ps1 EventHandle 类型不存在**：原代码 `New-Object System.Threading.EventHandle`
  使用了不存在的类型。改为 `Get-Process | Stop-Process` 直接关闭旧实例。
- **run.ps1 无控制台输出**：`Write-Log` 现在同时写文件和控制台，
  直接 `powershell -File run.ps1` 运行时可见进度。

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
