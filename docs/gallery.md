# Gallery：Markdown 编辑器

`samples/StreamMark.Gallery` 是 StreamMark 的演示程序：一个 Avalonia 原生 Markdown 编辑器（左侧源码、右侧 StreamMark 实时预览），外加原来的 AI 会话演示。它同时是仓库唯一的 NativeAOT 冒烟测试（`--smoke`），并以预发布的形式发布到 GitHub Releases 供测试。

## 一个 Markdown 编辑器需要哪些能力

参照 Typora、Obsidian、MarkText、VS Code 的 Markdown 预览、Zettlr、iA Writer 整理。「阶段」一列是本仓库的计划：P1 = 第一个预发布，P2 / P3 = 之后。

### 文件与文档

| 能力 | 说明 | 阶段 |
|---|---|---|
| 新建 / 打开 / 保存 / 另存为 | 系统文件对话框，`.md` / `.markdown` / `.txt` | P1 |
| 未保存标记与关闭确认 | 标题栏 `•`，关闭窗口、新建、打开前询问 | P1 |
| 最近文件 | 最多 10 个，存入设置 | P1 |
| 拖放打开 | 把文件拖进窗口 | P1 |
| 命令行 / 「打开方式」 | 参数里的文件路径直接打开；macOS 包登记 `.md` 文档类型 | P1 |
| 外部修改检测 | 文件被其它程序改动时提示重新载入 | P2 |
| 自动保存与草稿恢复 | 未命名文档定时写入应用数据目录，崩溃后恢复 | P2 |
| 多标签页 | 同时打开多个文档 | P3 |

### 源码编辑

| 能力 | 说明 | 阶段 |
|---|---|---|
| 语法高亮 | AvaloniaEdit + TextMate 的 Markdown 语法，主题随亮 / 暗切换 | P1 |
| 行号、当前行高亮、软换行 | 可在「视图」里开关 | P1 |
| 撤销 / 重做 | 格式化命令合并为一步撤销 | P1 |
| 查找 / 替换 | AvaloniaEdit 搜索面板，支持正则与大小写 | P1 |
| 格式化命令 | 粗体、斜体、删除线、行内代码、链接、图片、标题 1–6、引用、无序 / 有序 / 任务列表、代码块、表格、分隔线、公式、Mermaid、折叠块、提示块；有选区时包裹，再按一次取消 | P1 |
| 列表续写 | 回车续写下一项（有序列表自动加 1、任务框保留），空项回车退出；引用里回车续写 `>` | P1 |
| 缩进 | 选中多行时 Tab / Shift+Tab 整体缩进 | P1 |
| 编辑器字号缩放 | Ctrl/⌘ + `=` / `-` / `0` | P1 |
| 成对符号补全、表格自动对齐、粘贴网址成链接 | | P2 |
| 粘贴 / 拖入图片保存到 `assets/` | | P3 |

### 预览

