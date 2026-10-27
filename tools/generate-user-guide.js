// 生成 PianAssistant 发布版 Word 使用说明
// 结构：封面 → 第一章 安装 → 第二章 功能全览 → 第三章 版本更新 → 第四章 卸载与反馈
// 运行：cd PianAssistant && node tools/generate-user-guide.js

const fs = require('fs');
const path = require('path');
const {
    Document, Packer, Paragraph, TextRun, HeadingLevel,
    AlignmentType, LevelFormat, BorderStyle, WidthType,
    PageNumber, Header, Footer
} = require('docx');

// ---- 工具函数 ----
const FONT_CJK = { ascii: "微软雅黑", hAnsi: "微软雅黑", eastAsia: "微软雅黑" };
const FONT_ASCII = { ascii: "微软雅黑", hAnsi: "微软雅黑", eastAsia: "微软雅黑" };

function h1(text) {
    return new Paragraph({
        heading: HeadingLevel.HEADING_1,
        spacing: { before: 480, after: 240 },
        children: [new TextRun({ text, font: FONT_CJK, bold: true, size: 36 })]
    });
}
function h2(text) {
    return new Paragraph({
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 360, after: 200 },
        children: [new TextRun({ text, font: FONT_CJK, bold: true, size: 30 })]
    });
}
function h3(text) {
    return new Paragraph({
        heading: HeadingLevel.HEADING_3,
        spacing: { before: 240, after: 120 },
        children: [new TextRun({ text, font: FONT_CJK, bold: true, size: 26 })]
    });
}
function p(text, opts = {}) {
    return new Paragraph({
        spacing: { after: 120 },
        children: [new TextRun({
            text, font: FONT_CJK, size: 24, ...opts
        })]
    });
}
function bullet(text) {
    return new Paragraph({
        numbering: { reference: "bullets", level: 0 },
        spacing: { after: 80 },
        children: [new TextRun({ text, font: FONT_CJK, size: 24 })]
    });
}
function emptyLine() {
    return new Paragraph({ children: [] });
}
function pageBreak() {
    return new Paragraph({
        children: [new TextRun({ text: "", break: 1 })]
    });
}

// ---- 内容 ----
const TITLE = "拉片助手（PianAssistant）";
const VERSION = "v1.15.5";
const DATE = "2026-10-27";

const children = [];

// ===== 封面 =====
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { before: 1200, after: 400 },
    children: [new TextRun({ text: TITLE, font: FONT_CJK, bold: true, size: 56 })]
}));
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 200 },
    children: [new TextRun({ text: "安装使用说明", font: FONT_CJK, size: 36 })]
}));
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 600 },
    children: [new TextRun({
        text: `${VERSION} · ${DATE} · Windows 10/11 x64 · 全程离线可用`,
        font: FONT_CJK, size: 24, color: "666666"
    })]
}));
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { before: 800, after: 200 },
    children: [new TextRun({
        text: "拉片助手是一款本地视频拉片辅助工具",
        font: FONT_CJK, size: 24
    })]
}));
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 200 },
    children: [new TextRun({
        text: "回放视频时快速记录时间戳、截取画面与片段",
        font: FONT_CJK, size: 24
    })]
}));
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({
        text: "把看片过程中的零散观察沉淀为结构化笔记",
        font: FONT_CJK, size: 24
    })]
}));

// ===== 第一章：安装 =====
children.push(pageBreak());
children.push(h1("第一章 安装"));

children.push(h2("1.1 方式 A：安装包（推荐）"));
children.push(bullet(`双击 PianAssistant_Setup_${VERSION}.exe，按向导完成安装。`));
children.push(bullet("安装包已内置 .NET 运行时、VLC 播放引擎与 ffmpeg，无需任何额外环境。"));
children.push(bullet("安装后可从开始菜单 / 桌面快捷方式启动。"));

children.push(h2("1.2 方式 B：绿色免安装版"));
children.push(bullet(`解压 PianAssistant_${VERSION}_win-x64_portable.zip 到任意目录。`));
children.push(bullet("双击其中的 PianAssistant.exe 即可运行，不写注册表、可直接删除。"));

