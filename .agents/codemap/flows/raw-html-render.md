---
flow: raw-html-render
modules: [core-parsing-and-model, avalonia-text-rendering, core-streaming, avalonia-images, theme-and-styles]
commit: 8d344a6
updated: 2026-10-18
---
# 原始 HTML（README 风格）从源码到屏幕

**Trigger:** 文档里出现 HTML 块或行内 HTML（`<div align="center">`、`<table>`、`<font color>`、`<details>`、`<strong>`…），并且可能被空行夹在 Markdown 之间  **Outcome:** 容错解析成 HTML 树 → 映射为块模型（盒子 `HtmlBoxBlock` + 可继承文字样式 + 能复用就复用已有块）→ 盒子视图绘制外边距 / 边框 / 内边距 / 背景 / 圆角 / 宽度并按外层对齐摆放，文字样式经 `RenderContext.WithHtml` 下发；未知元素整段按源码显示、绝不执行。

> 本页按 HEAD `8d344a6` 逐条重新核对（此前写作时是 `cd7a2a8`）。相对上一版：`MarkdigMapper.RangeOf` 增加了**脚注组区间修正**（`FootnoteGroup` 不再从文档开头起算，否则 key 与第一个块重复）与**空区间容器的子块并集回退**，第 5 步与 Errors 里受影响；文本排版从"一个片段一个 `TextLayout`"改为逐行 `SegmentLayout`（`Rendering/SegmentLayout.cs:17`）；其余步骤结论未变，行号全部按当前文件重算。`docs/architecture.md:110`（"原始 HTML（阶段 3.5）"）、`docs/decisions.md:51-57`、`README.md:59-69` 三处描述的设计意图与代码一致。

