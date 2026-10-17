# CHANGELOG — 拉片助手（PianAssistant）

本文件记录所有版本变更。版本号遵循三段式 `主版本.次版本.修订号`，
唯一来源为 `src/PianAssistant/PianAssistant.csproj` 的 `<Version>`。

---

## [Unreleased]

### v0.14.2 — 移除笔记面板旧工具栏按钮（NO.P，2026-10-17）

> 用户要求：工具栏中旧内容（🖼 画廊 / ⬆ 导入裁切 / ⬇ 导出裁切 / 📦 批量导出）从当前界面删掉，仅作为功能保留备份，后续需要可调出复用。

#### 变更

- **旧工具栏按钮从界面移除**：W1-C2-D-02 工具栏仅保留「📤 导出选中」
- 功能与处理器完整备份：4 个按钮的 XAML 定义以注释保留在 `NotesView.xaml`，`OnOpenGallery` / `OnImportCutPoints` / `OnExportCutPoints` / `OnOpenClipsPanel` 处理器原样保留，需要时取消注释即可恢复
- 修复 `NotesView.xaml` 中「画廊」「导出选中」按钮 emoji 损坏（U+FFFD 替换字符，此前渲染为乱码方块）
- 文档同步：`UI_NUMBERING.md` W1-C2-D-02 描述更新

---

### v0.14.1 — 老数据片段显示修复 + 条目勾选按钮（NO.P，2026-10-17）

> 用户反馈：笔记列表里的片段打开老数据不显示，需点击时间轴或插入切入点才刷新；片段与截图需加勾选按钮标识选中，用于批量导出视频和截图。

#### 修复

- **打开旧数据片段不显示**：会话还原先于 VLC 解析出时长（`Length=0`），`RecomputeSegments` 因 length=0 跳过建段，片段列表为空直到下一次插入裁切点才出现。修复：`ShellViewModel` 监听 `Player.Length` 从 0 变为实际值时补算一次分段（`OnPlayerLengthChanged`）
- **侧边栏面板可见性绑定失效**（b48c494）：`NotesView`/`ClipsView` 的 `Visibility` 绑定因 `DataContext` 已切到子 VM 而静默失败，两面板同时渲染互相覆盖；改用 `RelativeSource AncestorType=Window` 绑定窗口 `DataContext.ActivePanel`
- **TimelineControl 历史坏数据容错**：`OnRender` 对还原会话中 In>Out / In 超时长的坏 I/O 点钳制宽度非负（消除渲染 `ArgumentException`）；`SetViewportWidth` 拒绝 NaN（消除 ScrollViewer 布局未完成时 6 万余次 `MeasureOverride` NaN 异常风暴）；`MeasureOverride` 全链路 `IsFinite` 兜底
- **全局异常弹窗 30 秒限频**：异常风暴时每次异常都弹模态框，嵌套消息泵导致 `TextShaping.dll` 栈溢出（0xc00000fd）直接杀死进程；改为 30 秒限频弹窗

#### 增强

- **条目勾选按钮**：截图与片段卡片右侧新增复选框（双向绑定 `IsSelected`），点勾即标为「已选中」，配合「✔ 已选中」筛选与「导出选中 / 批量导出」使用，无需再按住 Ctrl 点选；裁切点条目不提供勾选

#### 验证

- Release 编译 0 警告 0 错误；全量 310 个单元测试通过
- E2E：打开含 13 个历史裁切点的会话，未做任何交互片段即自动显示；勾选框渲染正常、toggle 交互正常且状态保留；全程无 ERROR 日志，应用优雅退出

---

### v0.14.0 — 笔记面板重构为统一时间线（NO.M，2026-10-17）

> 用户要求：笔记面板按时间顺序排布显示截图与片段；片段由剪切点切断后形成；片段之间保留可选中的裁切点标记，删除后上下片段自动合并；列表项为带缩略图的长方条（前图标、后名称与时长）；片段黄框、截图绿框、裁切点红标；选中项可导出到指定位置（后台先落库）；筛选支持 全部 / 仅截图 / 仅片段（含裁切点）/ 已选中 / 更多筛选。