children.push(h2("1.3 环境要求与数据目录"));
children.push(bullet("操作系统：Windows 10 21H2 及以上 / Windows 11，64 位（x64）。"));
children.push(bullet("首次启动自动准备数据目录（默认 %APPDATA%\\Qianlan_App\\PianAssistant），可在设置中改到自定义根目录。"));
children.push(bullet("数据目录包含：config.json（设置）、pianassistant.db（会话数据库）、screenshots\\（截图）、clips\\（片段）、logs\\（日志）。"));
children.push(bullet("同一时刻只允许运行一个实例；重复启动不会打开第二个窗口。"));

children.push(h2("1.4 数据便携与自动恢复"));
children.push(bullet("把整个数据根目录拷贝到新机器，在设置中把「数据路径」指向它，重启后即可恢复全部使用状态（项目、截图、片段、笔记、素材会话记录）。"));
children.push(bullet("打开某个视频时，应用会自动记录该素材上的裁切点、截图清单、播放位置与倍速（「素材会话」）。"));
children.push(bullet("再次打开同一素材时自动还原——即使文件被改名或换了路径/设备，只要内容一致，仍能识别为同一素材（身份识别用内容哈希 SHA-256）。"));

// ===== 第二章：功能全览 =====
children.push(pageBreak());
children.push(h1("第二章 功能全览"));

children.push(h2("2.1 界面总览"));
children.push(p("主窗口自上而下分为五个区域："));
children.push(bullet("顶部导航栏：Logo + 打开视频 + 设置 + 侧面板收起/展开。"));
children.push(bullet("左侧播放列表（可收起，默认收起）：文件列表、拖拽排序、双击播放、右键菜单。"));
children.push(bullet("中间视频画面区：支持拖入视频/字幕文件，空态点击打开视频。"));
children.push(bullet("时间轴：三条并排轴（刻度轴 / 裁切片段轴 / 截图标记轴），滚轮缩放（0.5×~50×），主刻度贯穿三轴。"));
children.push(bullet("底部控制栏：倍速、音量、字幕/音轨、播放控制（按窗口居中）、片段循环、截图、添加裁切点、标记片段、撤回、清空标记。"));
children.push(bullet("右侧侧边栏（可收起，默认收起）：拉片笔记统一时间线 + 片段批量导出面板。"));

children.push(h2("2.2 视频播放与控制"));
children.push(h3("多格式播放"));
children.push(bullet("基于 LibVLC 引擎，支持 mp4 / mkv / avi / mov / flv / wmv / webm / ts / m2ts / mpg 等常见格式。"));
children.push(bullet("倍速播放 0.25× ~ 32×（默认上限 8×，设置可调）。倍速超过 4× 时自动静音以保证画面流畅，回落 4× 及以下自动恢复声音。"));

children.push(h3("字幕与音轨"));
children.push(bullet("外挂字幕自动匹配：同目录同主名的 .srt / .ass / .ssa / .vtt / .sub 自动挂载。"));
children.push(bullet("手动加载字幕：底栏「CC」按钮 → 加载字幕文件，或直接把字幕文件拖放到视频画面区。"));
children.push(bullet("多音轨切换：底栏「♪」按钮，双语片源一键切换。"));
children.push(bullet("会话记忆：切走再切回同一素材时，字幕/音轨选择自动恢复。"));

children.push(h3("代理媒体（超 1080P 素材）"));
children.push(bullet("打开长边 > 1920 或短边 > 1080 的素材（4K / 2.7K / 1440P）时弹窗推荐生成低码率代理。"));
children.push(bullet("代理在后台转换（期间可照常播放），完成后自动切到代理播放；底栏「源文件 / 代理」可随时切换。"));
children.push(bullet("截图、片段导出、会话记录始终读取源文件，代理只影响播放流畅度。"));
children.push(bullet("生成速度可选：安静 / 均衡（默认）/ 极速；HEVC/AV1/VP9 素材自动启用 NVDEC 硬解。"));
children.push(bullet("代理缓存上限可配置（GB，默认 50），超限按最近使用时间 LRU 清理。"));

children.push(h2("2.3 标记与裁切"));
children.push(h3("裁切点与自动分段"));
children.push(bullet("按 C 键或点底栏「✂ 添加裁切点」在当前播放位置添加红色裁切点。"));
children.push(bullet("相邻裁切点之间自动形成片段，时间轴片段轴黄色高亮，笔记面板自动分段。"));
children.push(bullet("选中片段：在时间轴上点击两个裁切点之间的区域，或在笔记列表点击片段条目。"));