## Steps
1. `src/StreamMark.Core/Parsing/MarkdownProfile.cs:MarkdownProfileBuilder.Build`（:105）— 方言装配：GFM / 数学 / 脚注等扩展，外加 `HtmlComponentExtension`（:122）；后者把 `HtmlComponentBlockParser` **插在** `HtmlBlockParser` 之前、把 `HtmlComponentInlineParser` 插在 `AutolinkInlineParser` 之前（`src/StreamMark.Core/Parsing/HtmlComponentExtension.cs:HtmlComponentExtension.Setup`，:22、:26）。因此行首的白名单标签（`details` / `summary` / `think`）走组件路径，**不**进入 HTML 块拼接 (`core-parsing-and-model`)
2. `src/StreamMark.Core/Streaming/DocumentComposer.cs:DocumentComposer.Parse`（:111）— 冻结片段、流式尾部、流结束整篇三条路径共用同一个入口：`RemendHealer.Complete` → `Markdown.Parse(healed, _profile.Pipeline)`（:114）→ `MarkdigMapper.MapDocument(document, _profile, OffsetMap.Create(...))`（:115）。**Markdig 产出的 `HtmlBlock` 就是本流程的起点** (`core-streaming`)
3. `src/StreamMark.Core/Parsing/MarkdigMapper.cs:MarkdigMapper.MapDocument`（:37）— 建 `MapContext`（:39）后直接进 `MapSiblings`（:40）(`core-parsing-and-model`)
4. `src/StreamMark.Core/Parsing/MarkdigMapper.cs:MarkdigMapper.MapSiblings`（:47）— **拼接规则**：遇到 `MdHtmlBlock` 就开 / 续一个 `HtmlGroup`，把该块原文交给 `group.AddHtml`（:56；`Tree.Feed(source)` :91 + 原样映射的 `HtmlBlock` 进 fallback :92）；`group.Tree.HasOpenElements` 为假时才 `Finish`（:57-61）。夹在其中的 Markdown 兄弟块被 `group.AddMarkdown` 收进同一个组（:67；`Tree.AddMarkdown` :102 + fallback :103），组未结束时它们**不**单独进结果（:64-73）。`HtmlGroup.Extend` 取所有成员 `SourceRange` 的并集（:106-116），`Finish` 再把这个区间写给合并后的块（:137、:141），所以块的 `Key`（= `Range.Start`，`src/StreamMark.Core/Model/Blocks.cs:MarkdownBlock.Key`，:18）就是**第一个 HTML 块的起点**，在流式与整篇解析之间稳定 (`core-parsing-and-model`)
5. `src/StreamMark.Core/Parsing/MarkdigMapper.cs:HtmlGroup.Finish`（:118）— 四个出口：① `Tree.HasUnknownElements` → 整个组退回 fallback（:120-124，=`HtmlBlock` 原文 + 夹在其中的 Markdown 块，谁都按源码显示）；② 树里没有元素且映射结果全是段落（只有注释或零散文字）→ 丢掉**纯注释**的 `HtmlBlock`（`IsCommentOnly`，:146-159），其余按源码显示（:126-131）；③ 恰好一个顶层块 → `blocks[0] with { Range = _range }`（:136-138）；④ 多个顶层元素 → `new HtmlBoxBlock("#fragment", Default, Default, blocks)`（:139-142）。同一条路径在 `HtmlMapper` 里被重用于 `MapChildren` (`core-parsing-and-model`)
6. `src/StreamMark.Core/Html/HtmlTree.cs:HtmlTreeBuilder.Feed`（:89）— 容错扫描（单遍、逐字符）：`<!-- -->` 注释整段丢弃（未闭合就吃到末尾，:102-109）；`<!` / `<?` 声明吃到 `>`（:110-117）；`HtmlTagReader.TryRead` 成功的标签按开 / 闭 / 自闭合分派（:118-142）；`IsRawText` 的 `script` / `style` / `textarea` / `title` 把内容整体当文本节点直到对应结束标签（:132-140、:157-159）；**写了一半的标签**（`<` 后无 `>`）丢掉已扫过的文本、等下一轮输入（:144-150）(`core-parsing-and-model`)
7. `src/StreamMark.Core/Html/HtmlTree.cs:HtmlElements`（:9）— 未知元素判定的**单一来源**：`Block`（div p center section… h1-h6 table thead tbody tfoot tr th td ul ol li dl dt dd blockquote pre hr details summary，:12）、`Inline`（a img picture br strong b em font span …，:19）、`Void`（br img hr source col …，:25）、`Containers`（可跨空行包住 Markdown 的容器，:29）。`Open` 里 `HtmlElements.IsKnown(name)`（:32）为假就置 `HasUnknownElements`（:178-193），不配对的结束标签落在 `Close` 的兜底分支里同样置位（:244-259）(`core-parsing-and-model`)
8. `src/StreamMark.Core/Html/HtmlTree.cs:HtmlTreeBuilder.Open`（:178）/`ImplicitClose`（:196）/`CloseUntil`（:223）— 隐式闭合：`li` / `dt` / `dd` / `tr` / `td` / `th` / `thead|tbody|tfoot` 按各自边界关闭前一个同类（:198-220），块级元素结束开着的 `p`（:216-219）；`CloseUntil` 从栈顶往下找最外层目标元素再截断（:223-242）(`core-parsing-and-model`)
9. `src/StreamMark.Core/Html/HtmlMapper.cs:HtmlMapper.MapChildren`（:14）/`Blocks`（:16）— 树 → 块序列。`HtmlMarkdown` 节点直接放回块（拼接进来的 Markdown 兄弟块原样复用）（:32-35）；块级元素（或内部含 Markdown 的行内元素，`ContainsMarkdown`，:53-54）走 `Block`（:36-43）；其余节点攒进一个 `HtmlInlineBuilder`，遇到块边界才 `Flush` 成 `ParagraphBlock`（:17-22、:45-49）(`core-parsing-and-model`)
10. `src/StreamMark.Core/Html/HtmlMapper.cs:HtmlMapper.Block`（:60）— **复用已有块**：`h1-h6` → `HeadingBlock`、`ul|ol` → `ListBlock`（`List`，:89）、`blockquote` → `QuoteBlock`、`pre` → `CodeBlock`（`Code` 取 `<code class="language-*">`、去首尾换行，:115）、`hr` → `ThematicBreakBlock`、`table` → `TableBlock`（`Table` 汇总 `thead/tbody/tfoot/tr`，缺 `<tr>` 的单元格自成一行，:171）、`details` → `ComponentBlock("details")` 并把 `<summary>` 折成 `"summary"` 子块（`Details`，:222）、`dl` → `DefinitionListBlock`（`Definitions`，:241）。带非默认盒子 / 文字样式的复用块会被 `HtmlBoxBlock(element.Name, box, text, [inner])` 包一层（:81-84）；其余一切（div / p / center / section / figure / article…）就是 `HtmlBoxBlock(element.Name, box, text, children)`（:86）。`hidden` 属性或 `display:none` 的元素整块隐藏（`IsHidden`，:56-58）(`core-parsing-and-model`)
11. `src/StreamMark.Core/Html/HtmlMapper.cs:HtmlInlineBuilder`（:286）— 行内内容：空白按 HTML 规则折叠（`AppendCollapsed` 用 `_pendingSpace` / `_pendingSpaceHadNewline`，:469-502），**换行两侧都是 CJK 时不插入空格**（`IsWide`，:488、:506）；`StyleFor` 把 `strong/b`…`mark/sup/sub` 折成 `InlineStyle`（:396-411）；`a[href]` 建 `LinkTarget`（:349-354）；`br` 换行、`wbr/source/col` 忽略、`img` / `picture` → `AddImage`（把 `width/height` 从 style 规范化成属性，:426-456）；`small` / `big` 补默认缩放（:356-364）
12. `src/StreamMark.Core/Html/HtmlMapper.cs:HtmlInlineBuilder.VisitElement`（:321）— 行内 CSS 的落点：`var (_, own) = CssStyle.FromAttributes(...)`（:355），`own with { Align = Inherit }`（:364）后 **`own.Over(current)` 叠加到外层并追加进 `_textStyles`**（:365-370），再用 `textStyle = _textStyles.Count - 1` 记进当前区间；块级元素在行内上下文里前后 `LineBreak()`（表格单元格里的 `<p>` / `<div>` 被压平成换行，:371-392）。`Build`（:554）去掉末尾换行空格、截断越界区间 (`core-parsing-and-model`)
13. `src/StreamMark.Core/Html/CssStyle.cs:CssStyle.FromAttributes`（:20）— 表现属性优先于 `style`：`align`（`center` 标签本身等价于居中，:25-30）、`bgcolor`（:33）、`width`（`img` 的 width/height 不算盒子宽度，:37）、`<font color size>`（`FontTagScale` 把 size 1-7 / ±n 映射成比例，:282）；随后 `style` 走 `Apply`（:54 → :62）(`core-parsing-and-model`)
14. `src/StreamMark.Core/Html/CssStyle.cs:CssStyle.Apply`（:62）— **`style` 子集与夹紧**：`text-align` / `color` / `background(-color)`（同时写 box 与 text）/ `font-size`（关键词、%、em、rem、px，`ApplyFontSize` :245）/ `font-weight`（`bold|bolder` 或 ≥600）/ `font-style` / `margin*` / `padding*`（简写 `ParseEdges`）/ `border` `border-color` `border-width`（宽度 `Clamp(0,8)`）/ `border-radius`（`Clamp(0,64)`）/ `width` / `max-width`；未知声明直接忽略（:77-202）。长度上限：`Spacing` 走 `MaxSpacing = 200`（:12）且下限 `-MaxSpacing/4`（:312-313），字号 `8…64`（:13-14、:278），宽度 `4096`、百分比 `min(…,100)`（:303-310），图片尺寸 `min(…,4096)`（:207-233）；颜色支持 `#rgb/#rgba/#rrggbb/#rrggbbaa`、无 `#` 的 3/6 位旧写法、`rgb()/rgba()`、`transparent` 与 60 多个命名色（:382-436、:476）(`core-parsing-and-model`)
15. `src/StreamMark.Core/Parsing/MarkdigMapper.cs:InlineBuilder.VisitHtml`（:519）— **Markdown 段落中的行内 HTML**：`HtmlTagReader.TryRead` 成功且名字在 `HtmlElements.Inline` 里才处理，否则原文照抄（注释直接丢，:521-529）；维护 `_html`（开放标签栈，:516）。开始标签压栈时 `frameStyle = outer.Style | HtmlInlineBuilder.StyleFor(name)`（:556）、`<a href>` 补链接栈帧（:558-562）、`CssStyle.FromAttributes` 得到 `own`（`Align` 强制 `Inherit`）后 `own.Over(outer.Current)` 追加进 `_textStyles` 并把索引存进栈帧（:563-573）；结束标签把栈从匹配处整段弹出（:531-538）。`Append`（:627）时取栈顶的样式 / 链接 / 文字样式（:634-643）。所以 `<strong>` / `<span style>` 能跨软换行、跨 Markdown 强调继续生效 (`core-parsing-and-model`)
16. `src/StreamMark.Core/Streaming/StreamSegmenter.cs:StreamSegmenter.TryOpenHtmlBlock`（:382）— **HTML 容器标签同时进切分器**：注释 / `<?` / `<![CDATA[` / 声明 / 白名单容器（`_containerTags`，`HtmlKind.Container`，记录同名嵌套深度）/ 原始文本标签（`script pre style textarea`，`HtmlKind.Raw`，:438）都让当前区域成为不透明块（:382-436）；`EndsHtmlBlock`（:440）→ `ContainerEnds`（:452）逐字符配平同名开 / 闭标签，深度回到 0 以下才结束。不透明期间 `_html != HtmlKind.None` 使 `InOpaqueBlock`（:128）为真、不提交冻结边界，所以**未闭合的 HTML 容器不会被冻结**，每个快照都重新解析尾部 (`core-streaming`)
17. `src/StreamMark.Core/Parsing/MarkdownProfile.cs:MarkdownProfile.SegmenterContainers`（:52-55）— 容器表的内容：块级组件名 ∪ `Html.HtmlElements.Containers`（:54），由 `DocumentComposer` 构造 `StreamSegmenter` 时传入（`src/StreamMark.Core/Streaming/DocumentComposer.cs:47`）
18. ⇢ crosses thread boundary (后台 → 界面) via `Volatile.Write(ref _latest)` + `SnapshotReady`（在**后台线程**触发，`src/StreamMark.Core/Streaming/StreamingDocument.cs:149-150`、`Run` :213）→ `MarkdownView.OnSnapshotReady`（`MarkdownView.cs:140`）只 `Dispatcher.UIThread.Post(RequestFrame, DispatcherPriority.Render)`，`ApplyLatest`（:161）取 `document.Latest` 交给 `DocumentPresenter.Apply`（:169）
19. `src/StreamMark.Avalonia/Rendering/BlockViewFactory.cs:BlockViewFactory.ViewTypeFor`（:91）/`CanReuseBuiltIn`（:56）— `HtmlBoxBlock` → `HtmlBoxBlockView`（:35、:69、:105），`HtmlBlock`（未知元素按源码显示的那个）→ `TextBlockView`（:93）(`avalonia-text-rendering`)
20. `src/StreamMark.Avalonia/Rendering/BlockView.cs:TextBlockView.OnUpdate`（:170）— `HtmlBlock` 分支把它变成一段等宽、次要色的纯文本（`new InlineContent(html.Html, [new InlineSpan(0, html.Html.Length, InlineStyle.Code)], [], [])`，foreground 用 `style.SecondaryForeground`，:187-190）。这就是"未知元素整段按源码显示"的落点；**没有任何地方解释或执行这段文本** (`avalonia-text-rendering`)
21. `src/StreamMark.Avalonia/Rendering/HtmlBoxBlockView.cs:HtmlBoxBlockView.OnUpdate`（:23）— 只在本盒子的文字样式或背景变化（或外层上下文换了）时新建子上下文：`box.Text.IsDefault && box.Box.Background is null ? context : context.WithHtml(box.Text, box.Box.Background)`，并把 `_childContext` / `_childParent` / `_childFor` 缓存起来（:17-31）→ `UpdateChildren`（:33），避免每次更新都让子视图重新排版。注意 `HtmlMapper.Block` 已经把 `text.Background` 清成 null（:68，背景归盒子），所以这里的 `box.Box.Background` 才是下发给行内颜色可读性判断的背景 (`avalonia-text-rendering`)
22. `src/StreamMark.Avalonia/Rendering/HtmlBoxBlockView.cs:HtmlBoxBlockView.MeasureOverride`（:38）— 盒子几何：`outer = available − margin.L/R`（:45），宽度依次被 `Width`（px，:47-50）/ `WidthPercent`（外层宽度百分比，:51-54）/ `MaxWidth`（:55-58）收紧；**窄于可用宽度的盒子按外层对齐摆放**：`offset = Context.Inherited.Align` 为 `Center` → `(outer−width)/2`、`Right` → `outer−width`（:60-70）；算出的 `_content = Thickness(left+border+padding.L, margin.T+border+padding.T, right+border+padding.R, margin.B+border+padding.B)` 就是 `StackBlockView.ContentPadding`（:21、:73），`_boxRect` 记下画背景 / 边框的矩形（:76）(`avalonia-text-rendering`)
23. `src/StreamMark.Avalonia/Rendering/HtmlBoxBlockView.cs:HtmlBoxBlockView.Render`（:80）— 背景与边框：`HtmlPaint.Background` 得到画刷、`style.BorderColor is not null && BorderWidth > 0` 才建 `Pen`，`DrawRectangle` 带 `BorderRadius`（笔宽非零时把矩形内缩半个笔宽）（:87-96）；两者都没有就什么都不画 (`avalonia-text-rendering`)
24. `src/StreamMark.Avalonia/Rendering/RenderContext.cs:RenderContext.WithHtml`（:60）— 继承通道：`Inherited = text.Over(Inherited ?? HtmlTextStyle.Default)`（:63），`HtmlBackground = background.A > 0 ? background : HtmlBackground`（:64）；`With`（:52-57）会原样带走这两个值。`HtmlTextStyle.Over` 的叠加语义是 `TextSegmentView.PropertiesFor` 与 `Set` 都依赖的（`src/StreamMark.Core/Model/HtmlStyles.cs:HtmlTextStyle.Over`，:82-89：非空项优先，`FontSize` 与 `FontScale` 互斥、`FontScale` 相对外层字号累乘）(`avalonia-text-rendering`)
25. `src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:TextSegmentView.Set`（:129）— 应用继承的文字样式：`alignment` 仍是默认 `Left` 而 `Inherited.Align != Inherit` 时改成 Center / Right / Justify（:139-148）；`Inherited.FontSize` 按 `px / context.Style.FontSize` **比例缩放**当前字号（标题因此保持相对大小），否则按 `FontScale` 乘（:150-157）；`Bold` 为真且当前字重更轻时提升到 `FontWeight.Bold`（:158-161）；前景色缺省时 `HtmlPaint.Foreground(context, inherited.Foreground)`（:162）(`avalonia-text-rendering`)
26. `src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:TextSegmentView.InlineTextSource.PropertiesFor`（:1212）— 行内 CSS 真正生效的地方：`textStyle` 索引取 `_content.TextStyles[i]`（:1220），`Bold` / `Italic` / `FontSize`（再 `Clamp` 到 `CssStyle.Min/MaxFontSize`，:1227-1230）/ `FontScale`（:1231-1234）决定字重、字形与字号，`html.Foreground` / `html.Background` 经 `HtmlPaint.Foreground(...)`（用同一段文字自己的 `html.Background` 作为对比背景，:1276）与 `HtmlPaint.Background` 覆盖前景 / 背景（:1273-1278）；属性按 `(style, component, textStyle)` 缓存（:1214、:1300）(`avalonia-text-rendering`)
27. `src/StreamMark.Avalonia/Rendering/HtmlPaint.cs:HtmlPaint.Foreground`（:27）— **作者颜色**：`color` 为空或 `MarkdownServices.HtmlColors == Ignore` → 返回 null（= 用主题颜色）；`Readable`（默认，`src/StreamMark.Avalonia/Extensions/MarkdownServices.cs:34`）下取 `background ?? context.HtmlBackground ?? ThemeBackground(context)` 算 WCAG 对比度，`< 2.2`（:36）就返回 null；`Exact` 直接返回画刷。`ThemeBackground` 在**没有 HTML 背景**时按主题文字色亮度反推（浅色文字 → 视为暗背景 `#1E1E1E`，否则白色，:48-55）。`Background` 只放行 `A > 0` 且模式不为 `Ignore`（:44-45）。画刷按 `CssColor` 缓存（:11-24）(`theme-and-styles`)
28. `src/StreamMark.Avalonia/Rendering/TableBlockView.cs:TableBlockView.ArrangeOverride`（:163）— 表格的"外层居中时整体居中"：`spare = Math.Max(0, finalSize.Width − _contentWidth − 1)`（:165），`_shift = Context?.Inherited?.Align` 为 `Center` / `Right` 时分别 `floor(spare/2)` / `spare`（:166-171）；复制 / 全屏按钮排在网格上方的操作条（`ActionBarHeight`，单元格从这里起排），不盖住表头 (`avalonia-text-rendering`)
29. `src/StreamMark.Core/Html/HtmlMapper`→`HtmlInlineBuilder.AddImage`（:426）产生的图片对象，由 `TextSegmentView.SlotFor`（`TextSegmentView.cs:457`）→ `ImageSlot.Ensure` → `ImagePipeline` 异步加载 (`avalonia-images`, 见下条)
30. ⇢ crosses thread boundary (I/O → 界面) via `ImagePipeline.LoadAsync`（后台 `Task.Run` + `ConfigureAwait(false)`）→ `Dispatcher.UIThread.InvokeAsync` → 回调 `TextSegmentView.OnResourceLoaded`（整体 `Invalidate` 整形缓存并重新排版）。契约细节见 `flows/streaming-token-to-view.md` 第 27 步；本次只确认 HTML 图片走的是同一条路（`src/StreamMark.Avalonia/Rendering/TextSegmentView.cs:TextSegmentView.SlotFor`，(unverified：`ImageSlot` / `ImagePipeline` 内部未逐行复核)）(`avalonia-images`)