#### 变更

**笔记面板整体重构（W1-C2 → W1-C2-D 统一时间线）**
- 原三子标签（📷 截图 / ✂ 片段 / 📝 活动）合并为按时间顺序混排的统一时间线列表
- 排序规则：`Position` 升序，同刻次序 裁切点 → 片段 → 截图
- 新增统一条目模型 `TimelineListItem` 基类 + `ScreenshotListItem` / `SegmentListItem` / `CutPointListItem` 三子类
- 卡片样式：截图绿框 `#4EC9B0` 📷、片段黄框 `#D7BA7D` 🎬、裁切点红色细条 `#E51400` ✂（无缩略图）
- 截图卡片显示缩略图 + 「截图 N」+ 位置时间；片段卡片显示段起始帧缩略图 + 「片段 N · 时长」+ 起始时间

**裁切点删除合并**
- 时间线列表中选中红色裁切点条目 → `Delete` 键或右键「删除（合并相邻片段）」
- `PlayerViewModel.RemoveCutPoint`（容差 100ms 按值删除）→ `CutPoints.CollectionChanged` → `NotesViewModel.RecomputeSegments` 边界重算，上下相邻片段自动合并

**片段缩略图服务**
- 新增 `SegmentThumbnailService`：ffmpeg `CaptureSingleFrame` 异步后台抽帧
- 缓存目录 `clips/thumbs/{startMs}.jpg`；启动时 `PruneOrphans` 清理孤儿缩略图
- 失败兜底返回 null，不阻塞 UI

**选中导出**
- 工具栏「📤 导出选中」+ `OpenFolderDialog` 选目录
- 截图走 `File.Copy`，片段走 `ClipExtractionService.ExtractTo`（新重载，不走日期/项目子目录组织）
- 数据先行落库（screenshots / clips 表），导出仅复制实体文件

**筛选栏**
- chip「全部 / 📷 截图 / 🎬 片段（含裁切点）/ ✔ 已选中」+「更多筛选 ▾」下拉（含「✂ 仅裁切点」）
- `NotesFilter` 枚举：All / Screenshots / Segments / Selected / CutPointsOnly

**文字笔记功能整体移除**
- 移除：活动流子标签、笔记输入框、模板下拉、活动流搜索/导出
- 移除类型：`ActivityEntry`、`NoteTemplate`、`NotesExportService`
- 批量文本「标记」指令（`BatchActionKind.Marker`）改为返回「标记指令已不支持」
- 时间轴 `Markers` 集合只来自截图（`MarkerKind.Screenshot`），不再产生 Note 标记
- 会话快照 `Notes` 字段传空数组；旧项目包中的文字笔记数据静默忽略

#### 编号变更
- 新增 W1-C2-D-01（筛选栏）/ D-02（工具栏）/ D-03（统一时间线列表）/ D-04（截图右键）/ D-05（片段右键）/ D-06（裁切点右键）
- 废弃 W1-C2-T1/T2/T3、W1-C2-A-01~03、W1-C2-B-01~05、W1-C2-C-01~09（共 20 项，永不复用）

#### 文档同步
- `docs/USER_GUIDE.md`：核心能力表、3.2 右侧面板整段重写、5.3 工作流重写、6 批量文本标记指令标不支持、其它章节入口编号更新
- `docs/REGRESSION_CHECKLIST.md`：C 区「M2 笔记统一时间线」10 项新回归；D / F / G / N 区相关条目同步
- `docs/UI_NUMBERING.md`：新增 D-01~D-06 表，20 个旧编号标「已删除」
- `docs/ui-layout-map/ui-layout-map.html`：§3 整段重写（SVG 重绘 + 表格），§1 表格同步
- `docs/superpowers/specs/2026-10-17-notes-panel-unified-timeline-design.md`：设计 spec 入库

#### 测试
- 新增 `TimelineListItemTests` / `SegmentThumbnailServiceTests`
- 重写 `NotesViewModelTests`：统一列表排序、5 种筛选、删裁切点合并、Markers、跳转、选中导出、缩略图接线
- `PlayerViewModelTests` 新增 `RemoveCutPoint` 3 个测试
- `BatchExecutionServiceTests` Marker 测试断言失败含「不支持」
- 全量 310 个单元测试通过

