---
module: avalonia-selection
sources: ["src/StreamMark.Avalonia/Selection/**"]
commit: 8d344a6
updated: 2026-10-18
---
# 选区、导航与查找模块（选择 / 复制 / 目录 / 查找）

用一组与视图无关的坐标 `(消息 Entry, 顶层块 key, 片段序号, 片段内偏移)` 表示选区，由 `SelectionController` 处理指针与键盘交互，复制时**从块模型重新取文本**（`BlockText`），而不是从已实例化的视图取。因此被虚拟化回收的块仍在选区内、仍可复制。同一套坐标还承载导航（`DocumentNavigation` 的目录与文内锚点）与查找（`SearchModel` 的结果高亮与跳转）：三者的坐标语义相同，都交给宿主实现的 `ISelectionHost.ScrollToPoint` 去滚动。

**不负责：** 文本排版与命中几何（`TextSegmentView`、`CodeTextView`、`MathBlockView` 在 `avalonia-text-rendering`）；会话行摊平、实例化与滚动（`TranscriptPanel` 在 `avalonia-transcript`）；块模型与源码区间（`MarkdownBlock.Key` 在 `core-parsing-and-model`）；选区 / 查找高亮的实际绘制（各片段视图自己按 `SelectionModel.GetSegmentRange` / `GetHighlights` 画矩形）；右键菜单的外观（`MenuFlyout` 模板来自宿主主题）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `SelectionController` | src/StreamMark.Avalonia/Selection/SelectionController.cs:44 | 指针与键盘交互的入口。构造时向宿主控件挂 PointerPressed（`handledEventsToo: false`）/ PointerMoved（`true`）/ PointerReleased（`true`）/ KeyDown（`false`）（`:60-63`）；对外提供 `SelectAll` / `CopyAsync` / `GetSelectedText` / `GetSelectedMarkdown` / `ExtendByKey` |
| `SelectionModel` | src/StreamMark.Avalonia/Selection/SelectionModel.cs:7 | 选区本体（anchor / active）**加上查找结果高亮**；只发一个 `Changed` 事件，视图据此重绘 |
| `ISelectionHost` | src/StreamMark.Avalonia/Selection/SelectionController.cs:13 | 宿主契约：`HostControl` / `Selection` / `RealizedSegments` / `EntryCount` / `GetSnapshot` / `GetSource` / `OnSelectionDrag`（默认空实现，`:31`）/ `Plugins`（默认 null，`:34`）/ `ScrollToPoint`（默认空实现，`:37`） |
| `ISelectableSegment` | src/StreamMark.Avalonia/Selection/ISelectableSegment.cs:7 | 片段视图契约：`Key` / `Text` / `View` / `HitTest` / `GetCharacterBounds` |
| `BlockText` | src/StreamMark.Avalonia/Selection/BlockText.cs:11 | 唯一"块模型 → 片段列表/纯文本"的提取器，复制、查找、目录编号共用 |
| `SearchModel` | src/StreamMark.Avalonia/Selection/SearchModel.cs:8 | 文档 / 会话内查找：`Find` / `Next` / `Previous` / `MoveTo` / `Clear`，由宿主经 `Search` 属性懒建（`SearchModel` 的构造函数是 `internal`，`:13`） |
| `DocumentNavigation` | src/StreamMark.Avalonia/Selection/DocumentNavigation.cs:10 | 纯静态：`Outline(snapshot, entry)` 出目录、`ResolveAnchor(snapshot, entry, url)` 把 `#…` 解析成坐标、`Slug(text)` 做 GitHub 风格锚点 |
| `MarkdownContextMenu` | src/StreamMark.Avalonia/Selection/MarkdownContextMenu.cs:21 | 右键菜单（复制 / 复制为 Markdown / 全选 / 查找）：`Attach` 由两个宿主控件在构造时调用 |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `TextPoint` | Selection/TextPoint.cs:8 | 选区坐标 `(Entry, BlockKey, Segment, Offset)`，`IComparable` 字典序即阅读顺序 |
| `SegmentKey` / `SegmentKey.At` | Selection/TextPoint.cs:33 / :35 | 片段身份；`At(offset)` 生成该偏移处的 `TextPoint` |
| `SelectionModel.Set` / `.Extend` / `.Clear` | Selection/SelectionModel.cs:35 / :46 / :60 | 修改选区，变化时触发一次 `Changed`（值未变则不触发）；`Extend` 在 `_anchor is null` 时先把 anchor 设成同一个点（`:48-51`） |
| `SelectionModel.Range` / `.IsEmpty` | Selection/SelectionModel.cs:21 / :15 | 派生：anchor/active 归一化为 `(Start, End)`；`IsEmpty` 含"anchor==active"（即单击无选中） |
| `SelectionModel.SetHighlights` / `.ClearHighlights` / `.GetHighlights` | Selection/SelectionModel.cs:76 / :94 / :106 | 查找高亮：按 `SegmentKey` 分桶存 `(Start, End, Index)`（`:72-73`），查询时把区间夹进片段长度、`e > s` 才返回，并标出哪个是当前结果；三者都走同一个 `Changed` |
| `SelectionModel.GetSegmentRange` | Selection/SelectionModel.cs:124 | 派生查询：某片段内的 `[start,end)` 字符区间，供视图画高亮；`Segment < 0` 直接返回 null（`:127`） |
| `SelectionController.OnPressed` | Selection/SelectionController.cs:69 | 单击定位 / 双击选词 / 三击选段 / Shift 扩展的判定；先 `IsHostContent` 早退（`:77`），`FindPoint` 返回 null 则 `Clear()`（`:82-86`） |
| `SelectionController.IsHostContent` | Selection/SelectionController.cs:115 | `internal`：从指针下的控件向上走，遇到 `BlockView` / `ISelectableSegment` / `OverlayLayer` 返回 false，遇到宿主的直接子控件返回 true——即"按在宿主自己塞进来的内容上" |
| `SelectionController.OnMoved` / `.OnReleased` | Selection/SelectionController.cs:133 / :159 | 超过 `DragThreshold` 才开始拖拽并捕获指针（`:139-146`），拖拽中调 `OnSelectionDrag` 再 `Extend`；松开时清 `_pressPending` 并释放捕获 |
| `SelectionController.OnKeyDown` | Selection/SelectionController.cs:170 | 热键顺序：⌘C / ⌘⇧C → ⌘A → Esc → Shift+方向键（`ExtendByKey`） |
| `SelectionController.ExtendByKey` | Selection/SelectionController.cs:199 | `public`，供测试与宿主直接调用：Shift+←/→ 按字符（Alt 或 Ctrl 按词）、↑/↓ 按显示行、Home/End 到片段首尾；返回是否"处理了该按键" |
| `SelectionController.MoveHorizontal` / `.MoveVertical` / `.NeighbourSegment` | Selection/SelectionController.cs:244 / :321 / :285 | 三种移动的实现：字符/词移动含代理对处理（`:268-275`）；纵向用字符框上下方的探测点按坐标命中；跨片段用一次全量扫描找阅读顺序上的邻居 |
| `SelectionController.SegmentText` / `.SegmentLength` | Selection/SelectionController.cs:225 / :242 | 从 `_host.GetSnapshot` + `BlockText.Segments` 取片段文本，**不依赖视图是否实例化** |
| `SelectionController.FindPoint` | Selection/SelectionController.cs:472 | 宿主坐标 → `(片段, TextPoint)`：先 `InputHitTest` 找祖先片段，否则取纵向最近的**已实例化且可见**片段（`:498`） |
| `SelectionController.WordAt` | Selection/SelectionController.cs:535 | 双击选词：字母/数字/`_` 连续段；落在非词字符上只选 1 个字符 |
| `SelectionController.SelectAll` | Selection/SelectionController.cs:347 | 从第一个非空条目第一个块的 (0,0) 选到最后一个非空条目末块末片段的末尾 |
| `SelectionController.GetSelectedText` | Selection/SelectionController.cs:382 | 纯文本导出：块间 `\n\n`，块内片段用 `Segment.Separator`，整段选中时加 `Prefix` |
| `SelectionController.GetSelectedMarkdown` | Selection/SelectionController.cs:435 | Markdown 源码导出：取选区覆盖的**完整块**的 `Range`，多消息间 `\n\n`，末尾 `TrimEnd` |
| `SearchModel.Find` | Selection/SearchModel.cs:33 | 按片段 `IndexOf` 找全部结果（**结果不跨片段**），`CurrentIndex` 置 0；遍历 `GetSnapshot(entry)`，因此会话中未解析的按需条目会被解析 |
| `SearchModel.MoveTo` / `.Next` / `.Previous` | Selection/SearchModel.cs:73 / :67 / :70 | 移动当前结果（`Next`/`Previous` 循环）→ `Publish()` → `_host.ScrollToPoint` |
| `SearchModel.Clear` | Selection/SearchModel.cs:85 | 清 query / 结果 / `CurrentIndex`，并 `Selection.ClearHighlights()` |
| `SearchModel.Publish` | Selection/SearchModel.cs:94 | 有结果时 `Selection.SetHighlights(matches, current)`，无结果时 `ClearHighlights()`，都触发自己的 `Changed` |
| `OutlineItem` / `DocumentNavigation.Outline` | Selection/DocumentNavigation.cs:7 / :13 | 目录项 `(Target, Level, Text, Id)`；`Outline` 递归遍历顶层块（含 HTML 盒子 / 引用 / 列表 / 容器，`:83`）里的 `HeadingBlock`，用 `SegmentOf`（`:106`）按**引用相等**把标题的第一个片段对到顶层块的片段下标上 |
| `DocumentNavigation.ResolveAnchor` | Selection/DocumentNavigation.cs:33 | `#fn:标签` → 脚注定义（`FootnoteDefinition.Label` 带不带 `^` 都认，`:50`）；`#id` → 先比 `HeadingBlock.Id`（忽略大小写），再比 `Slug` 后的文字；找不到返回 null，由宿主自己处理 |
| `DocumentNavigation.Slug` | Selection/DocumentNavigation.cs:66 | 小写、空白转 `-`、保留字母/数字/CJK/`-`/`_`，其余标点丢弃 |
| `MarkdownMenuCommand` / `MarkdownContextMenu.Attach` | Selection/MarkdownContextMenu.cs:8 / :28 | 四个命令的枚举；`Attach` 装 `MenuFlyout`（`Opening` 时重建菜单项，`:31`），并在 `ContextRequested` 的**隧道阶段**把事件标记为已处理来关掉菜单（`:34-40`） |
| `MarkdownContextMenu.FindRequestedEvent` | Selection/MarkdownContextMenu.cs:24 | `RoutedEvent<RoutedEventArgs>`（Bubble），菜单里的"查找"把它 Raise 到宿主，由宿主显示自己的查找框 |
| `MarkdownContextMenu.DefaultText` | Selection/MarkdownContextMenu.cs:63 | 默认（英文）菜单文字；宿主用 `MarkdownServices.MenuText` 覆盖 |