## Sequence
```mermaid
sequenceDiagram
  participant Md as Markdig
  participant MM as MarkdigMapper
  participant Tree as HtmlTreeBuilder
  participant Map as HtmlMapper / CssStyle
  participant Seg as StreamSegmenter
  participant Fac as BlockViewFactory
  participant Box as HtmlBoxBlockView
  participant Seg2 as TextSegmentView
  Md->>MM: MapDocument - DocumentComposer.Parse
  MM->>MM: MapSiblings 开放元素栈合并 HTML 块与 Markdown 兄弟块
  MM->>Tree: Feed(html 原文) / AddMarkdown(块)
  Tree->>Tree: 隐式闭合 / 原始文本 / 半截标签丢弃 / 未知元素置位
  Tree-->>MM: HasOpenElements HasUnknownElements
  MM->>Map: Finish - MapChildren
  Map->>Map: 复用已有块 / HtmlBoxBlock / HtmlInlineBuilder
  Map-->>MM: 块序列（Key = 首个 HTML 块起点）
  Seg-->>MM: 容器未闭合 则不冻结 尾部重解析
  MM-->>Fac: DocumentSnapshot 后台线程 SnapshotReady
  Fac->>Box: HtmlBoxBlockView.Update
  Box->>Box: WithHtml 下发继承样式与背景
  Box->>Seg2: TextSegmentView.Set + PropertiesFor
  Seg2->>Seg2: HtmlPaint 处理作者颜色
```