children.push(h3("时间轴标记拖拽修改"));
children.push(bullet("裁切点（红）与截图标记（水滴形）可按住左右拖动改位置，光标变为 ↔。"));
children.push(bullet("拖动中显示虚影与时间码气泡，播放头实时跟随预览画面。"));
children.push(bullet("裁切点不会越过相邻裁切点；松手落位，可 Ctrl+Z 撤回。"));

children.push(h3("片段标记与循环"));
children.push(bullet("按 V 键或底栏 🚩 按钮标记/取消标记当前选中片段（黄带 ⇋ 笔记 ☑ 双向同步）。"));
children.push(bullet("选中片段后入点/出点自动对齐到片段边界，勾选片段自动开启循环（🔁）。"));
children.push(bullet("取消勾选片段自动清除入出点并关闭循环。"));

children.push(h2("2.4 笔记统一时间线"));
children.push(h3("筛选与勾选"));
children.push(bullet("5 个筛选 chip：全部 / 📷 截图 / 🎬 片段 / ✂ 仅裁切点 / ⚑ 仅标记（叠加开关）。"));
children.push(bullet("勾选标记为独立字段，与行选中解耦——勾选条目可经「📤 导出标记」批量导出。"));
children.push(bullet("勾选标记支持持久化：截图、片段、裁切点三类标记随素材会话一起落库，重开视频自动恢复。"));

children.push(h3("删除与合并"));
children.push(bullet("选中一个或多个红色裁切点条目，按 Delete 键删除——上下相邻片段自动合并为一个。"));
children.push(bullet("缩略图与编号自动重算。"));

children.push(h2("2.5 截图与截段"));
children.push(h3("快速截图（X 键）"));
children.push(bullet("按 X 键，后台 ffmpeg 从源视频抽原始分辨率帧，时间轴出现水滴标记，缩略图进入笔记列表。"));
children.push(bullet("截图输出格式可选 png / jpg，分辨率可选源分辨率 / 1080P / 720P / 480P（默认 1080P）。"));
children.push(bullet("截图按视频独立子文件夹存储，不同视频彻底隔离。"));

children.push(h3("截段导出"));
children.push(bullet("选中片段后按 Ctrl+E 或点底栏「✂ 截取片段」导出。"));
children.push(bullet("视频格式可选 mp4 / mkv / mov；分辨率非「源」时自动重新编码（帧级精确）。"));
children.push(bullet("源分辨率走流复制（最快、无损，起点按关键帧对齐）。"));

children.push(h3("批量导出与拼接"));
children.push(bullet("右侧侧边栏切换到「片段批量导出」面板，可把队列中全部片段批量导出。"));
children.push(bullet("「拼接导出」把同源片段按顺序无损拼接为一个输出文件。"));

children.push(h2("2.6 批量操作"));
children.push(bullet("入口：顶部工具栏「📋」打开批量操作窗口。"));
children.push(bullet("指令格式：每行一条，支持「HH:MM:SS 截图 标签」「HH:MM:SS-HH:MM:SS 片段 标签」「HH:MM:SS（仅裁切点）」。"));
children.push(bullet("容错：支持多种时间码格式、中英别名、乱序、全角标点自动转换。"));
children.push(bullet("先预览再执行，错误行标记但不中断整批。"));

children.push(h2("2.7 导入导出"));
children.push(h3("导出数据（📋 导出数据）"));
children.push(bullet("把当前视频的裁切点、片段、截图、勾选标记导出为 .xlsx Excel 表格（默认）或 CSV 文件。"));

children.push(h3("导入数据（📥 导入数据）"));
children.push(bullet("读取 .xlsx 或 .csv 文件，覆盖当前视频的笔记数据。"));
children.push(bullet("文件缺失的截图行按时间码后台 ffmpeg 自动重新截取。"));
children.push(bullet("导入前弹出确认提示，覆盖操作可 Ctrl+Z 撤回。"));