## Dependencies
- Uses: `core-parsing-and-model` via `DocumentSnapshot`、`MarkdownBlock.Key`（= `Range.Start`）、`InlineContent`、`InlineObject`、`HeadingBlock.Id` / `FootnotesBlock` / `FootnoteDefinition.Label` — `Selection/BlockText.cs:32`、`Selection/DocumentNavigation.cs:22`、`:50`、`src/StreamMark.Core/Model/Blocks.cs:49`、`:182`
- Uses: `avalonia-text-rendering` via `ISelectableSegment` 的实现与 `BlockView.Segments()` — `Rendering/BlockView.cs:43`、`Rendering/BlockView.cs:157`、`:226`、`Rendering/CodeBlockView.cs:403`、`Rendering/MathBlockView.cs:132`、`Rendering/TableBlockView.cs:233`、`Rendering/DiffBlockView.cs:202`、`Rendering/DiagramBlockView.cs:358`、`Rendering/ContainerViews.cs:197`、`:326`、`:413`
- Uses: `avalonia-transcript` via `ISelectionHost` 的实现，含拖拽自动滚动与跳转定位 — `Transcript/TranscriptPanel.cs:1126`、`:1133`、`:1153`、`:95`
- Uses: `avalonia-plugin-system` 通过 `ISelectionHost.Plugins` 取行内组件纯文本 — `Selection/SelectionController.cs:432` → `Plugins/IMarkdownPlugin.cs:114`、`Plugins/BuiltInComponentsPlugin.cs:236`
- Uses: `theme-and-styles` 只到"宿主提供菜单文字与开关"这一层 — `Extensions/MarkdownServices.cs:43`（`ContextMenu`）、`:46`（`MenuText`）
- Used by: `avalonia-transcript` via `TranscriptView.Selection` / `.SelectionController` / `.Search` / `.GetOutline()` / `.ScrollToPoint` 转发到面板 — `Transcript/TranscriptView.cs:30`、`:32`、`:35`、`:38`、`:41`
- Used by: 单文档视图 via `MarkdownView : ISelectionHost` — `MarkdownView.cs:16`、`:63`、`:286-296`
- Used by: `avalonia-text-rendering` via `RenderContext.Selection` 与高亮绘制 — `Rendering/RenderContext.cs:38`、`Rendering/TextSegmentView.cs:575`、`:583`、`Rendering/CodeBlockView.cs:635`、`:644`、`Rendering/DiffBlockView.cs:690`、`:817`、`Rendering/MathBlockView.cs:96`
- Used by: 折叠块视图 via `BlockText.Segments(summary).Count` 让编号对齐 — `Plugins/BuiltInComponentsPlugin.cs:92`

