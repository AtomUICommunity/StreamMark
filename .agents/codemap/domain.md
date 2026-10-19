# 领域概念

StreamMark 库没有数据库、没有网络 API、没有配置文件：库的状态都在进程内。库侧会落盘的只有图片磁盘缓存；测试截图写到 `STREAMMARK_SNAPSHOT_DIR`（或测试输出目录下的 `snapshots/`）。Gallery 示例另外把窗口设置写到应用数据目录。下表把业务名词映射到代码、界面与流程。

| 概念 | 一句话定义 | 定义位置 | 持久化 | 出现界面 | 相关流程 |
|---|---|---|---|---|---|
| 流式文档 | 只追加的源码缓冲 + 后台单工作者 + 快照发布 | `src/StreamMark.Core/Streaming/StreamingDocument.cs` › `StreamingDocument` | 无（进程内） | 所有视图的输入 | [streaming-token-to-view](flows/streaming-token-to-view.md)、[stream-completion-reparse](flows/stream-completion-reparse.md) |
| 冻结边界 | 之后再来什么文本都不会改变前文解析结果的安全切点，只前进 | `src/StreamMark.Core/Streaming/StreamSegmenter.cs` › `StreamSegmenter.FrozenEnd` | 无 | 不可见（表现为「已完成的块不再重排」） | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 快照与代 | 冻结前缀共享 + 尾部的不可变视图；`Complete()` / `Reset()` 开启新的一代 | `src/StreamMark.Core/Streaming/DocumentSnapshot.cs` › `DocumentSnapshot.Generation`、`SharesFrozenWith` | 无 | 差异应用后反映为块视图变化 | [stream-completion-reparse](flows/stream-completion-reparse.md) |
| 块 key | 块在原始源码中的起始偏移，跨重解析与代稳定 | `src/StreamMark.Core/Model/Blocks.cs` › `MarkdownBlock.Key` | 无 | 键与渲染指纹决定视图复用与块状态 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 渲染指纹 | 64 位 FNV-1a 内容指纹，代替深比较（不含 Range） | `src/StreamMark.Core/Model/Fingerprint.cs` › `Fingerprint`、`Blocks.cs` › `ComputeFingerprint` | 无 | 指纹不变则不发 Update | [stream-completion-reparse](flows/stream-completion-reparse.md) |
| 差异操作 | 两个快照之间的 Insert / Update / Remove（按 key 归并） | `src/StreamMark.Core/Streaming/SnapshotDiff.cs` › `DiffKind`、`DiffOp` | 无 | 由 `DocumentPresenter` / `TranscriptPanel` 消费 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| remend | 未完成 Markdown 的修复器；尾部完整补全，冻结片段与整篇只做转义修正 | `src/StreamMark.Core/Remend/Remend.cs` › `Remend.Complete` | 无 | 表现为「流式中的 `**粗体` 立刻有样式」 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 偏移映射 | 把转义修正后文本中的位置映射回原始源码 | `src/StreamMark.Core/Parsing/OffsetMap.cs` › `OffsetMap.Create` | 无 | 保证流式与最终解析的块 key 一致 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 文档方言 | 不可变的 Markdig 管线 + 白名单组件 + 映射器 + remend 处理器 | `src/StreamMark.Core/Parsing/MarkdownProfile.cs` › `MarkdownProfile` | 无 | 宿主用 `services.Profile` 创建文档 | [plugin-registration](flows/plugin-registration.md) |
| 白名单组件 | 可被当作组件解析的 HTML 标签及其种类（块 / 行内成对 / 自闭合） | `src/StreamMark.Core/Parsing/ComponentSpec.cs` › `ComponentSpec`、`ComponentKind` | 无 | `<details>`、`<think>`、`<Citation>`、插件自定义标签 | [plugin-registration](flows/plugin-registration.md) |
| HTML 拼接组 | 一个 HTML 块连同夹在其中的 Markdown 兄弟块合并成的块；key 取第一个 HTML 块起点 | `src/StreamMark.Core/Parsing/MarkdigMapper.cs` › `MapSiblings` | 无 | README 风格居中对齐块 | [raw-html-render](flows/raw-html-render.md) |
| HTML 盒子块 | 携带盒子样式（外边距/边框/背景/圆角/宽度）+ 可继承文字样式 + 子块 | `src/StreamMark.Core/Model/Blocks.cs` › `HtmlBoxBlock` | 无 | `HtmlBoxBlockView` | [raw-html-render](flows/raw-html-render.md) |
| 行内内容 | 一个文本片段的文字 + 三张索引表（链接 / 组件 / 文字样式），对象用 U+FFFC 占位 | `src/StreamMark.Core/Model/InlineContent.cs` › `InlineContent`、`InlineObject` | 无 | `TextSegmentView` 与图片 / 公式 / 复选框运行 | [raw-html-render](flows/raw-html-render.md) |
| 节奏控制 | 把到达速率平滑成显示速率（滑动速率 + 积压/目标落后 + MaxLag 上限 + 字素簇对齐） | `src/StreamMark.Core/Pacing/StreamPacer.cs` › `StreamPacer` | 无 | 逐字渐显的手感 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 块状态 | 每个文档一份、按 (块 key, 状态名) 保存的界面状态（折叠、换行、图表/源码、diff 模式） | `src/StreamMark.Avalonia/Plugins/BlockStateStore.cs` › `BlockStateStore` | 无（随文档重建清空） | 折叠、换行、图表/源码切换跨回收存活 | [transcript-virtualization-scroll](flows/transcript-virtualization-scroll.md) |
| 代码块共享状态 | 行分段 + 增量高亮会话 + 复制/换行等状态，供回收后重建的视图共用 | `src/StreamMark.Avalonia/Rendering/CodeBlockState.cs` › `CodeBlockState` | 无 | `CodeBlockView` | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 选区坐标 | 与视图无关的选择位置 `(消息, 顶层块 key, 片段, 偏移)` | `src/StreamMark.Avalonia/Selection/TextPoint.cs` › `TextPoint`、`SelectionModel` | 无 | 高亮、⌘C / ⌘⇧C | [text-selection-copy](flows/text-selection-copy.md) |
| 片段 | 块内可选中/可复制的单位（内容 / 源码 / 分隔符 / 前缀），编号在构建期按阅读顺序分配 | `src/StreamMark.Avalonia/Selection/BlockText.cs` › `BlockText.Segment`、`TextPoint.cs` › `SegmentKey` | 无 | 选区边界与复制拼接 | [text-selection-copy](flows/text-selection-copy.md) |
| 会话行 | 会话中一个可虚拟化的单位：Markdown 顶层块一行，宿主内容控件一行 | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs` › `Row`、`EntryState` | 无 | `TranscriptPanel` 的 Extent / 行实例化 | [transcript-virtualization-scroll](flows/transcript-virtualization-scroll.md) |
| 代码分段 | 超过 200 行的代码块按 128 行一段拆成多行；横向滚动各段独立（已知限制） | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs` › `Chunk`、`ChunkThresholdLines` | 无 | 会话中的长代码块 | [transcript-virtualization-scroll](flows/transcript-virtualization-scroll.md) |
| 延迟条目 | 先占位、解析推迟到接近视口时才做的条目 | `src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs` › `FromTextDeferred`、`EnsureParsed` | 无 | 会话历史加载 | [transcript-virtualization-scroll](flows/transcript-virtualization-scroll.md) |
| 图片租约 | 引用计数句柄：缓存淘汰不等于释放，最后一个句柄释放才 Dispose 位图 | `src/StreamMark.Avalonia/Images/ImageMemoryCache.cs` › `ImageLease` | 内存缓存（进程内） | 图片占位符与查看器 | [raw-html-render](flows/raw-html-render.md) |
| 解码档位 | 按显示宽度 × 缩放向上取整到 256 像素档，用于缓存键与内存上限 | `src/StreamMark.Avalonia/Images/ImageDecoder.cs` › 档位计算 | 无 | 图片清晰度与内存占用 | — |
| 图片同意三态 | 远程图片需要用户同意：待同意 / 已同意 / 被阻止 | `src/StreamMark.Avalonia/Images/ImageLoadResult.cs` › `ImageLoadResult` | 无 | 占位符上的操作按钮 | — |
| 插件注册表 | 不可变注册表：方言 + 服务（公式/高亮/图表）+ 组件/围栏视图 + 块操作 | `src/StreamMark.Avalonia/Plugins/PluginRegistry.cs` › `PluginRegistry` | 无 | 解析与视图创建的查找表 | [plugin-registration](flows/plugin-registration.md) |
| 样式快照 | 从 `Md.*` 资源解析出的不可变 `MarkdownStyle`（含别名解析与缺省推导） | `src/StreamMark.Avalonia/Rendering/MarkdownStyle.cs` › `MarkdownStyle` | 无（来自 Avalonia 资源） | 所有块视图的字体、颜色、间距 | [theme-resolution](flows/theme-resolution.md) |
| 资源别名 | 只保存「目标资源键」的间接层，让宿主 token 在控件自身作用域被解析 | `src/StreamMark.Avalonia/MarkdownResourceAlias.cs` › `MarkdownResourceAlias` | 无 | AtomUI 令牌下发 | [theme-resolution](flows/theme-resolution.md) |
| 作者颜色模式 | HTML 作者颜色的处理：可读（对比度不足换主题色）/ 精确 / 忽略 | `src/StreamMark.Avalonia/Extensions/MarkdownServices.cs` › `HtmlColorMode`、`Rendering/HtmlPaint.cs` | 无 | 原始 HTML 渲染 | [raw-html-render](flows/raw-html-render.md) |
| 显示列表协议 | RaTeX 原生库返回的排版结果（em 单位、Y 向下、基线 y=height，扁平条目便于前向兼容） | `src/StreamMark.RaTeX/DisplayList.cs` › `DisplayList` | 无 | 公式块 | — |
| 语言别名 | 代码围栏语言到 TextMate 语法的三级解析（别名表 → language id → 扩展名） | `src/StreamMark.Highlighting.TextMate/LanguageAliases.cs` › `LanguageAliases` | 无 | 代码块着色 | — |
 | `src/StreamMark.Avalonia/Rendering/TailFade.cs` › `TailFade` | 无 | 流式文本的尾部 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 查找 | 在块模型上按片段搜索，结果是选区坐标，内容不在屏幕上也能找到 | `src/StreamMark.Avalonia/Selection/SearchModel.cs` › `SearchModel` | 无 | 查找高亮与当前项 | [text-selection-copy](flows/text-selection-copy.md) |
