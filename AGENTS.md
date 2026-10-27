# AGENTS.md — 协作指令入口

在本仓库开始任何工作前：

1. **先读 `GEMINI.md`**。它是工程规范真源；本文件只是入口。
2. 产品基线在 `PRD/拉片助手产品需求.md`；UI 编号真源在 `docs/UI_NUMBERING.md`。

## 执行规则

- 启动应用一律使用仓库自带 `run.bat`（静默构建 + 单实例优雅重启），不要直接 `dotnet run`
- 构建/发布一律使用 `build.bat` / `build.ps1`，版本号只能来自 csproj
- 里程碑门禁：Release 0 警告 0 错误 + `dotnet test` 全绿，缺一不可
- 架构红线：纯 MVVM；Services 不引用 UI；P/Invoke 只进 Interop/；
  不引入第三方 MVVM/DI 框架；不引入 WebView2/Electron/HTML UI
- 数据红线：状态文件原子写 + `.backup`；不得把数据写到程序安装目录
- 发布产物输出到 `Releases/拉片助手/v{版本}/`，三件套（安装包 + Word 说明 + 绿色 zip）全部纳入 git 追踪；不自动 `git push`、不自动打 Tag
- 阶段性设计/计划/验证报告放 `docs/superpowers/` 对应子目录

## 版本号指令代号（与用户偏好一致）

- **NO.V** = 交付版本，叠加到最大版本号 `1.0.0`（主版本号 +1，次/修订号归零）
- **NO.M** = 功能里程碑更新，叠加 `+0.1.0`（次版本号 +1，修订号归零）
- **NO.P** = 补丁修改，叠加 `+0.0.1`（修订号 +1）
- 大版本号 1.0.0 起须**询问用户确认**后方可更新主版本号