## Errors and cancellation
- **写了一半的 HTML（流式主路径）**：尾部每次重解析，半截标签有三道防线。① remend 的 `HtmlTagHandler.HandleIncompleteHtmlTag` 只把**行尾**（且不在代码 / 公式内）的 `<x…` 去掉（`src/StreamMark.Core/Remend/HtmlTagHandler.cs:HtmlTagHandler.HandleIncompleteHtmlTag`，`RemendOptions.HtmlTags` 默认 true，`src/StreamMark.Core/Remend/Remend.cs:RemendOptions`）；`DocumentComposer.SettledOptions`（:21）把 `HtmlTags` 关掉，所以这条只作用于尾部。② `HtmlTreeBuilder.Feed` 自己遇到"`<` 之后没有 `>`"时丢弃该候选、等下一轮输入（`src/StreamMark.Core/Html/HtmlTree.cs:HtmlTreeBuilder.Feed`，:144-150）。③ 若 `HtmlGroup` 到兄弟块结束仍然 `HasOpenElements`，`group?.Finish(builder)` 照样把已解析出的树映射成块（`src/StreamMark.Core/Parsing/MarkdigMapper.cs:MarkdigMapper.MapSiblings`，:74），未闭合容器只是本块内还没闭合而已。回归测试：`tests/StreamMark.Core.Tests/Html/HtmlTests.cs:202`（`Streaming_PartialHtml_NeverShowsRawTags`）对每个前缀断言"渲染文本里不出现 `<`"。
- **未闭合的 HTML 容器不冻结**：`StreamSegmenter.TryOpenHtmlBlock` 置 `_html` 后 `InOpaqueBlock`（`StreamSegmenter.cs:128`）为真，`Advance`（:69）不再提交边界，`ContainerEnds` 的深度配平看的是**同名**标签（:452-477），`<div><div></div>` 里第一个 `</div>` 只把深度减到 1，容器继续。代价是这段 HTML 每帧重解析，收益是不会出现"半个 HTML 被冻结成最终结果"。
- **未知元素 / `script` / `style` / `iframe` 的降级路径**：`Open`（`HtmlTree.cs:178`）里 `IsKnown` 为假 → `HasUnknownElements = true` → `HtmlGroup.Finish` 把**整组**退回 fallback（`MarkdigMapper.cs:120-124`）→ fallback 里是 `Model.HtmlBlock(source)` → 视图层 `TextBlockView` 按等宽次要色纯文本画出来（`BlockView.cs:187-190`）。`script` / `style` 不在 `HtmlElements.Block` / `Inline` 里，所以走的就是这条路；`Feed` 里的 `IsRawText` 只是把它们的**内容**当文本收进树（避免内容里的 `<` 被当标签），并不赋予任何行为。整条链上没有任何 DOM、没有事件、没有求值——"绝不执行"是结构性的，不是靠黑名单。单行的自定义元素（`<custom-widget>x</custom-widget>`）按 CommonMark 是段落中的行内 HTML，`InlineBuilder.VisitHtml` 的 `HtmlElements.Inline.Contains` 不通过 → 原文 `Append(tag, …)`（`MarkdigMapper.cs:519-529`）；回归测试 `tests/StreamMark.Core.Tests/Html/HtmlTests.cs:161`（`UnknownElements_KeepSource_CommentsHidden`）。
- **注释**：树构建器整段丢弃（`HtmlTree.cs:102-109`）；只含注释的 HTML 组在 `Finish` 里被过滤掉，什么都不输出（`HtmlGroup.IsCommentOnly`，`MarkdigMapper.cs:146-159`）。行内注释由 `VisitHtml` 的 `tag.StartsWith("<!--")` 分支丢弃（:523-526）。
- **结构不平衡**：不配对的结束标签被忽略（`Close` 找不到就返回；未知名字顺带置 `HasUnknownElements`，`HtmlTree.cs:244-259`）；`<p>one<p>two`、`<td>a<td>b` 这类靠 `ImplicitClose` 收拢（:196-220），回归测试 `tests/StreamMark.Core.Tests/Html/HtmlTests.cs:188`（`TreeBuilder_ImplicitClosesAndPartialTags`）。层级错乱的结束标签只会把栈从匹配处往下截断（`CloseUntil` / `Close` 的 `RemoveRange`），不会抛异常，整个解析器没有抛点。
- **块 key 的稳定性**：`HtmlGroup.Extend`（`MarkdigMapper.cs:106-116`）取的是成员区间的**并集**，`Finish` 把它写给合并后的块，所以 `HtmlBoxBlock` / 复用块的 `Key` 是首个 HTML 块的源码起点；`RangeOf`（:198）新增的两条分支也影响 key：`FootnoteGroup` 改取其中脚注的区间（:207-213，Markdig 给的组区间起点为 0，不改会与第一个块 key 重复），`span.IsEmpty` 的容器改取子块区间并集（:214-228，都为空才回落到单点区间）。改这两处会让流式与整篇解析之间 key 漂移。
- **解析异常**：本流程自研的解析段没有 `throw`；上游整体异常由 `StreamingDocument.Run` 的 `try/catch` 兜住（`src/StreamMark.Core/Streaming/StreamingDocument.cs:213`、:222）——保留上一个快照、`Debug.WriteLine`，界面停在旧内容。
- **取消**：本流程没有自己的取消令牌；HTML 里的图片沿用 `ImageSlot` 的"回投时校验身份 / 世代"模式（未在本页逐行复核）。
- **已知不支持**（`docs/decisions.md:57`，代码一致）：`colspan` / `rowspan` / 单元格背景色没有解析（`CssStyle.Apply` 无这些 name，`HtmlMapper.Table` 只按单元格个数算列数）；表格单元格只能放行内内容，单元格里的 `<p>` / `<div>` 被 `LineBreak()` 压平成换行（`src/StreamMark.Core/Html/HtmlMapper.cs:HtmlInlineBuilder.VisitElement`，:371-392）。