## State it owns
- 选区本体只有两个字段：`SelectionModel._anchor` / `_active`（`Selection/SelectionModel.cs:9-10`），两个 `TextPoint` 都是值类型，**不含任何视图或 `SegmentKey` 引用**。这是"虚拟化回收后选区仍有效"的根据。
- 查找高亮是同一对象里的第二份状态：`_highlights`（`Dictionary<SegmentKey, List<(int Start, int End, int Index)>>`）与 `_currentHighlight`（`Selection/SelectionModel.cs:72-73`）。它也是纯值，与视图无关。
- 查找自身的状态在 `SearchModel` 里：`Query` / `MatchCase` / `_matches`（`(TextPoint Start, TextPoint End)` 列表）/ `CurrentIndex`（`Selection/SearchModel.cs:11-27`）。`Matches` 以 `IReadOnlyList` 暴露给宿主。
- 坐标语义：`Entry` = 会话中第几条消息（`RenderContext.Entry`，`Rendering/RenderContext.cs:41`）；`BlockKey` = 顶层块在源码中的起始偏移（`StreamMark.Core/Model/Blocks.cs:18`）；`Segment` = 块内第几个文本片段；`Offset` = 片段内字符偏移（`Selection/TextPoint.cs:8`）。
- 稳定性来源：`BlockKey` 取自源码偏移，而源码只追加（`Selection/TextPoint.cs:6` 注释），所以流式重解析后同一块的 key 不变；`Entry` 在消息插入/删除后会漂移，`TranscriptPanel.SyncEntries` 在 `Context.Entry != i` 时把 `Context` 置 null 让下一帧重建（`Transcript/TranscriptPanel.cs:321-327`），但**已经在 `SelectionModel` / `SearchModel` 里的 `TextPoint` 不会跟着平移**。
- 派生数据（不持久化，随时可重算）：`Range`、`IsEmpty`、`GetSegmentRange`、`GetHighlights`；以及复制结果文本。
- 被虚拟化移出的块如何仍可复制 / 仍能被键盘移动与查找：`GetSelectedText` / `GetSelectedMarkdown` / `SegmentText` / `NeighbourSegment` / `SearchModel.Find` 遍历的都是 `_host.GetSnapshot(entry)`，不是 `RealizedSegments()`（`Selection/SelectionController.cs:392`、`:444`、`:227`、`:291`、`Selection/SearchModel.cs:43`）。`TranscriptPanel.GetSnapshot` 对尚未解析的按需条目会就地 `EnsureParsed()` 并返回 `Latest`（`Transcript/TranscriptPanel.cs:1133-1148`）；已回收（存在但未实例化）的行本来就在 `state.Applied` 里。
- 片段身份 `SegmentKey` 由 `BlockView.KeyFor` 在每次 `Update` 时按阅读顺序自增分配（`Rendering/BlockView.cs:45`，调用点 `:173`、`Rendering/CodeBlockView.cs:218`、`Rendering/MathBlockView.cs:34`、`Rendering/TableBlockView.cs:85`、`Rendering/DiffBlockView.cs:138`、`Rendering/DiagramBlockView.cs:77`、`Rendering/ContainerViews.cs:360`、`Plugins/BuiltInComponentsPlugin.cs:76`），编号本身不持久化，靠"与 `BlockText` 遍历顺序一致"这条契约在每帧重建。