children.push(h2("2.8 撤回与恢复"));
children.push(bullet("Ctrl+Z 或底栏「↩」按钮：裁切点/截图/勾选标记/清空/导入均可逐步撤回。"));
children.push(bullet("每次操作前自动记录快照，清空/导入等复合操作合并为一步。"));
children.push(bullet("撤回步数上限在设置「通用」页可调（默认 5，范围 1~50）。"));
children.push(bullet("切换素材时撤回历史自动清空。"));

children.push(h2("2.9 截图画廊与标注"));
children.push(bullet("入口：笔记统一时间线工具栏「🖼 画廊」。"));
children.push(bullet("网格模式：缩略图卡片浏览，双击进入大图；支持按标签搜索。"));
children.push(bullet("标注工具（✏）：选择、画笔、箭头、文字、裁剪五种工具。"));
children.push(bullet("标注副本保存为原名_annotated.png，不修改原图。"));

children.push(h2("2.10 设置面板（⚙）"));
children.push(h3("通用页"));
children.push(bullet("数据目录（唯一路径，改动需重启）、按日期分目录、项目名、最高倍率、逐帧步长、倍速步长。"));
children.push(bullet("恢复上次拉片记录（默认开）、恢复播放位置与倍速（默认开）、撤回步数上限。"));

children.push(h3("输出页"));
children.push(bullet("截图：格式（png/jpg）、分辨率（源/1080P/720P/480P）。"));
children.push(bullet("视频：格式（mp4/mkv/mov）、分辨率（源/1080P/720P/480P）。"));
children.push(bullet("代理媒体：是否提示生成、档位、缓存上限、生成速度（安静/均衡/极速）。"));

children.push(h3("快捷键页"));
children.push(bullet("全部动作以中文显示；可选中动作后按新组合键，点「应用」保存。"));
children.push(bullet("支持重置单个或全部快捷键。"));
children.push(bullet("可开启全局热键（应用失焦时按 X 仍可截图，默认关闭）。"));

children.push(h2("2.11 完整快捷键总表"));
const hotkeys = [
    ["Space", "播放 / 暂停（或点击视频画面）"],
    ["← / →", "后退 / 前进 3 秒（长按连续）"],
    [", / .", "逐帧后退 / 前进（长按连续）"],
    ["Shift+← / Shift+→", "后退 / 前进 1 秒"],
    ["Ctrl+← / Ctrl+→", "后退 / 前进 10 秒"],
    ["Home / End", "跳到开头 / 结尾"],
    ["↑ / ↓", "提高 / 降低倍速（长按连续）"],
    ["+ / -", "提高 / 降低一档预设倍速"],
    ["X", "截图"],
    ["C", "添加裁切点"],
    ["Backspace", "撤销最后一个裁切点"],
    ["Ctrl+Z", "撤回上一步笔记操作"],
    ["V", "标记 / 取消标记当前片段"],
    ["L", "片段循环开关"],
    ["I / O", "标记入点 / 出点"],
    ["Ctrl+Shift+I", "清除入/出点"],
    ["Ctrl+Shift+E", "截取入出点片段"],
    ["Ctrl+Shift+C", "清空全部标记"],
    ["M", "静音开关"],
    ["F", "进入 / 切换全屏"],
    ["Esc", "退出全屏"],
    ["[ / ]", "跳到上一个 / 下一个时间线条目"],
    ["Tab", "切换右侧边栏"]
];
for (const [k, v] of hotkeys) {
    children.push(new Paragraph({
        numbering: { reference: "bullets", level: 0 },
        spacing: { after: 60 },
        children: [
            new TextRun({ text: `${k}  `, font: FONT_CJK, bold: true, size: 24 }),
            new TextRun({ text: v, font: FONT_CJK, size: 24 })
        ]
    }));
}

// ===== 第三章：版本更新说明 =====
children.push(pageBreak());
children.push(h1("第三章 版本更新说明"));
children.push(p("以下为本版本（v1.15.5）自首个正式版 v1.0.0 以来的主要变更，按时间倒序排列。"));

