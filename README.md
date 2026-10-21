# 拉片助手（PianAssistant）

面向编导的视频拉片辅助工具：多格式播放、快速截图、片段截取、专业快捷键。

## 快速开始

### 普通用户

直接使用安装包 `artifacts\installer\PianAssistant_Setup_v1.6.0.exe`，双击安装即可——
**.NET 运行时、VLC、ffmpeg 已全部内置，无需任何额外环境**。

详细安装与使用步骤见 `docs\USER_GUIDE.md`（与版本同步更新）。

### 开发者

1. 安装 .NET 10 SDK（Windows x64）
2. 跑 `tools\download-ffmpeg.ps1` 下载 ffmpeg.exe / ffprobe.exe 到
   `src\PianAssistant\Tools\ffmpeg\`（首次 clone 必做，gitignored）

```powershell
# 日常门禁：Release 0 警告 + 测试 + self-contained 发布
.\build.bat

# 直接运行（Debug）
.\run.bat

# 生成安装包（需先跑门禁，再用 Inno Setup 6 编译 installer\PianAssistant.iss）
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.6.0 installer\PianAssistant.iss
```

### 使用

- 拖视频文件到窗口或菜单「打开」
- **Space** 播放/暂停，**←/→** 逐帧，**Shift+←/→** ±1 秒，**Ctrl+←/→** ±10 秒
- **I/O** 标记入/出点，**Ctrl+Shift+E** 截取片段
- **C** 添加裁切点，**Ctrl+Shift+C** 清空全部标记（裁切点 + 截图）
- **V** 标记/取消标记当前片段（播放头所在片段自动选中；黄带与笔记 ☑ 同步）
- **Ctrl+Z** 撤回上一步笔记操作（裁切点/截图/标记/清空/导入；步数上限默认 5，可在设置调整）
- **X** 截图（时间轴标记点 + ffmpeg 后台抽帧缩略图进笔记列表）
- **F** 全屏，**Esc** 退出全屏
- **+/-** 变速 0.25×~8×（>4× 自动静音以保证画面流畅）
- 侧边栏顶部切换「播放列表 / 拉片笔记」面板

更多见 `docs\USER_GUIDE.md`。

## 工程规范

- 真源：`GEMINI.md`
- 入口：`AGENTS.md`
- 变更：`CHANGELOG.md`

## 技术栈

- C# / .NET 10 (`net10.0-windows`) / WPF，纯 MVVM
- LibVLCSharp.WPF（VLC 引擎，多格式播放）
- ffmpeg.exe 子进程（片段截取 + 截图抽帧，流复制默认+重编码可选）
- 不引入 WebView2 / Electron / 第三方 MVVM 框架
