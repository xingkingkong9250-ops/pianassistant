# 拉片助手（PianAssistant）

面向编导的视频拉片辅助工具：多格式播放、快速截图、片段截取、专业快捷键。

## 快速开始

### 首次启动准备

1. 安装 .NET 10 SDK（Windows x64）
2. 跑 `tools\download-ffmpeg.ps1` 下载 ffmpeg.exe / ffprobe.exe 到
   `src\PianAssistant\Tools\ffmpeg\`（首次 clone 必做，gitignored）

### 构建

```powershell
# 日常门禁：Release 0 警告 + 测试 + self-contained 发布
.\build.bat

# 直接运行（Debug）
.\run.bat
```

### 使用

- 拖视频文件到窗口或菜单「打开」
- **Space** 播放/暂停，**←/→** 逐帧，**Shift+←/→** ±1 秒，**Ctrl+←/→** ±10 秒
- **I/O** 标记入/出点，**C** 截取片段
- **S** 截图（PNG，文件名含时间码）
- **F** 全屏，**Esc** 退出全屏
- **+/-** 变速 0.25×~4×
- 侧边栏顶部切换「播放列表 / 笔记」面板

更多见 `docs\USER_GUIDE.md`。

## 工程规范

- 真源：`GEMINI.md`
- 入口：`AGENTS.md`
- 变更：`CHANGELOG.md`

## 技术栈

- C# / .NET 10 (`net10.0-windows`) / WPF，纯 MVVM
- LibVLCSharp.WPF（VLC 引擎，多格式播放 + TakeSnapshot 截图）
- ffmpeg.exe 子进程（片段截取，流复制默认+重编码可选）
- 不引入 WebView2 / Electron / 第三方 MVVM 框架