| 大纲 | 从快照抽出标题，跳转目标是 `TextPoint` | `src/StreamMark.Avalonia/Selection/DocumentNavigation.cs` › `DocumentNavigation.Outline` | 无 | 大纲列表与跳转 | [text-selection-copy](flows/text-selection-copy.md) |
| 右键菜单 | 按命中位置组装复制、链接与代码块命令 | `src/StreamMark.Avalonia/Selection/MarkdownContextMenu.cs` › `MarkdownContextMenu` | 无 | 文档与会话的右键菜单 | [text-selection-copy](flows/text-selection-copy.md) |
| 差异视图 | `diff` / `patch` 围栏的统一或并排呈现；用户切换后的模式按块记在块状态里 | `src/StreamMark.Avalonia/Rendering/DiffBlockView.cs` › `DiffBlockView`、`DiffViewMode` | 无（键 `diff.mode`，在 `BlockStateStore`） | 差异围栏 | [streaming-token-to-view](flows/streaming-token-to-view.md) |
| 链接策略 | 默认只放行 https / http / mailto；相对地址与文内锚点交给宿主，文字像另一个网址时要求确认 | `src/StreamMark.Avalonia/Extensions/LinkPolicy.cs` › `LinkPolicy` | 无 | 可点击链接 | [text-selection-copy](flows/text-selection-copy.md) |
| 引用来源 | 按 id 提供标题与 URL，供 `<Citation>` 显示 | `src/StreamMark.Avalonia/Extensions/ISourceProvider.cs` › `CitationSource` | 无 | 引用标签 | [plugin-registration](flows/plugin-registration.md) |
| Mermaid 配色 | 默认复刻 mermaid.js（暗色 dark、亮色 default）；半透明宿主色先叠到底色上 | `src/StreamMark.Mermaid/MermaidTheme.cs` › `MermaidTheme` | 无 | 图表块 | — |
| 图片磁盘缓存 | 把取回的图片字节和旁路元数据写到调用方指定的目录 | `src/StreamMark.Avalonia/Images/ImageDiskCache.cs` › `ImageDiskCache` | `DirectoryPath` 下的 `.bin` 与 `.json` | 再次显示同一张远程图片 | — |
| Gallery 设置 | 示例程序的主题、布局与最近文件；冒烟测试关掉持久化 | `samples/StreamMark.Gallery/GallerySettings.cs` › `GallerySettings` | 应用数据目录 `StreamMark.Gallery/settings.json`（`Persist = false` 时不写） | Gallery 窗口 | — |