const updates = [
    {
        ver: "v1.15.5", date: "2026-10-27", title: "补丁：修复标记还原时序问题",
        items: [
            "修复：重开视频后片段/裁切点/截图标记丢失。根因是会话还原时 VLC 尚未解析出时长（Length=0）导致分段未创建，标记无法套用；修复后在时长就绪重算分段后自动补齐标记。"
        ]
    },
    {
        ver: "v1.15.4", date: "2026-10-27", title: "补丁：笔记勾选标记持久化",
        items: [
            "新增：笔记面板的三类勾选标记（截图★、片段★、裁切点★）随素材会话一起落库，再次打开同一视频时自动还原。此前标记仅存在于撤回快照内存态，关闭/切视频即丢失。"
        ]
    },
    {
        ver: "v1.15.3", date: "2026-10-27", title: "补丁：倍速标记迁至时间轴右端常驻显示",
        items: [
            "优化：倍速标记移到时间轴右端区域（与当前时间/总时长/缩放值四合一），1.0× 时灰显常驻，非 1× 时蓝底白字高亮，点击恢复 1×。",
            "优化：底栏功能按钮垂直对齐，消除不同图标字体导致的高低不齐。"
        ]
    },
    {
        ver: "v1.15.2", date: "2026-10-27", title: "补丁：笔记勾选标记改为醒目的五角星 UI",
        items: [
            "优化：勾选标记由 CheckBox 改为自定义五角星样式——未选中空心☆（中灰），选中实心★（金色），字号放大到 18。深色条目下金色高对比醒目。"
        ]
    },
    {
        ver: "v1.15.1", date: "2026-10-27", title: "补丁：截图勾选/选中态同步至时间轴水滴标记",
        items: [
            "优化：笔记侧勾选截图条目时，时间轴水滴标记同步呈金色；点击笔记行时，水滴标记加白色描边高亮；两个状态可叠加。",
            "新增：TimelineMarker 变可观察对象，支持 IsSelected/IsMarked 属性增量同步。"
        ]
    },
    {
        ver: "v1.15.0", date: "2026-10-27", title: "交互细节批",
        items: [
            "时间码移至时间轴右端常驻显示，与缩放指示同区。",
            "底栏播放组按整个窗口水平居中，新增 ⏪/⏩ 快退快进 3 秒按钮，⏮/⏭ 改为播放列表上/下一个。",
            "时间轴主刻度贯穿三轴，跨轴定位更直观。",
            "底栏新增音量滚轮调节、静音图标双态 🔊/🔇。",
            "点击视频画面即可暂停/继续播放。",
            "白边加固：窗口创建瞬间接管 VLC 视频窗口填黑。"
        ]
    },
    {
        ver: "v1.14.0", date: "2026-10-27", title: "时间轴视觉改版",
        items: [
            "时间轴整体缩窄（56→46），沙漏播放头 / 水滴截图标记替代原样式。",
            "未加载视频时点击播放区直接打开视频。",
            "播放区信箱带根治为黑色（不再露白边）。"
        ]
    },
    {
        ver: "v1.13.0", date: "2026-10-27", title: "交互体验批",
        items: [
            "播放/暂停改双态图标（▶/⏸）。",
            "时间轴刻度自适应密度、滚轮以鼠标位置为锚缩放、悬停滚动条滚轮直接滚动。",
            "截图按视频独立子文件夹存储。"
        ]
    },
    {
        ver: "v1.12.0", date: "2026-10-26", title: "代理生成提速",
        items: [
            "新增「生成速度」档位：安静 / 均衡（默认）/ 极速；均衡与极速把素材等分多段并行转码再无损拼接。",
            "HEVC/AV1/VP9 素材自动启用 NVDEC 硬解（实测 4K HEVC 提速约 3.5×）。"
        ]
    },
    {
        ver: "v1.11.0", date: "2026-10-26", title: "代理媒体：大分辨率素材拉片更流畅",
        items: [
            "超过 1080P 的素材（4K/2.7K/1440P）打开时弹窗推荐生成代理，确认后后台转换。",
            "底栏「源文件 / 代理」一键切换，切换保留播放位置、倍速、音轨与字幕选择。",
            "截图、片段导出、会话记录始终读取源文件，画质与时间码不受代理影响。",
            "代理档位可选 720P / 540P / 360P，缓存上限可配置。"
        ]
    },
    {
        ver: "v1.10.0", date: "2026-10-26", title: "片段起始结束标记与裁切点挂钩",
        items: [
            "选中片段后入点/出点自动对齐到片段边界，无需手动按 I / O。",
            "勾选片段自动开启循环并对齐入出点，取消勾选清除入出点并关闭循环。"
        ]
    },
    {
        ver: "v1.9.0", date: "2026-10-26", title: "片段循环（入点/出点 A-B 循环）",
        items: [
            "按 L 或底栏 🔁 按钮开启，播放头到达出点自动跳回入点继续。"
        ]
    },
    {
        ver: "v1.8.0", date: "2026-10-26", title: "时间轴拆分为三条并排轴",
        items: [
            "时间轴拆为三轴自上而下：时间滑块轴（刻度/播放头）、裁切片段轴（裁切点/入出点/选中段/标记带）、截图标记轴（水滴标记）。各轴命中互不干扰。"
        ]
    },
    {
        ver: "v1.7.0", date: "2026-10-22", title: "时间轴标记拖拽修改",
        items: [
            "裁切点与截图标记支持鼠标选中拖拽，拖动中显示虚影与时间码气泡，松手落位，可 Ctrl+Z 撤回。"
        ]
    },
    {
        ver: "v1.6.0", date: "2026-10-22", title: "笔记操作撤回",
        items: [
            "Ctrl+Z 全局撤回：裁切点/截图/勾选标记/清空/导入均可逐步撤回；步数上限可调（1~50，默认 5）。"
        ]
    },
    {
        ver: "v1.5.0", date: "2026-10-22", title: "外挂字幕 + 字幕轨/多音轨切换",
        items: [
            "外挂字幕自动匹配（同主名 .srt/.ass/.ssa/.vtt/.sub），支持手动加载与拖放。",
            "底栏 CC / ♪ 按钮切换字幕轨与多音轨，会话内按文件记忆选择。"
        ]
    },
    {
        ver: "v1.4.0", date: "2026-10-21", title: "导出格式/分辨率可配置",
        items: [
            "视频格式 mp4/mkv/mov + 分辨率 1080P/720P/480P/源；截图分辨率 1080P/720P/480P/源。",
            "分辨率非「源」时自动重编码（帧级精确），「源」时流复制（最快无损）。"
        ]
    },
    {
        ver: "v1.3.0", date: "2026-10-20", title: "正式版品牌图标 + 深色按钮主题",
        items: [
            "统一正式版 Logo（exe 图标、任务栏、安装包、快捷方式）。",
            "深色按钮主题：悬停/按下用半透明白叠加层提亮，保证深色背景下文字可辨。"
        ]
    },
    {
        ver: "v1.0.0", date: "2026-10-18", title: "首个正式交付版本",
        items: [
            "功能基线：多格式视频播放、倍速控制（≤8×）、时间轴裁切点/截图标记、笔记统一时间线（勾选标记/筛选/导入导出 xlsx·CSV）、片段批量导出/拼接、批量指令执行、项目工程管理、播放列表、设置窗口。",
            "发布形态：全自包含（内置 .NET 10 / LibVLC / ffmpeg），换电脑免环境。",
            "新增 Inno Setup 安装包，默认免管理员安装到 %LocalAppData%\\Programs\\PianAssistant。"
        ]
    }
];

