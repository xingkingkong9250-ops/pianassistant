# PR 风格时间轴裁切功能

## Context

用户需要模仿 Adobe Premiere Pro 的时间轴裁切工作流：用 C 键在视频播放过程中标记多个裁切点，
时间轴上可视化显示这些裁切点，用户选择某一段（两个相邻裁切点之间），导出该段为独立片段。
当前 C 键绑定的是 `ExtractClipCommand`（直接导出 I/O 入出点间的片段），需要改为标记裁切点。

## 改动文件

### 1. PlayerViewModel.cs — 核心逻辑
- 新增 `ObservableCollection<TimeSpan> CutPoints` — 所有裁切点
- 新增 `TimeSpan? SelectedSegmentStart / SelectedSegmentEnd` — 用户选中的段
- **C 键行为变更**：`ExtractClipCommand` → `AddCutPointCommand`，在当前 Position 添加裁切点
- 新增 `RemoveLastCutPointCommand`（Backspace）— 撤销最后一个裁切点
- 新增 `ClearCutPointsCommand`（Ctrl+Shift+C）— 清空所有裁切点
- 新增 `ExportSelectedSegmentCommand`（Ctrl+E）— 导出选中段
- `SelectSegment(TimeSpan position)` — 根据点击位置确定所属段（找到前后两个相邻裁切点）

### 2. TimelineControl.cs — 渲染与交互
- 新增 `CutPoints` 依赖属性（`IEnumerable<TimeSpan>`），`AffectsRender` 标志
- 新增 `SelectedSegmentStart / SelectedSegmentEnd` 依赖属性
- `OnRender`：在每个裁切点位置画红色竖线（PR 风格剃刀切口）
- `OnRender`：选中段画半透明高亮背景
- `OnMouseDown`：如果点击位置靠近裁切点（±5px），选中该裁切点相邻的段；否则 seek

### 3. MainWindow.xaml — 绑定与快捷键
- InputBindings：C → `AddCutPointCommand`，Backspace → `RemoveLastCutPointCommand`，Ctrl+Shift+C → `ClearCutPointsCommand`，Ctrl+E → `ExportSelectedSegmentCommand`
- TimelineControl 绑定 `CutPoints`、`SelectedSegmentStart`、`SelectedSegmentEnd`
- 工具栏新增「裁切」按钮（C 标记）和「导出段」按钮

### 4. ShellViewModel.cs — 事件接线
- `Player.SegmentExported` → `Clips.Add(e.Range)`（复用现有 ClipListViewModel）

### 5. HotkeyService.cs — 默认映射更新
- `ExtractClip` action 改为 `AddCutPoint`，默认手势仍为 `C`
- 新增 `RemoveLastCutPoint`（Backspace）、`ClearCutPoints`（Ctrl+Shift+C）、`ExportSelectedSegment`（Ctrl+E）

## 复用
- `ClipExtractionService.Extract(ClipRange, ...)` — 直接复用，传入选中段的 start/end
- `ClipRange` 模型 — 直接复用
- `ClipListViewModel` — 直接复用 `Add` 方法

## 验证
1. Debug 构建 + 66 项测试全绿
2. 启动应用 → 打开视频 → 播放中按 C 多次 → 时间轴出现红色裁切线
3. 点击两线之间 → 段高亮 → Ctrl+E 导出 → Clips 列表新增条目
4. Backspace 撤销最后一个裁切点，Ctrl+Shift+C 清空