---

### v0.13.1 — 顶栏按钮顺序微调（NO.P，2026-10-16）

> 用户要求：W1-R1-01 后紧跟 W1-R1-16（打开视频），然后 W1-R1-23；W1-R1-24 放到最右端。

#### 变更

**顶栏按钮顺序**
- W1-R1-16「📂 打开视频」从右侧移至左侧紧跟 Logo（W1-R1-01）
- W1-R1-23「📑」移至打开视频右侧
- W1-R1-24「🗔」移至最右端；W1-R1-17「⚙」位于 24 左侧
- 对应文档同步：UI_NUMBERING / USER_GUIDE / ui-layout-map

---

### v0.13.0 — 左右侧面板可收起（默认收起）+ 时间码字号缩小（NO.M，2026-10-16）

> 用户要求：W1-R4-01 / W1-R4-02 文字大小缩小；W1-C 左右两个侧面板可以收起，默认收起，找适当位置添加展开/收起按钮。

#### 变更

**面板收起/展开**
- 顶栏新增「📑」（W1-R1-23，Logo 右侧）展开/收起左侧播放列表面板；新增「🗔」（W1-R1-24，打开视频右侧）展开/收起右侧侧边栏面板
- 两个面板**默认收起**，收起时分隔条一并隐藏，视频区自动占满
- `ShellViewModel` 新增 `IsPlaylistVisible` / `IsSidebarVisible`（默认 false）与 `TogglePlaylistVisibilityCommand` / `ToggleSidebarVisibilityCommand`
- `ContentGrid` 列重构为 3 列（Auto / * / Auto），左右面板各自包进可 `Visibility=Collapsed` 的嵌套 Grid；`PlaylistSplitter` / `SidebarSplitter` 随面板整体收起

**字号调整**
- W1-R4-01 当前时间码、W1-R4-02 总时长（含中间「 / 」分隔符）字号 13 → 11

#### 编号变更
- 新增 W1-R1-23（播放列表开关）、W1-R1-24（侧边栏开关）
- W1-R2-04 / W1-R2-07 说明补充「v0.13.0 起随面板整体收起」

---

### v0.12.0 — 打开视频批量多选 + 播放列表固定于视频左侧（NO.M，2026-10-15）

> 用户要求：打开视频支持批量选择（Windows 原生 Ctrl/Shift 多选），选中视频加入播放列表；播放列表放到主视频窗口左侧。

#### 变更

**批量打开（W1-R1-16 📂 打开视频）**
- `OpenFileDialog.Multiselect = true`，支持 Ctrl/Shift 多选视频文件
- 多选文件全部加入播放列表，自动播放第一个新加入项
- `ShellViewModel.OpenMedia(IEnumerable<string>)` 批量重载（去重、去空白）
- 视频区拖放同步支持多文件：字幕仍加载字幕，视频批量入播放列表

**布局调整（W1-R2）**
- 播放列表（W1-C1）从右侧边栏移出，固定于主视频窗口**左侧**，宽 220px（可拖 160~400px，新增分隔条 W1-R2-07 `PlaylistSplitter`）
- 右侧边栏仅保留 笔记（C2）↔ 片段导出（C3）切换
- `SidebarPanel` 枚举删除 `Playlist`；`ToggleSidebarPanelCommand`（Tab）改为 笔记 ↔ 片段导出
- 删除 `ShowPlaylistCommand`（播放列表常驻，无需切换入口）

#### 编号变更
- 新增 W1-R2-07（播放列表分隔条）；W1-R2-06 说明改为承载 C2/C3 两面板
- W1-C1 编号保留，位置变更（右侧边栏 → 视频左侧常驻）

---

### v0.11.0 — 合并截图/片段/数据路径为单一数据根目录（NO.M，2026-10-15）

> 用户要求：截屏目录、片段输出目录、数据储存目录合并，设置里只认一个路径；在指认路径下自动创建对应子文件夹存放截屏图片、输出片段、数据信息。

#### 变更