for (const u of updates) {
    children.push(new Paragraph({
        spacing: { before: 200, after: 60 },
        children: [
            new TextRun({ text: `${u.ver}  `, font: FONT_CJK, bold: true, size: 28 }),
            new TextRun({ text: u.title, font: FONT_CJK, size: 26 }),
            new TextRun({ text: `  · ${u.date}`, font: FONT_CJK, size: 22, color: "888888" })
        ]
    }));
    for (const item of u.items) {
        children.push(bullet(item));
    }
}

// ===== 第四章：卸载与反馈 =====
children.push(pageBreak());
children.push(h1("第四章 卸载与反馈"));

children.push(h2("4.1 卸载"));
children.push(bullet("安装版：通过 Windows「应用与功能」卸载。"));
children.push(bullet("绿色版：直接删除解压目录。"));
children.push(bullet("用户数据（截图、片段、会话记录、设置）位于数据目录，卸载不会自动删除，可手动清理。"));

children.push(h2("4.2 常见问题 FAQ"));
const faqs = [
    ["双击 exe 没有反应？", "应用是单实例。若已有一个实例在运行，新进程会静默退出。请检查任务管理器中是否已有 PianAssistant.exe，结束旧进程后重试；或使用仓库自带 run.bat（会自动关闭旧实例再启动）。"],
    ["视频打不开 / 画面黑屏？", "确认文件格式在支持列表内；确认文件未被其它程序独占；首次请确认已运行 tools\\download-ffmpeg.ps1（绿色版已自带 ffmpeg）。"],
    ["截图或片段导出失败？", "最常见原因是 ffmpeg.exe 缺失，请确认 tools\\ffmpeg\\ 下存在该文件。"],
    ["倍速超过 4× 后没有声音？", "正常现象。LibVLC 在高倍速播放时静音音频以保证画面流畅。倍速上限可在设置中调整（2×~32×）。"],
    ["数据保存在哪里？如何备份？", "默认 %APPDATA%\\Qianlan_App\\PianAssistant\\：config.json（设置）、pianassistant.db（数据库）、screenshots\\（截图）、clips\\（片段）。备份建议直接复制整个数据根目录。"],
    ["换电脑后怎么恢复之前的状态？", "把原机器的整个数据根目录拷贝到新机器，在设置中把「数据路径」指向它。即使素材文件路径变了或改了名，只要内容一致，再次打开时仍能自动还原拉片记录（内容哈希识别）。"],
    ["时间轴太挤 / 看不清裁切点？", "在时间轴区域滚动鼠标滚轮放大（0.5×~50×），右端显示当前缩放倍数。"],
    ["为什么输入法总被切回英文？", "应用启动时强制切换为英文模式，避免中文输入法吞掉单键快捷键（S/C/F 等）；关闭应用时会还原启动前的输入法状态。"]
];
for (const [q, a] of faqs) {
    children.push(new Paragraph({
        spacing: { before: 160, after: 40 },
        children: [new TextRun({ text: `Q：${q}`, font: FONT_CJK, bold: true, size: 24 })]
    }));
    children.push(new Paragraph({
        spacing: { after: 120 },
        children: [new TextRun({ text: `A：${a}`, font: FONT_CJK, size: 24 })]
    }));
}