## Where to hook in
- **加一个支持的元素**：`src/StreamMark.Core/Html/HtmlTree.cs:HtmlElements` 的 `Block`（:12）/ `Inline`（:19）/ `Void`（:25）三张表（白名单是单一来源，`IsKnown` :32 只看 Block 与 Inline）；如果它内部要含空行、要在流式中不冻结，还要加进 `Containers`（:29），`MarkdownProfile.SegmenterContainers`（`src/StreamMark.Core/Parsing/MarkdownProfile.cs:52-55`）会自动并入。再在 `HtmlMapper.Block`（`HtmlMapper.cs:60`）的 switch 里决定"复用已有块还是 `HtmlBoxBlock`"。**别忘了 `README.md:63-66` 与 `docs/architecture.md:110-112` 的元素清单要同步。**
- **加一个新 CSS 属性**：`src/StreamMark.Core/Html/CssStyle.cs:CssStyle.Apply`（:62）的 `switch (name)` 加一个 case，并**顺手夹紧**（既有上限：`MaxSpacing` :12、`Min/MaxFontSize` :13-14、border 8、radius 64、width 4096）。落到行内文字要在 `HtmlTextStyle`（`src/StreamMark.Core/Model/HtmlStyles.cs:58`）加字段、在 `Over`（:82）里写叠加语义、在 `TextSegmentView.InlineTextSource.PropertiesFor`（`TextSegmentView.cs:1212`）里消费；落到盒子要在 `HtmlBoxStyle`（`HtmlStyles.cs:28`）加字段并在 `HtmlBoxBlockView.MeasureOverride`（:38）/ `Render`（:80）里消费。
- **加一个行内元素样式**：`HtmlMapper.HtmlInlineBuilder.StyleFor`（`HtmlMapper.cs:396`，单元格 / 行内上下文）与 `MarkdigMapper.InlineBuilder.VisitHtml`（`MarkdigMapper.cs:519`，段落内 HTML 标签栈）**两处**是分开的，两处都要加；纯样式（如 `kbd/mark`）还可以走 `ComponentSpec.StyleFor`（`src/StreamMark.Core/Parsing/ComponentSpec.cs:36-44`）。
- **加一个能折叠 / 有交互的块级标签**：走白名单组件 `MarkdownProfileBuilder.AddComponent`（`src/StreamMark.Core/Parsing/MarkdownProfile.cs:77`）+ `AddBlockComponent` 视图，而不是加进 `HtmlElements`。裸 `<details>` 由 `HtmlComponentBlockParser` 抢在建树之前处理（`src/StreamMark.Core/Parsing/HtmlComponentExtension.cs:88`），`HtmlMapper.Details`（:222）只服务"被包在其它 HTML 里"的 `<details>`。
- **会破坏东西的改动**：
  - 改拼接规则（`MapSiblings` 的 `HasOpenElements` 判定或 `HtmlGroup.Extend`，`MarkdigMapper.cs:47-76`、:106-116）：块 key = 合并区间的起点，改它会让流式与整篇解析的 key 漂移，触发无谓的 Insert/Remove，也会打断 `MarkdownView` / 组件状态按 key 的复用。
  - 给未知元素改成"渲染成 HTML"或引入任何执行 / 求值路径：现在未知元素靠 `HasUnknownElements` 整组退回 `HtmlBlock`（纯文本），这是"绝不执行"的唯一机制。
  - 把 HTML 容器从 `HtmlElements.Containers`（`HtmlTree.cs:29`）/ `SegmenterContainers` 里去掉，或在 `StreamSegmenter` 里提前提交边界：未闭合容器会被冻结，之后到达的 `</div>` 永远不会被认领。
  - 在 `CssStyle.Apply` 里去掉夹紧：`margin: 9999px` / `font-size: 400px` / `width: 100000` 会直接撑坏版面（`tests/StreamMark.Core.Tests/Html/HtmlTests.cs:83`（`Css_ClampsAndColors`）就是这条的守卫）。
  - 让 `HtmlBoxBlockView` 每次 `OnUpdate` 都重建子上下文：`_childContext` 缓存（`HtmlBoxBlockView.cs:17`、:23-31）是为了让子块在流式更新时不重新排版，去掉它等于每次追加都全量重排整棵子树。
  - 忘记 `HtmlMapper.Block` 里 `text = text with { Background = null }`（`HtmlMapper.cs:68`）：行内背景与盒子背景会同时生效，作者颜色的对比度判断也会拿错背景。
  - 在文本路径上绕过 `SegmentLayout`（`src/StreamMark.Avalonia/Rendering/SegmentLayout.cs:17`）直接整段重建 `TextLayout`：逐行排版是流式追加只重排差异行（`Format` :57）的前提，HTML 盒子里的大段文字会退回每帧 O(段落长度)。

## Related
- Modules: [core-parsing-and-model](../modules/core-parsing-and-model.md)
- Modules: [avalonia-text-rendering](../modules/avalonia-text-rendering.md)
- Modules: [core-streaming](../modules/core-streaming.md)
- Modules: [avalonia-images](../modules/avalonia-images.md)
- Modules: [theme-and-styles](../modules/theme-and-styles.md)
- Modules: [tests](../modules/tests.md)
- Flows: [streaming-token-to-view](streaming-token-to-view.md)
w.md)