| 能力 | 说明 | 阶段 |
|---|---|---|
| 实时预览 | 输入后短暂防抖再刷新；StreamMark 只重建内容变化的块 | P1 |
| 三种视图 | 仅编辑 / 分栏 / 仅预览，Ctrl/⌘+`\` 轮换 | P1 |
| 滚动同步 | 编辑器滚动或移动光标时，预览跟随到对应位置 | P1 |
| 完整渲染能力 | CommonMark + GFM（表格、任务列表、脚注、删除线、自动链接、提示块）、RaTeX 公式、Mermaid、TextMate 代码高亮、diff 视图、HTML 子集与白名单组件 | P1（库已具备） |
| 链接 | 外链交给系统浏览器，可疑链接（文字像另一个网址）先确认；文内锚点与脚注在预览内跳转 | P1 |
| 相对路径图片 | 按当前文件所在目录解析 | P1 |
| 预览内搜索 | 高亮全部匹配、上一个 / 下一个 | P1 |
| 选区与复制 | 跨块选中、复制纯文本、右键「复制为 Markdown」 | P1（库已具备） |
| 表格 / 图表全屏 | | P1（库已具备） |

### 导航、外观与导出

| 能力 | 说明 | 阶段 |
|---|---|---|
| 大纲 | 标题树，点击后编辑器与预览同时定位 | P1 |
| 状态栏 | 行列、字数（中日韩按字计）、字符数、阅读时间、块数 | P1 |
| 亮 / 暗 / 跟随系统 | 编辑器、预览、Mermaid 一起切换，设置持久化 | P1 |
| 导出 HTML | 单文件 HTML，公式与图表用 KaTeX / mermaid.js 的 CDN 脚本 | P2 |
| 导出图片 | 预览整页渲染为 PNG | P2 |
| 打印 / PDF | | P3 |
| 界面多语言 | 目前只有中文 | P3 |

### StreamMark 专属演示

| 能力 | 说明 | 阶段 |
|---|---|---|
| 示例库 | 基础语法、GFM、公式、图表、代码与 diff、HTML 与组件、欢迎页 | P1 |
| 流式回放 | 把当前文档当成 AI 回答，按可调速度流式输出到预览（平滑 + 渐显 + 光标） | P1 |
| 会话演示 | 原有的虚拟化会话视图与模拟流式回答 | P1 |
| 压力测试文档 | 一键生成长文档，观察大文档下的编辑与预览 | P2 |

## 设计

```
MainWindow
├── 顶栏：页面切换（编辑器 / 会话演示）· 主题
├── EditorPage
│   ├── 工具栏：文件 · 格式 · 插入 · 示例 · 流式回放 · 视图
│   ├── 大纲（ListBox） │ AvaloniaEdit 源码编辑器 │ MarkdownView 预览（ScrollViewer + 查找条）
│   └── 状态栏
└── ChatPage：TranscriptView + 模拟流式回答
```

- **编辑器控件用 AvaloniaEdit 12.0.0 + AvaloniaEdit.TextMate**。`docs/decisions.md` 否决 AvaloniaEdit 指的是「每个代码块一个编辑器」；Gallery 只有一个源码编辑器，不受影响。实测 NativeAOT 发布 0 个 IL 警告；样式必须在 `App.axaml` 里用 `StyleInclude` 编译期引入（代码里 `new StyleInclude(uri)` 会产生 IL2026）。只有 Gallery 引用它，库的依赖不变。
- **编辑逻辑是纯函数**（`Editor/MarkdownCommands.cs`）：输入源码与选区，输出一次 `TextEdit`，编辑器在一个撤销组里应用。冒烟测试直接断言这些函数。
- **预览**：每次修改后防抖刷新 `MarkdownView`，文档用 `services.Profile` 创建，插件标签才会被解析。
- **流式回放**：新建 `MarkdownStream(profile: …)` 交给同一个预览控件，按设定速度推入当前源码；回放期间编辑器只读，结束或取消后回到实时预览。
- **设置**：`GallerySettings` 用源生成 JSON 存到应用数据目录（`StreamMark.Gallery/settings.json`）；冒烟测试关闭持久化。
- **会话演示** 保留原 Gallery 的全部内容，冒烟测试原有的 `ui.*` 检查继续在它上面跑。

## 测试与发布

- `--smoke` 新增 `editor.*`（纯函数）与 `ui.editor.*`（无头渲染编辑器页：载入示例、预览出块、修改后预览更新、大纲、流式回放完成）检查。
- 发布：`.github/workflows/release-gallery.yml`。推送 `gallery-v<版本>` 标签或手动运行（填写版本号）后，6 个桌面 RID 各自在原生 runner 上 NativeAOT 发布（要求 0 个 IL 警告）、运行 `--smoke`、打包，最后建一个 GitHub 预发布：
  - Windows：`StreamMark.Gallery-<版本>-win-<arch>.zip`
  - Linux：`StreamMark.Gallery-<版本>-linux-<arch>.tar.gz`
  - macOS：`StreamMark.Gallery-<版本>-osx-<arch>.zip`，内含 ad-hoc 签名、未公证的 `StreamMark Gallery.app`
  - `SHA256SUMS.txt`
- 本地打包：`dotnet publish samples/StreamMark.Gallery -c Release -r <rid> -o artifacts/gallery/<rid>`，把 `artifacts/native/<rid>/` 复制进去，再 `tools/package-gallery.sh <rid> <版本>`。