## Contracts it exposes
- `ISelectableSegment`（`Selection/ISelectableSegment.cs:7`）：`Key`（片段身份）、`Text`（片段文本，`HitTest` 的偏移域与 `BlockText.Segment.Length` 相同，U+FFFC 占位由复制端替换，见 `:11` 注释）、`View`（坐标换算用）、`HitTest(Point)→int`、`GetCharacterBounds(int)→Rect`。`BlockView.Segments()` 必须按阅读顺序、深度优先地返回本块全部片段（`Rendering/BlockView.cs:43`；容器实现见 `Rendering/BlockView.cs:157`、`Rendering/ContainerViews.cs:197`、`Rendering/TableBlockView.cs:233`、`Rendering/ContainerViews.cs:413`）；`DiagramBlockView` 在显示图而非源码时返回空（`Rendering/DiagramBlockView.cs:358`，条件 `DiagramVisible` 在 `:258`），`CollapsibleBlockView` 折叠时只返回标题（`Plugins/BuiltInComponentsPlugin.cs:207-210`）。
- `ISelectionHost`（`Selection/SelectionController.cs:13`）：`RealizedSegments()` 只需给"已实例化"的片段，供 `FindPoint` 的最近片段回退与 `MoveVertical` 的字符框查询用（`:498`、`:324`）；`GetSnapshot(entry)` 必须能返回未实例化条的完整块模型（这是跨虚拟化复制、查找与键盘移动的前提）；`GetSource(entry, start, length)` 给 ⌘⇧C 取源码；`OnSelectionDrag(hostPoint)` 有默认空实现（`:31`），面板覆写为滚动；`ScrollToPoint(point)` 有默认空实现（`:37`），面板覆写为"先估算滚动、行实例化后按字符实际位置微调一次"（`Transcript/TranscriptPanel.cs:95`、`:946-955`），`MarkdownView` 覆写为 `BringIntoView`（`MarkdownView.cs:214`）。
- `BlockText` 的隐式契约：`Segment.Length` 与视图 `Text.Length` 相等；`Collect` 的遍历顺序必须与块视图分配编号的顺序一致（`Selection/BlockText.cs:6-9` 注释）。折叠块标题无 `summary` 时用负编号 `-1` 绕过（`Plugins/BuiltInComponentsPlugin.cs:76`，`Selection/SelectionModel.cs:127` 忽略负编号）。
- 剪贴板格式：只用 `clipboard.SetTextAsync(string)`（`Selection/SelectionController.cs:377`）——没有 HTML/自定义 MIME 格式。两种文本形态：纯文本（公式还原为 `$latex$`、表格单元格用制表符、列表项带 `- ` / `1. ` / `[x] ` 前缀，`Selection/BlockText.cs:107-112`、`Selection/SelectionController.cs:418`、`:420-423`）与 Markdown 源码（完整块的原文，`Selection/SelectionController.cs:457-466`）。
- 菜单契约：`MarkdownContextMenu.Attach(host, controller)` 只做两件事——给宿主装 `ContextFlyout`，以及"宿主关掉菜单时在隧道阶段吃掉 `ContextRequested`"（`Selection/MarkdownContextMenu.cs:28-40`）。文字每次打开时从 `MarkdownServices.GetServices(host).MenuText` 取（`:45-47`），"查找"只 Raise `FindRequestedEvent`（`:59`）——**宿主必须自己监听这个事件才有查找框**。
- 文内锚点契约：`MarkdownView.OnAnchorLinkClicked`（`MarkdownView.cs:242-249`）与 `TranscriptPanel.OnLinkClicked`（`Transcript/TranscriptPanel.cs:190-207`）都只在 `ResolveAnchor` 返回非 null 时 `ScrollToPoint` 并 `Handled = true`，否则事件继续冒泡交给宿主。