**设置界面（W2-A 输出设置页签）**
- 删除独立的截图目录（W2-A-01/02）与片段目录（W2-A-03/04）配置项——编号废弃不复用
- 仅保留唯一「数据目录」（W2-A-11/12），置于页签顶部；程序自动在其下创建 `screenshots\`、`clips\` 子文件夹

**服务层**
- `AppSettingsService.ScreenshotsDirectory`/`ClipsDirectory` 强制从 `DataRoot` 派生（不再读取独立配置）
- 删除 `SetOutputDirectories`；`AppSettings` 中旧字段保留仅为兼容旧 config 反序列化
- `SettingsViewModel` 移除截图/片段目录属性、命令与浏览回调

#### 兼容性
- 曾单独配置过截图/片段目录的用户：升级后新输出落到数据根子目录；旧文件仍在原位置，数据库记录的绝对路径可正常访问

---

### v0.10.0 — 主界面 UI 布局重构（NO.M，2026-10-14）

> 用户要求：精简顶栏、消除时间轴跳动、重排控制栏。

#### 变更

**W1-R1 顶栏精简**
- 仅保留 W1-R1-01（Logo+标题）、W1-R1-16（改名「打开视频」）、W1-R1-17（设置，移至最右端）
- 删除 W1-R1-02~04（导航标签）、06~07（画廊/批量入口）、09/12~15（裁切/导出/入出点/循环按钮）、18~22（项目按钮及右键菜单）
- W1-R1-05/08/10/11 移至 W1-R4 右端（编号保留，位置变更）

**W1-R3 时间轴固定高度**
- RowDefinition 从 `Auto` 改为固定 `50`，消除缩放时滚动条出现/消失导致的布局跳动
- TimelineControl 高度从 40 调整为 32，配合固定行高预留滚动条空间

**W1-R4 控制栏重排**
- 左侧：01 时间码 → 02 总时长 → 03 倍速标记（新增点击恢复 1×）→ 04 静音 → 05 音量滑块 → 06 音量百分比 → 08 全屏
- 中间：09 ⏮ → **14 −** → 10 ◀ → 11 ⏯（大圆钮）→ 12 ▶ → **15 +** → 13 ⏭（左右对称）
- 右侧：W1-R1-05 📷 / W1-R1-08 ✂ / W1-R1-10 ↩ / W1-R1-11 ✖
- 删除 W1-R4-07 倍速下拉框（由新增 ± 按钮替代）

#### 新增命令
- `ResetRateCommand`：倍速标记点击恢复 1×

#### 删除功能入口说明
- 播放列表/笔记/片段面板切换入口已移除（侧边栏固定为笔记面板）
- 截图画廊可从 W1-C2-A-01「🖼 画廊」打开
- 项目保存/打包/导入入口已移除（M7 已实现自动会话保存与恢复）

---

## 历史版本

### v0.9.4 — 高倍速真实生效 + 高速档自动静音（NO.P，2026-10-14）

> 用户反馈：设定最高倍率（如 8×）后并未真正加速，实际卡在 4×，且音频未屏蔽。

#### 根因

- `VideoPlaybackService.SetRate` 内遗留 `Math.Clamp(rate, 0.25, 4.0)`——早期倍速
  0.25~4× 时代的服务层硬钳制；v0.6.1 提高上限时只改了 VM 层（`CurrentMaxRate`），
  漏掉了这处，导致 8× 传到 LibVLC 前被钳成 4×。
- 音轨激活时 LibVLC 3 音频时钟也会把有效倍速钳制在约 4×（aout 限制）。

#### 修复（`VideoPlaybackService`）

- **移除 4× 遗留钳制**：服务层兜底范围放宽为 0.25~32（VM 层已按设置的最高倍率钳制）
- **高速档音轨策略 `ApplyHighRateAudioPolicy`**：倍速 > 4× 时禁用音轨
  （`AudioTrack = -1`，同时达成静音）并重新应用倍速解除音频时钟钳制；
  回落 ≤ 4× 时自动恢复禁用前的音轨；`Playing` 事件重应用（覆盖打开素材时按
  倍速记忆恢复高速、而音轨在播放开始才选中的时序）；`Open` 时重置音轨记忆
- `Rate` 属性 setter 统一收敛到 `SetRate`

#### 验证

- `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 296 项全绿
- self-contained win-x64 发布包 `artifacts/staging/PianAssistant_v0.9.4`