children.push(emptyLine());
children.push(new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { before: 600 },
    children: [new TextRun({ text: "— 版本发布文档，以程序实际行为为准 —", font: FONT_CJK, size: 20, color: "999999" })]
}));

// ---- 组装文档 ----
const doc = new Document({
    styles: {
        default: {
            document: { run: { font: FONT_CJK, size: 24 } }
        }
    },
    numbering: {
        config: [{
            reference: "bullets",
            levels: [{
                level: 0,
                format: LevelFormat.BULLET,
                text: "•",
                alignment: AlignmentType.LEFT,
                style: { paragraph: { indent: { left: 720, hanging: 360 } } }
            }]
        }]
    },
    sections: [{
        properties: {
            page: {
                size: { width: 11906, height: 16838 },
                margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 }
            }
        },
        headers: {
            default: new Header({
                children: [new Paragraph({
                    alignment: AlignmentType.RIGHT,
                    children: [new TextRun({ text: `${TITLE} · ${VERSION}`, font: FONT_CJK, size: 18, color: "999999" })]
                })]
            })
        },
        footers: {
            default: new Footer({
                children: [new Paragraph({
                    alignment: AlignmentType.CENTER,
                    children: [
                        new TextRun({ text: "第 ", font: FONT_CJK, size: 18 }),
                        new TextRun({ children: [PageNumber.CURRENT], font: FONT_CJK, size: 18 }),
                        new TextRun({ text: " 页", font: FONT_CJK, size: 18 })
                    ]
                })]
            })
        },
        children
    }]
});

// ---- 输出 ----
const outDir = path.join(__dirname, "..", "artifacts");
if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
const outPath = path.join(outDir, "installer", "拉片助手_安装使用说明.docx");
fs.mkdirSync(path.dirname(outPath), { recursive: true });

Packer.toBuffer(doc).then(buf => {
    fs.writeFileSync(outPath, buf);
    console.log(`✅ Word 说明文档已生成: ${outPath}`);
    console.log(`   大小: ${(buf.length / 1024).toFixed(1)} KB`);
});
