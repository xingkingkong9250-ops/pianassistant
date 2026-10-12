# CHANGELOG — 拉片助手（PianAssistant）

本文件记录所有版本变更。版本号遵循三段式 `主版本.次版本.修订号`，
唯一来源为 `src/PianAssistant/PianAssistant.csproj` 的 `<Version>`。

---

## [Unreleased]

### v0.7.0 — M5 文本驱动批量操作（NO.M，2026-10-12）

#### 新增

- **批量文本解析器** `BatchTextParser`：`HH:MM:SS 截图 标签` /
  `HH:MM:SS-HH:MM:SS 片段 标签` / `HH:MM:SS 标记 标签` / 裸时间→裁切点；
  中英别名、全角冒号、MM:SS 与毫秒容错，`#`/`//` 注释，错误行不中断
- **批量执行器** `BatchExecutionService`：ffmpeg 逐条截图/截段
  （OutputDirectoryResolver 日期/项目子目录），单条失败不中断，可取消，
  进度回调（已完成/总数/单条结果）
- **批量预览窗** `BatchWindow`：粘贴/载入清单 → 解析预览 DataGrid
  （行号/类型/时间/标签/状态）→ 执行 → 结果视图（输出路径/耗时）；
  入口：顶部工具栏「📋」
- **裁切数据导入/导出** `ClipExportService`：导出裁切点 + 自动分段行（v1 格式），
  导入解析后整体替换裁切点并生成片段；片段标签页「⬆ 导入裁切 / ⬇ 导出裁切」
- **笔记整理导出** `ExportActivityReport`：Markdown 拉片报告
  （统计 + 按位置排序的时间线活动 + 分段汇总）；活动标签「📄 整理导出」
- 测试：新增 46 项（解析 22/执行 8/导入导出 8/VM 8），共 **197 项全绿**

### v0.6.1 — 倍速上限可配置（NO.P，2026-10-12）

- 预设倍速扩展至 0.25×~32×（新增 10/12/16/20/32×）
- 设置窗新增「最高倍率」选项（2×~32×，默认 8×）
- 倍速下拉框联动上限档位，当前倍速实时夹紧
- 键盘（+/−、↑↓ 长按）变速同步受上限约束

### v0.6.0 — M4 截图与片段管理（NO.M，2026-10-12）

#### 新增

- **截图画廊**：`GalleryWindow`（WrapPanel 网格 160×90 缩略图），双击放大、
  ←/→ 翻页、Esc 返回，右键打开文件夹/标注/删除；入口：截图标签「🖼 画廊」+ 顶部工具栏
- **截图标注**：`AnnotationWindow` 五模式（选择/画笔 InkCanvas/箭头/文字/裁剪）
  + 四色三粗细，RenderTargetBitmap 合成保存 `原名_annotated.png` 副本，不改原图
- **片段批量导出 UI**：侧边栏新增「📦 片段」面板（ClipsView），队列状态
  （等待/导出中 x%/完成/失败/已取消）+ 单项进度条 + 整体进度 + 取消按钮
- **片段拼接**：`ConcatService`（先流复制导出临时段 → ffmpeg concat demuxer `-c copy`），
  跨源/不完整片段抛异常；「🔗 拼接导出」按钮
- **导出目录管理**：`OrganizeByDate`（按 yyyyMMdd 子目录）+ `ProjectSubfolderName`
  （项目名层级），`OutputDirectoryResolver` 统一解析，设置窗可配
- **标签与筛选**：画廊/片段面板搜索框，Label 忽略大小写包含匹配（`#标签` 可命中），
  截图支持日期范围过滤
- 测试：新增 41 项（Concat/OutputDirectoryResolver/Gallery/ClipList/ScreenshotGallery），
  共 **150 项全绿**

### v0.5.0 — M2 笔记与标记增强（NO.M，2026-10-12）

#### 新增

- **笔记编辑/删除/搜索**：活动流新增「📝 活动」子标签，文本搜索（大小写不敏感）
  + 时间范围筛选（mm:ss 解析）；笔记右键编辑（内联 TextBox，Enter 提交/Esc 取消）与删除
- **笔记导出**：`NotesExportService`（Markdown/TXT，含时间码，截图/片段带路径），
  活动流 📤 按钮触发 SaveFileDialog
- **时间轴标记可视化**：`TimelineMarker` 模型 + TimelineControl `Markers` 依赖属性，
  顶部绘制小色块（笔记=蓝/截图=青/片段=橙），与裁切红三角不冲突
- **笔记跳转**：`[`/`]` 键跳转上/下一个活动条目（含首尾边界处理）
- **笔记模板**：6 个拉片预设（构图/灯光/节奏/转场/表演/剪辑），AddNote 自动拼前缀，
  输入区 ComboBox 切换
- **全局热键**：`GlobalHotkeyService`（Win32 RegisterHotKey，仅注册截图键 S，
  Space 因全局拦截风险不注册），设置窗开关，默认关闭
- 测试：新增 21 项（NotesViewModel/NotesExportService），共 109 项全绿

#### 修复

- 设置窗缺 DataContext 导致绑定落空

### v0.4.0 — M1 播放增强 + M3 项目持久化（NO.M，2026-10-12）

#### M1 播放增强

- **全屏模式**：F 键切换/Esc 退出，隐藏导航栏/侧边栏/时间轴/控制栏，
  鼠标静止 3 秒自动隐藏指针
- **播放列表增强**：拖拽重排序 + 右键菜单（删除/复制路径/资源管理器打开）
- **多文件连续播放**：EndReached → MoveNext → 自动播放下一曲（非循环时）
- **播放速度记忆**：每文件独立保存 Rate，Open 时自动恢复
- **字幕加载**：拖拽 .srt/.ass/.ssa/.vtt/.sub 到视频窗口，
  LibVLC `AddSlave(MediaSlaveType.Subtitle)` 实现

#### M3 项目持久化

- **SQLite 数据库**：`ProjectDatabase`（projects/screenshots/clips/notes 四表
  + schema_version 迁移 + 索引），路径 `%APPDATA%\Qianlan_App\PianAssistant\pianassistant.db`
- **ProjectRepository**：完整 CRUD + 级联删除 + 参数化 SQL
- **ProjectService**：SaveProject/LoadProject/ExportProject/ImportProject（zip 打包）
- **.paproj 项目文件**：JSON 源生成
- **模型**：ScreenshotEntry/ClipRange/NoteEntry 新增只读 Id 属性（DB 回填）
- 测试：新增 18 项 DB 测试，共 88 项全绿

### v0.3.3 — 倍速标记（NO.P，2026-10-12）

- 底部控制栏新增倍速蓝色标签（非 1.0× 时显示），`RateText`/`IsRateNormal` 属性
- 倍速播放时方向键帧步长乘以倍率

### v0.3.2 — 方向键重新设计（NO.P，2026-10-12）

- 左右键：自定义帧数后退/快进（默认 5 帧，可配 1~600），长按连续触发
  （300ms 初始延迟 + 50ms 重复间隔）
- 上下键：自定义步长变速（默认 0.25，可配 0.05~1.0）
- 视觉反馈：视频中央叠加图标+文字面板（600ms 淡出）
- AppSettings 新增 SeekStepFrames/RateStep 持久化

### v0.3.1 — 时间轴缩放 + 笔记面板拆分（NO.P，2026-10-12）

- TimelineControl：鼠标滚轮缩放（0.5~50×）+ 自适应刻度时间标签 + PR 风格白色播放头
- NotesView：📷截图标签（缩略图列表）/ ✂片段标签（裁切点自动分段+编号+时长）
- ActivityEntry/TimelineSegment 新模型

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