### v0.9.3 — 输入法强制英文模式（NO.P，2026-10-14）

> 用户反馈：中文输入法处于中文模式时单键快捷键失效（WPF 收到 `Key.ImeProcessed`
> 而非原始字母键 S/C/F 等），要求软件启动后强制切换为英文输入。

#### 新增（`Services/ImeService` + `MainWindow` 接线）

- **启动即强制英文**：`OnSourceInitialized` 时通过 `WM_IME_CONTROL → IMC_SETCONVERSIONMODE`
  （对微软拼音等 TSF 输入法兼容层有效）与 imm32 `ImmSetConversionStatus`（老式输入法兜底）
  双通道把 IME 转换状态置为字母数字（英文）模式
- **持续保障**：窗口激活时（alt-tab 切回）与键盘焦点离开文本输入控件时再次强制——
  防止用户在外部切回中文后热键失效；**文本框内不打扰**（笔记/搜索框仍可正常切换中文输入）
- **退出恢复**：首次强制前记录原转换状态，`OnClosing` 时还原系统输入法状态
- 无输入法的纯英文键盘环境所有调用均为安全空操作

#### 验证

- `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 296 项全绿
- self-contained win-x64 发布包 `artifacts/staging/PianAssistant_v0.9.3`

### v0.9.2 — 暂停态时间轴点击跳转修复（NO.P，2026-10-14）

> 用户反馈：视频暂停状态下点击时间轴，画面与播放头都不会跳到点击位置。

#### 根因

- 时间轴点击走 `SeekPercent` → LibVLC `MediaPlayer.Position` 赋值；
  方向键逐帧走 `SeekRelative` → `MediaPlayer.Time` 赋值。
  **LibVLC 3 暂停态下 `Position` 赋值不刷新画面、且 `TimeChanged` 事件不触发**
  （播放头也不动），而 `Time` 赋值可靠——这解释了逐帧正常、点击失效的差异。

#### 修复（`VideoPlaybackService`）

- `Seek` / `SeekPercent` 统一收敛到新私有入口 `SeekToMs`：改用 `Time`（毫秒，精度也优于
  float 型 `Position`）赋值
- 暂停态补两步：`NextFrame()` 强制渲染新位置画面（步进 ≈1 帧，误差可忽略）；
  手动触发 `PositionChanged` 通知 UI 播放头立即更新（有 `_isUpdatingFromPlayback`
  守卫，无回写循环）
- 播放态行为不变（仍由 `TimeChanged` 驱动）

#### 验证

- `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 296 项全绿
- self-contained win-x64 发布包 `artifacts/staging/PianAssistant_v0.9.2`

### v0.9.1 — 深色主题弹出控件撞色修复（NO.P，2026-10-14）

> 用户反馈：倍速下拉框展开后白底浅灰字，信息无法辨认。根因：App.xaml 无任何全局
> 样式，ComboBox 弹出层 / ContextMenu / TabControl / ListBox 全部使用 WPF 默认
> 浅色模板，而各窗口 Foreground 为浅灰（继承），形成"白底浅字"撞色。

#### 修复（全局深色主题，`App.xaml` 统一生效于所有窗口）

- **ComboBox 完整深色模板**：深色 ToggleButton + 弹出层 `#252526` 底 `#3F3F46` 边框
  （默认模板弹出层白底，无法仅靠颜色覆盖修复）。影响 5 处：主窗倍速框（`W1-R4-07`）、
  笔记模板框（`W1-C2-C-07`）、设置窗截图格式（`W2-A-05`）与最高倍率（`W2-A-08`）
- **ComboBoxItem**：深底浅字，悬停/选中用强调色 `#1A6B8A`
- **ContextMenu / MenuItem**：深底浅字（保留系统模板，仅覆盖颜色）——
  播放列表 / 截图 / 活动 / 画廊右键菜单与最近文件菜单