## How to extend
- To add 新块类型成为可选中/可复制：实现 `ISelectableSegment`（或让块视图的 `Segments()` 返回内部的 `TextSegmentView`）→ 在 `BlockView.OnUpdate` 里用 `KeyFor(context, topKey, ref segment)` 按阅读顺序分配编号（`Rendering/BlockView.cs:45`）→ 在 `BlockText.Collect` 加同顺序、同片段数的 `case`（`Selection/BlockText.cs:39`）。缺 `BlockText` 这一步的症状：该块能选中、能画高亮，但复制时缺失它的文字，并且**后续块的全部片段文本会整体前移错位**（复制由 `new SegmentKey(entry, block.Key, i)` 按下标重算，`Selection/SelectionController.cs:407`）；查找与目录也会跟着错位（`Selection/SearchModel.cs:49-56`、`Selection/DocumentNavigation.cs:106-124`）。
- To add 新片段类型（一个块里多出一种可选文本，如表格单元格之外的子片段）：先决定它在阅读顺序中的位置，再同时改两处：视图侧分配编号的顺序（`Rendering/BlockView.cs:45` 调用点）与 `BlockText.Collect` 对应分支的 `list.Add` 次序（`Selection/BlockText.cs:39`）；`Segment.Separator` / `Prefix` 决定复制时片段间与整段选中时的拼接（`Selection/BlockText.cs:17`）。两侧顺序不一致不会报错，只会静默复制出别人的文本。
- To add 新复制格式（如 HTML、CSV）：在 `GetSelectedText` / `GetSelectedMarkdown` 旁加 `GetSelectedXxx()`，再在 `CopyAsync` 的选择处加分支（`Selection/SelectionController.cs:374`），最后在 `OnKeyDown` 里加触发键（`Selection/SelectionController.cs:170`，注意先做平台热键匹配再做 Shift 判定）；要在右键菜单里也能触达，同时加 `MarkdownMenuCommand` 成员、`Populate` 里的菜单项（`Selection/MarkdownContextMenu.cs:55`）与 `DefaultText`（`:63`）。缺 `OnKeyDown` 一步的症状：方法存在但用户无法触达；缺 `CopyAsync` 分支的症状：按键触发后仍复制纯文本。
- To add 新快捷键移动（如 Shift+PageUp）：在 `ExtendByKey` 的 `key switch` 里加分支（`Selection/SelectionController.cs:206`），**并把它加进最后那个 `key is Key.Left or …` 列表**（`:218`）——那个列表决定"没有目标时是否也算处理了按键"，漏改会让按键落到宿主。
- To add 插件行内组件的纯文本：由插件实现 `IInlineComponentRenderer.ToPlainText`（`Plugins/IMarkdownPlugin.cs:114`，示例 `Plugins/BuiltInComponentsPlugin.cs:236`），本模块通过 `Selection/SelectionController.cs:432` 取用，无需改本模块。
- To change 拖拽时自动滚动的速度/边界：改 `TranscriptPanel.OnSelectionDrag`（`Transcript/TranscriptPanel.cs:1153`，单次最多 ±40px），本模块只负责调用（`Selection/SelectionController.cs:152`）。
- To change 跳转落点（现在约在视口上方 30%）：改 `TranscriptPanel.ScrollToPoint` 的估算（`Transcript/TranscriptPanel.cs:117`）与 `TargetY` 之后的微调（`:946-955`）两处，只改一处会让"先跳到估算位置"和"微调"互相打架。
- To add 一个新的文内锚点协议（如 `#cite:1`）：在 `DocumentNavigation.ResolveAnchor` 里加分支（`Selection/DocumentNavigation.cs:33-63`），返回 `TextPoint?`；两个宿主控件的点击处理器不必改（它们只问"解析得到吗"）。

