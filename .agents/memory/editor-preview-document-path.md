---
name: editor-preview-document-path
description: 编辑器式预览要用 MarkdownView.Document + StreamingDocument.Load；给 Markdown 属性赋值会清空全部块视图
type: project
updated: 2026-10-19
source: session
evidence: [src/StreamMark.Avalonia/MarkdownView.cs:OnSourceChanged, src/StreamMark.Core/Streaming/SnapshotDiff.cs:Compute, samples/StreamMark.Gallery/Editor/EditorPage.cs:LoadText, samples/StreamMark.Gallery/SmokeTest.cs:ui.editor.reuse]
---
`MarkdownView.Markdown` 每次赋值都走 `OnSourceChanged`：`_presenter.Clear()` 清掉全部块视图，同时清空块状态与选区，所以逐键输入会让整篇重建。2026-10-19 把 Gallery 改成编辑器时改为：预览持有一个 `StreamingDocument` 交给 `Document`，每次修改（防抖 120ms）调用 `Load(text)`；差异按 key（源码偏移）归并，编辑点之前的块视图原样保留（冒烟检查 `ui.editor.reuse`），之后的块因 key 平移被 Remove + Insert 重建，公式（RaTeX LRU）与图表（`DiagramCache`）有内容缓存，代价可接受。

另外两点：`Document` 不变时 `BlockStateStore` 永不清空（折叠、并排 diff 等按偏移记录的状态会挂到新落在该偏移的块上），所以换文件时要换一个新的 `StreamingDocument`；`SearchModel` 的结果不随文档变化刷新，需要在新快照应用后重新 `Find`。

**Why:** 库的 API 是为「一次性文本」和「只追加的流」设计的，中间编辑没有增量接口，而最直观的 `Markdown` 属性恰好是最贵的路径。
**How to apply:** 宿主做编辑器或频繁替换全文时照 `EditorPage` 的写法；若要让编辑点之后的块也复用视图，需要在 `SnapshotDiff` 里按指纹匹配平移的块（未做，牵涉 key 即身份的不变量，见 AGENTS.md「块的身份」）。