- **TabControl / TabItem**（设置窗页签）：深色化，选中项强调色下划线（与主窗导航一致）
- **ListBox**：默认白底改深色（影响设置窗快捷键列表 `W2-B-06` 等未显式设背景的列表）
- **NotesView「✏ 编辑」菜单项**：其局部 Style 会屏蔽 App 级隐式样式，补显式前景色

#### 验证

- `dotnet build -c Release` 0 警告 0 错误；`dotnet test` 296 项全绿
- self-contained win-x64 发布包 `artifacts/staging/PianAssistant_v0.9.1`

### v0.9.0 — M7 数据便携与会话恢复（NO.M，2026-10-14）

> 由用户提出的新功能：自动记录每个素材的拉片数据，再次打开同一视频时自动调取；
> 全部运行数据集中一处，更换设备后指认数据路径即可还原使用状态。

#### 新增（数据根便携化）

- **`DataRootResolver`**：唯一路径解析入口，优先级
  `PIAN_ASSISTANT_DATA_ROOT`(环境变量，测试隔离) > `config.DataRoot` > 引导目录
  `%APPDATA%\Qianlan_App\PianAssistant\`
- `config.json` 仍固定在引导位置（否则无法定位配置）；`AppSettingsService` /
  `ProjectDatabase` / `AppLog` / `OutputDirectoryResolver` 一律改为从解析器取值，
  不再各自拼路径
- 设置窗新增「数据路径」+「浏览…」（`W2-A-11`/`W2-A-12`）；保存时校验（非法字符 /
  可写探针），仅在路径**实际变化**时提示「需重启生效」，**不搬迁旧数据**

#### 新增（素材会话，内容哈希身份）

- **`ContentFingerprint` 两级键**（性能刚需，避免大文件每次全量哈希卡顿）：
  - 快速键 `路径|大小|mtime(UTC)` → 打开瞬间**立即**定位并还原
  - 规范键 SHA-256（流式 1MB 分块、`CancellationToken` 贯穿、**后台线程**）
    → 跨设备 / 改名后仍可识别；算完回写升级会话记录；同哈希冲突保留旧行不删除
- **数据库升级 `schema_version` → 2**：新增 `media_sessions`（快速键主键、哈希部分唯一索引、
  位置 / 倍速 / 入出点 / 更新时间）+ 明细表 `session_cut_points` / `session_screenshots` /
  `session_notes`；**v1 老库幂等迁移，不丢原有 projects/screenshots/clips/notes 数据**
- **`MediaSessionService`**：两级键还原、后台哈希升级、**~2s 防抖落库**、
  `Flush()` 兜底（退出 / 窗口关闭 / 全局异常兜底路径均 flush）
- **自动恢复**：打开视频静默还原裁切点 / 入出点 / 截图清单 / 笔记 / 播放位置 / 倍速，
  右下角提示条「已恢复上次拉片记录：N 处裁切 / M 张截图 / K 条笔记」（不弹窗打断）；
  空会话**不创建记录、不弹提示**
- 设置项 `RestoreSession` / `RestorePlaybackPosition`（默认开启，`W2-A-13`/`W2-A-14`）
- 提示条：`MainWindow` 右下角 `ToastBorder` + `ShowToast`（`W1-E-01`）

#### 说明

- 与 `.paproj` 手动项目**分工并存**：自动会话 = 单素材随用随存；
  `.paproj` = 跨素材 / 跨机打包
- 位置变更采用按秒节流写库，避免高频 IO

#### 验证

- `dotnet build -c Release` **0 警告 0 错误**
- `dotnet test` **296 项全绿**（新增 44 项：指纹 9 / 数据根 6 / 迁移 5 / 会话仓储 7 /
  会话服务 10 / 设置 7）
- self-contained win-x64 发布包 `artifacts/staging/PianAssistant_v0.9.0`

### v0.8.0 — M6 交付版本（开发版，NO.M，2026-10-14）

> 本轮为**非正式版本**：完成 M6 除安装程序外的全部内容，待用户测试无误后
> 再以 NO.V 发布 **1.0.0**（含安装程序）。

#### 新增（全功能整合）

- **项目文件 UI 入口**：顶部工具栏新增「💾 保存 / 📁 打开 / 📦 打包 / 📥 导入」
  （`W1-R1-18~21`），打通 `ProjectService` 的 `.paproj` 保存/加载与 zip 导出/导入
- **最近文件菜单**：`📂 打开文件` 右键菜单列出最近文件（`W1-R1-22`），空时禁用占位
- **元数据入库**：保存项目时把截图/片段/笔记写入 SQLite，加载时读回
- **设置项补全**：设置窗新增「后退/快进步长」（1~600 帧）与「倍速步长」（0.05~1.0）
  （`W2-A-09`/`W2-A-10`）
- **快捷键自定义真实生效**：`Window.InputBindings` 改为按 `HotkeyService` 运行时重建，
  设置保存后即刻生效（此前为静态 XAML，改键无效）
- **快捷键修复**：新增 `Ctrl+Shift+E`（截取片段）、`D1~D9`（跳 10%~90%）、
  `Shift+←/→`（±1s）、`Ctrl+←/→`（±10s）、`M`（静音）、`Ctrl+↑/↓`（音量）、`[`/`]`（活动跳转）；
  `HotkeyService.Defaults` 与运行时绑定完全一致（38 条，无"假快捷键"）
- **裸键绑定修复**：WPF `KeyGesture` 会静默丢弃无修饰键的裸键（`S`/`F`/`I`/`O`/`C`/`L`/`M`/`D1~D9`
  共 16 个），改用 `KeyBinding` 直接赋值 `Key`/`Modifiers` 绕过；同步修复全局热键与设置改键

#### 优化

- **缩略图按需解码 + LRU 缓存** `ThumbnailImageCache`/`ThumbnailConverter`：
  截图列表（120×68）与画廊网格（160×90）改用低分辨率解码（`DecodePixelWidth`），
  替代原先整张全分辨率 PNG 载入，显著降低内存占用；`OnLoad` 立即释放文件流
- **列表虚拟化**：截图/分段/活动列表由 `ItemsControl` 改为 `ListBox`（`Recycling`），
  播放列表与片段队列显式开启虚拟化
- **资源释放**：`GalleryViewModel` 实现 `IDisposable` 退订 `PropertyChanged`，避免长时运行事件泄漏

#### 加固（破坏性测试对应）

- **项目文件损坏**：`ProjectOperationResult` 友好中文报错，不抛 UI、不破坏原文件
- **数据库损坏**：损坏库隔离为 `*.corrupt`（保留时间戳）并重建空库，启动提示数据库状态
- **配置恢复**：`AtomicTextFile.RestoreBackup` 首次接入启动路径，崩溃残留自动恢复；
  非法 JSON 回退默认值且不覆盖用户文件
- **依赖/文件缺失**：ffmpeg 缺失给出中文提示（指向 `tools/download-ffmpeg.ps1`）；
  缺失视频源项标灰 + 提示，不崩溃
- **全局异常兜底**：`DispatcherUnhandledException` 记日志 + 提示 + `Handled`；
  `AppDomain`/`UnobservedTaskException` 仅记日志；日志落 `%APPDATA%\Qianlan_App\PianAssistant\logs\`

#### 文档

- **`docs/USER_GUIDE.md`**：12 章用户手册（简介/安装/界面总览/快捷键总表/工作流示例/
  批量文本/裁切导入导出/画廊标注/片段管理/设置详解/FAQ/目录结构）
- **`docs/UI_NUMBERING.md`**：界面编号体系（`W{窗口}-{区域}-{序号}`）+ 变更核对清单
- **`docs/REGRESSION_CHECKLIST.md`**：全功能回归（A~H）+ 破坏性测试（I~M）+ 结果记录模板

#### 验证

- `dotnet build -c Release` **0 警告 0 错误**
- `dotnet test` **252 项全绿**（新增 55 项：性能 8 / 加固 21 / 整合 26）

#### 未完成（推迟至正式版）

- 打包安装程序（MSIX / Inno Setup）
- 正式发布 1.0.0（NO.V，须用户确认）

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
