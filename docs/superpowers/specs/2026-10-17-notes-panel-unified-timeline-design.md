# 笔记面板统一时间线重构 — 设计（v0.14.0 / NO.M）

日期：2026-10-17　状态：用户已确认（方案 A）

## 背景

W1-C2 笔记面板现为三子标签结构（📷截图 / ✂片段 / 📝活动，共 17 个编号项）。
用户要求改为按时间顺序排布的统一列表，混排截图、片段、裁切点，支持选中导出与筛选；
文字笔记功能（输入、模板、活动流搜索/导出）整体移除。

## 需求确认记录

1. 文字笔记功能：**移除**（输入区、模板、活动流搜索/导出删除）。
2. 片段缩略图：**ffmpeg 抽段起始帧**，缓存到数据根目录 `clips/thumbs/`，增量补抽。
3. 选中与导出：**点击选中（Ctrl/Shift 多选）+ 弹目录选择框**；截图复制 png，片段 ffmpeg 切出 mp4。
   数据记录仍先落数据根目录/会话库，导出只是复制/切出。
4. 架构：**方案 A —— 统一条目模型**（废弃活动流 ActivityEntry 底层）。

## 数据模型

新增 `Models/TimelineListItem.cs`：

- 抽象基类 `TimelineListItem : ObservableObject`
  - `TimeSpan Position`（排序键）、`TimelineItemKind Kind`（Screenshot/Segment/CutPoint）
  - `bool IsSelected`（TwoWay 绑定 ListBox）
  - `string Title`、`string TimeText`、`string Icon`（📷/🎬/✂）
- `ScreenshotListItem`：包装 `ScreenshotEntry`，绿框，名称"截图 N"（按时间编号）
- `SegmentListItem`：包装 `TimelineSegment`，黄框，名称"片段 N · 时长 mm:ss.fff"，`ThumbnailPath`（可空，异步填充）
- `CutPointListItem`：红色细条行（无缩略图），名称"裁切点 mm:ss.fff"

筛选枚举 `NotesFilter { All, Screenshots, Segments, Selected, CutPointsOnly }`：
Segments 含裁切点；CutPointsOnly 走"更多筛选"下拉，预留扩展。

## 数据流

- 截图：`Player.ScreenshotCaptured → Screenshots.Add → RebuildItems()`
- 裁切点：`Player.CutPoints.CollectionChanged → ShellViewModel → Notes.RecomputeSegments() → RebuildItems()`
- 删裁切点：列表选中红色行 → Delete 键/右键删除 → 新增 `PlayerViewModel.RemoveCutPoint(TimeSpan)` 按值删除
  → CollectionChanged → 分段重算（= 相邻片段自动合并）→ 列表重建
- 排序：Position 升序；同刻顺序 裁切点 → 片段 → 截图
- 时间轴标记：改由 Screenshots 集合直接生成（MarkerKind.Note 不再产生）

## UI 与编号

废弃（UI_NUMBERING 标记"已删除"，永不复用）：
W1-C2-T1/T2/T3、W1-C2-A-01~03、W1-C2-B-01~05、W1-C2-C-01~09（共 17 项）

新增 **W1-C2-D 统一时间线面板**：

| 编号 | 控件 | 说明 |
|---|---|---|
| W1-C2-D-01 | 筛选栏 | chip「全部/📷截图/🎬片段/✔已选中」+「更多筛选」下拉（首期含"仅裁切点"） |
| W1-C2-D-02 | 工具栏 | 🖼画廊、⬆导入裁切、⬇导出裁切、📦批量导出、📤导出选中 |
| W1-C2-D-03 | 统一列表 | [图标][缩略图 120×68][名称+时长]；截图绿框/片段黄框/裁切点红色细条行 |
| W1-C2-D-04 | 截图右键 | 打开所在文件夹 / 标注 / 删除 |
| W1-C2-D-05 | 片段右键 | 导出此段 / 跳转播放 |
| W1-C2-D-06 | 裁切点右键 | 删除（合并相邻片段） |

条目视觉：截图绿框 `#4EC9B0`、片段黄框 `#D7BA7D`、裁切点红 `#E51400`。

## 片段缩略图服务

`SegmentThumbnailService`：
- 输入：源视频路径 + 段起始毫秒；输出：数据根目录 `clips/thumbs/{startMs}.jpg`
- ffmpeg 单帧抽取（`-ss {ms} -frames:v 1`），后台 `Task.Run` 执行
- 已存在缓存直接返回；抽完回调通知对应 `SegmentListItem` 刷新 `ThumbnailPath`
- 裁切点变化重建分段时增量补抽；孤儿缓存文件定期清理（重建时按当前段集合 diff）

## 选中与导出

- ListBox `SelectionMode=Extended`，`IsSelected` 双向绑定条目
- 「📤 导出选中」→ View 弹 `Microsoft.Win32.OpenFolderDialog`
- 截图：`File.Copy` 到目标目录（保留文件名）
- 片段：走现有 ffmpeg 切段逻辑输出 mp4（与批量导出面板的单段导出同路径）
- 后台数据记录不变：截图落 `screenshots/`、裁切点落会话库、缩略图落 `clips/thumbs/`

## 移除与兼容

- 删除：笔记输入区/模板下拉/活动流搜索/时间范围/📤导出/📄整理导出（C-01~C-09）
- 删除：`ActivityEntry`、`NotesSubTab`、`NoteTemplate`、`NotesExportService` 及相关处理器
- 批量文本操作：`BatchActionKind.Marker` 指令报"已不支持"（裁切点指令不受影响）
- 保留不动：`NoteEntry`、`MediaSession.Notes`、`ProjectRepository` notes 表、.paproj 项目包格式
  （向后兼容，旧笔记数据静默忽略；会话快照 Notes 传空集合）
- `[`/`]` 跳转改为基于统一列表位置

## 测试

- 统一列表混合排序（截图/片段/裁切点交错、同刻次序）
- 四种筛选 + 仅裁切点
- 删裁切点 → 相邻片段合并
- 选中项导出清单生成（截图文件 + 片段区间）
- 缩略图缓存命名与增量补抽
- 修正受影响旧测试（NotesViewModel 活动流/笔记相关）