## Invariants and traps
- 选区坐标绝不持有视图引用 — 否则虚拟化回收后选区指向已丢弃的控件 — see `Selection/SelectionModel.cs:9`、`Selection/SelectionController.cs:392`。
- `GetSelectedText` 的片段区间按**片段下标**从 `BlockText` 重算，不依赖任何视图是否实例化；而视图侧编号只在 `Update` 时分配，因此"顺序一致"是唯一粘合剂 — see `Selection/SelectionController.cs:407`、`Rendering/BlockView.cs:45`。
- 指针判定顺序：`OnPressed` 先做 `IsHostContent` 早退（按在宿主内容上时既不选也不抢焦点，`Selection/SelectionController.cs:77-80`），再 `Focus()`，`FindPoint` 为 null 时 `Clear()`；然后 `ClickCount == 2` 选词、`>= 3` 选整段、`default` 分支才看 Shift 扩展（`:88-107`）。注意 `case 2` 里的 `WordAt` 调用在 `switch` 分支内声明（`:90-93`，没有花括号），`case >= 3` 与 `default` 不会执行它。
- 拖拽不是"按下即成立"：`OnPressed` 只记 `_pressPending = e.ClickCount == 1`（`Selection/SelectionController.cs:109`），真正开始拖拽要等 `OnMoved` 里曼哈顿距离 `>= DragThreshold`（3px，`:54`、`:139`），此时才 `e.Pointer.Capture(host)`（`:146`）。这正是"单击能送达链接、图片与宿主内容"的原因——按下阶段不捕获指针，松开事件照常到达目标控件（`Rendering/TextSegmentView.cs:891` 只在选区非空时拒绝链接/图片点击）。双击 / 三击虽然也走 `OnPressed`，但 `_pressPending` 为 false，随后的移动不会进入拖拽。
- `IsHostContent` 只看可视树形状：从指针下的控件向上走到宿主，途中遇到 `BlockView` / `ISelectableSegment` / `OverlayLayer` 就返回 false，否则只要某一级的父就是宿主即返回 true（`Selection/SelectionController.cs:115-131`）。`docs/decisions.md` 阶段 5 把它描述成"按在 `ContentEntry` 控件上不开始选择"，代码更宽：任何宿主直接挂进面板的控件都算宿主内容。回归用例 `tests/StreamMark.Avalonia.Tests/Rendering/InteractionRegressionTests.cs:43` 覆盖三种输入（宿主的子控件 / 宿主内容本身 / 文本片段）。
- Shift 单击要求 anchor 已存在（`Selection/SelectionController.cs:98`）；且 `OnPressed` 在 `FindPoint` 返回 null 时会先 `Clear()`（`:82-86`），所以点到完全没有片段的空白且无最近片段回退时，Shift 扩展会退化为清空。首击的 anchor=`active` 使 `IsEmpty` 为真，所以"点一下再 Shift 点"才有可见效果。
- 键盘扩展另有两条坑：`ExtendByKey` 在没有 `Selection.Active` 时直接返回 false（`Selection/SelectionController.cs:201`），而"没有目标"时仍对六个键返回 true（`:216-219`）；`MoveVertical` 需要目标片段**已实例化**（它从 `RealizedSegments()` 里找，`:324`），所以对滚出视口的片段按 ↑/↓ 只会消耗按键、不移动。词移动是"先跳空白再跳一个词"，与 `WordAt` 的 `IsLetterOrDigit || '_'` 不是同一套定义（`:257-266` vs `:542`）。
- `SelectionModel.SetHighlights` 每次都**整体重建**字典并触发 `Changed`（`Selection/SelectionModel.cs:76-92`），因此 `Find` 一次会重绘所有片段；`GetHighlights` 把区间夹进当前片段长度（`:114-115`），片段变短（流式重解析）时旧高亮自动收缩而不会越界。
- 查找结果**不跨片段**：`Find` 在单个片段文本里 `IndexOf`（`Selection/SearchModel.cs:53`），所以跨行内格式（如 `**bold**` 拼成的词）或跨表格单元格的词找不到；`BlockText` 的 `Raw` 片段（代码 / 公式 / HTML）也照样参与，所以能在代码块里找到。
- 查找高亮的绘制只做了三处：`TextSegmentView`（`Rendering/TextSegmentView.cs:575`）、`CodeTextView`（`Rendering/CodeBlockView.cs:644`）、`DiffBodyView`（`Rendering/DiffBlockView.cs:817`），颜色来自 `Md.SearchHighlightBackground` / `Md.SearchCurrentBackground`（`MarkdownResourceKeys.cs:28`、`:30`，默认值 `Rendering/MarkdownStyle.cs:111-112`）。**`MathBlockView` 只画选区、不画查找高亮**（它只用 `GetSegmentRange`，`Rendering/MathBlockView.cs:96`），公式块里的命中结果会被找到、能跳转，但看不见高亮。
- ⌘C 与 ⌘⇧C 的差异只在 `asMarkdown` 这一个参数：`OnKeyDown` 用 `HotkeyConfiguration.Copy` 匹配（`:173`，平台配置缺失时回落到 "C + Control"），命中后 `asMarkdown = e.KeyModifiers.HasFlag(KeyModifiers.Shift)`（`:178`）→ `GetSelectedMarkdown()` 而不是 `GetSelectedText()`（`:374`）。两者产出差异很大：纯文本保留制表符/列表前缀，Markdown 给的是选区覆盖的**完整块**源码，部分选中的段落也会整段带出来（`Selection/SelectionController.cs:435`；对应测试 `tests/StreamMark.Avalonia.Tests/Transcript/TranscriptViewTests.cs:129`）。
- `GetSelectedText` 跳过"空区间"片段：`e == s && segment.Length > 0` 时 continue（`Selection/SelectionController.cs:411`），因此选区端点在一段末尾与下一段开头重合时不会插入多余分隔符。
- 被实例化但**被窗口化的代码块**仍是全量可复制：`CodeTextView.Text` 返回整个代码块文本（`Rendering/CodeBlockView.cs:442`），选中整块时复制的正是全量（测试 `tests/StreamMark.Avalonia.Tests/Transcript/LongCodeTests.cs:72`）。屏幕只画窗口内的行（`_lineStarts` 在 `Rendering/CodeBlockView.cs:421`、`:520`，按行拆区间的绘制在 `:635`）。
- 图片占位字符：`BlockText.PlainText` 把 U+FFFC 换成 `$latex$` / 图片 `Alt` / `[x]` / 组件文本（`Selection/BlockText.cs:167-174`）。而"图片在 `[![…](…)](…)` 中应带链接区间、点击打开链接而非图片查看器"的逻辑**不在本模块**，在 `avalonia-text-rendering`：`TextSegmentView.OnPointerReleased` 先判 `Loaded when LinkAt(position) >= 0` 再发 `LinkClicked`（`Rendering/TextSegmentView.cs:906-912`），只有无链接时才走 `ImageClicked` / 图片查看器（`:913-925`）。本模块无关，但 `TextSegmentView` 也刻意在选区非空时拒绝链接/图片点击，避免拖拽选中被当成点击（`Rendering/TextSegmentView.cs:891`）。
- 图表块在显示图时**没有片段**：`Segments() => DiagramVisible ? [] : [_code]`（`Rendering/DiagramBlockView.cs:358`）。后果是双向的——在图上点只能靠"纵向最近片段"回退到相邻块，但 `BlockText.Collect` 的 `CodeBlock` 分支照样给出源码片段，所以跨过它的选区仍会复制出图表源码（`Selection/BlockText.cs:49-51`）。
- `SelectAll` 依赖"第一个/最后一个非空快照"：条目快照为空（`Count == 0`）会被跳过（`Selection/SelectionController.cs:351`、`:357`），完全没有内容的文档不会改变选区（早先的选区保留）。
- `GetSelectedMarkdown` 只取完整顶层块的源码区间（`Selection/SelectionController.cs:449-459`），因此对嵌套内容不会裁到子块级别；`start/end` 初值 `int.MaxValue/-1` 保证无重叠块时返回空串（`:448`、`:460`）。
- 折叠块标题参与选区的条件：只有存在 `<summary>` 子块时标题才占一个正编号片段，否则用 `-1` 且被 `GetSegmentRange` 忽略（`Plugins/BuiltInComponentsPlugin.cs:76`、`Selection/SelectionModel.cs:127`），与 `BlockText.Collect` 的 `ComponentBlock` 分支（summary 优先）配对（`Selection/BlockText.cs:86-96`）。编号用完后还要跳过 summary 里其余片段（`Plugins/BuiltInComponentsPlugin.cs:81`，`CountSegments` 在 `:92`），改这里必须同时看 `BlockText`。
- 目录只认能对上下标的标题：`Outline` 用 `SegmentOf` 按**引用相等**匹配第一个片段的内容（`Selection/DocumentNavigation.cs:118`），匹配不到就跳过该标题（`:20`）——所以标题块的第一个片段若在顶层块片段列表里找不到（片段顺序不一致的实现），该标题会静默地从目录里消失，而不是报错。
- 目录的可见范围取决于调用方：`MarkdownView.GetOutline()` 用 `_presenter.Applied`（`MarkdownView.cs:211`），`TranscriptPanel.GetOutline()` **只遍历已解析条目**（`Transcript/TranscriptPanel.cs:181`），所以会话里靠后的按需条目在解析前不会出现在目录中。
- 拖拽期间指针超出视口才会滚动，且每帧调用一次、单次最多 ±40px（`Selection/SelectionController.cs:152`、`Transcript/TranscriptPanel.cs:1153-1162`）；滚动导致的行回收不影响选区，但 `FindPoint` 的最近片段回退要求片段 `IsEffectivelyVisible`（`Selection/SelectionController.cs:498`），所以滚出视口的片段不会命中。
- 右键菜单在宿主没有 `MenuFlyout` 模板时**不显示**（`docs/decisions.md` 阶段 5 的同一结论；`Selection/MarkdownContextMenu.cs:30` 用的是主题化 `MenuFlyout`）。关菜单是靠隧道阶段吃掉 `ContextRequested`（`:34-40`），所以 `ContextMenu = false` 时宿主自己挂的 `ContextFlyout` 也会一起失效。

