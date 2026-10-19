**StreamMark Gallery @VERSION@**（预发布，供测试）· 构建自 `@SHA@`

一个用 Avalonia + StreamMark 写成的原生 Markdown 编辑器：左侧 AvaloniaEdit 源码编辑（Markdown 语法高亮），右侧 StreamMark 实时预览；NativeAOT 发布，不依赖 WebView，不需要安装 .NET。

### 下载

| 平台 | 文件 |
|---|---|
| Windows x64 / ARM64 | `StreamMark.Gallery-@VERSION@-win-x64.zip` / `-win-arm64.zip`，解压后运行 `StreamMark.Gallery.exe` |
| macOS Apple 芯片 / Intel | `StreamMark.Gallery-@VERSION@-osx-arm64.zip` / `-osx-x64.zip`，解压得到 `StreamMark Gallery.app` |
| Linux x64 / ARM64 | `StreamMark.Gallery-@VERSION@-linux-x64.tar.gz` / `-linux-arm64.tar.gz`，解压后运行 `./StreamMark.Gallery` |

校验和见 `SHA256SUMS.txt`。

> [!NOTE]
> 测试版没有代码签名与公证。
> - **macOS**：首次打开请右键 →「打开」，或执行 `xattr -dr com.apple.quarantine "StreamMark Gallery.app"`。
> - **Windows**：SmartScreen 提示时选「更多信息 → 仍要运行」。
> - **Linux**：需要 X11 或 XWayland，以及 fontconfig。

### 可以测试的内容

- **编辑**：Markdown 语法高亮、行号、查找替换（编辑器内 Ctrl/⌘+F）、格式化工具栏与快捷键（粗体 / 斜体 / 删除线 / 代码 / 链接 / 标题…）、列表回车续写、Tab 缩进、撤销到保存点后不再提示未保存。
- **预览**：GFM（表格、任务列表、脚注、提示块）、RaTeX 公式、Mermaid 图表、代码高亮与 diff 视图、HTML 子集；编辑时只重建变化的块。
- **导航**：大纲跳转、滚动同步、预览内查找（Ctrl/⌘+Shift+F）、文内锚点、外链确认。
- **文件**：新建 / 打开 / 保存 / 另存为、最近文件、拖放打开、关闭前确认、相对路径图片。
- **视图**：仅编辑 / 分栏 / 仅预览（Ctrl/⌘+\），亮 / 暗 / 跟随系统，字号缩放（Ctrl/⌘+= / -）。
- **流式回放**：把当前文档当成 AI 回答流式输出（平滑 + 渐显 + 光标），可调速度。
- **会话演示**：虚拟化长会话、跨消息选区、模拟流式回答。
- **示例**：「示例」菜单里有每类语法的演示文档。

每个平台的构建都在发布前通过了 NativeAOT 发布（0 个 IL 警告）与 `--smoke` 无头冒烟测试。问题请提交 issue，并注明平台与版本号。