## Tests
测试位置：`tests/StreamMark.Avalonia.Tests/`。与本模块直接相关的用例：
- `tests/StreamMark.Avalonia.Tests/Rendering/SelectionAndFadeTests.cs:14` `ProgrammaticSelection_AcrossBlocks_IsPaintedAndCopied` — 跨块选区 + `GetSelectedText` + `SelectAll` + `GetSelectedMarkdown`（端点断言含 `- item one`、`1\t2`，`:32`、`:38`、`:41`）。
- `tests/StreamMark.Avalonia.Tests/Transcript/TranscriptViewTests.cs:108` `Selection_SpansMessages_AndCopiesVirtualizedRows` — 跨消息、多数行未实例化的复制（`:123` 用 `TextPoint(last, int.MaxValue, 0, 0)` 作末端）。
- `tests/StreamMark.Avalonia.Tests/Transcript/DeferredEntryTests.cs:19` `DeferredEntries_ParseOnlyNearViewport` — 尾部 `:48-50` 全选跨过尚未解析的按需条目。
- `tests/StreamMark.Avalonia.Tests/Transcript/LongCodeTests.cs:14` `ThreeThousandLineCode_IsChunked_AndCopiesExactly` — 分段虚拟化的超长代码块仍能整块（`:72`）/按行（`:76`）复制。
- `tests/StreamMark.Avalonia.Tests/Rendering/InteractionTests.cs:50` `Outline_And_Anchors_ScrollToTargets`（目录、`ResolveAnchor` 的 `#deep-section` / `#fn:note` / 找不到时返回 null）、`:96` `Search_FindsAcrossEntries_HighlightsAndScrolls`（跨条目计数、`matchCase`、`GetHighlights` 的 `Current`、循环跳转、`Clear`）、`:129` `Keyboard_ExtendsSelection_AcrossSegments`（`ExtendByKey` 的 End / Right / Alt+Right / Up / Home）、`:158` `ContextMenu_And_Fullscreen_And_Automation`（`MenuText` 定制、无选区时复制禁用、`FindRequestedEvent`、无障碍 peer）。
- `tests/StreamMark.Avalonia.Tests/Rendering/InteractionRegressionTests.cs:43` `Selection_IgnoresPressesOnHostContent` — `IsHostContent` 的三种输入。
- `tests/StreamMark.Avalonia.Tests/Rendering/HtmlRenderingTests.cs:146`、`tests/StreamMark.Avalonia.Tests/Plugins/PluginSystemTests.cs:156` — 全选后的纯文本（HTML 与插件行内组件文本）。
命名模式：类名 `<主题>Tests`，方法名用下划线分词（`<场景>_<断言>`），选区用例都走 `HeadlessApp.Run` + `HeadlessApp.Settle`（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs`），并直接调 `view.ApplyLatest()` 再读 `Snapshot`。
只跑一个文件：仓库 README 只给出整库命令 `dotnet test StreamMark.slnx`（`README.md:139`）；按用例过滤可用 `dotnet test StreamMark.slnx --filter "FullyQualifiedName~SelectionAndFadeTests"`（此 `--filter` 用法未在仓库文档或 CI 中验证）。

## Related flows
- [text-selection-copy](../flows/text-selection-copy.md)
- [transcript-virtualization-scroll](../flows/transcript-virtualization-scroll.md)
- [streaming-token-to-view](../flows/streaming-token-to-view.md)
