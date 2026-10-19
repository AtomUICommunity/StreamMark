---
module: avalonia-transcript
sources: ["src/StreamMark.Avalonia/Transcript/**"]
commit: 8d344a6
updated: 2026-10-18
---
# avalonia-transcript（会话记录：摊平行 + 自研虚拟化）

把长会话里所有消息摊平成一张行列表（每条消息的每个 Markdown 顶层块一行、每条宿主内容一行），只实例化视口上下各 600px 内的行，未实例化的行用估算高度参与滚动，并在实测高度或插入/删除行之后做滚动锚定与贴底跟随。导航（目录 / 文内锚点 / 查找跳转）与"代码块折叠状态改变后重建分段"也落在这个面板里。
**不负责：** Markdown 解析、快照与差异（`core-streaming`，`StreamingDocument` / `SnapshotDiff`）；块视图本身的渲染与块模型 → 视图映射（`avalonia-text-rendering`，`BlockViewFactory` / `BlockView` / `CodeBlockView`）；选区坐标、复制、查找与目录的**模型**（`avalonia-selection`，`SelectionModel` / `SelectionController` / `SearchModel` / `DocumentNavigation`）；样式解析与 `Md.*` 资源（`Rendering/MarkdownStyle.cs`）；流式节奏与渐显时间记录（`StreamMark.Avalonia/MarkdownStream.cs`、`StreamMark.Core/Pacing`）。

## Entry points
| Symbol | File | Role |
|---|---|---|
| `TranscriptView` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:13` | 对外控件：`ScrollViewer` + `TranscriptPanel`，沿用宿主 `ScrollViewer` 主题（`StyleKeyOverride` 第 24 行）；禁用横向滚动条（第 20 行） |
| `TranscriptView.Entries` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:26` | `ObservableCollection<TranscriptEntry>`，宿主往里加消息；构造时 `Panel.SetEntries(Entries)`（第 18 行） |
| `TranscriptView.Panel` / `Selection` / `SelectionController` / `IsPinnedToBottom` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:28,30,32,43` | 透传面板的状态与选区 |
| `TranscriptView.Search` / `.GetOutline()` / `.ScrollToPoint(TextPoint)` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:35,38,41` | 会话内查找、目录、按选区坐标跳转（三者都转调面板） |
| `TranscriptView.MaxContentWidth` / `.EntrySpacing` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:49,55` | 内容列居中宽度、消息间距（转调面板） |
| `TranscriptView.ScrollToEnd()` | `src/StreamMark.Avalonia/Transcript/TranscriptView.cs:46` | `new` 隐藏基类方法，转调 `TranscriptPanel.ScrollToEnd` |
| `TranscriptPanel` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:29` | `Panel, ILogicalScrollable, ISelectionHost`：行模型、虚拟化、锚定、贴底、选区宿主、查找与目录 |
| `TranscriptPanel.ApplyLatest()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:373` | 把各文档最新快照应用到行（测试可同步调用） |
| `TranscriptPanel.ScrollToPoint(TextPoint)` / `.ScrollToBlock(int,int)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:95,123` | 先按估算滚动，再在测量里按字符实际位置校正到视口上部约 30% |
| `TranscriptPanel.GetOutline()` / `.Search` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:176,1120` | 已解析条目的目录（`OutlineItem`）；`Search` 是惰性创建的 `SearchModel` |
| `TranscriptPanel.ScrollToEnd()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:1020` | 置 `_pinned = true` 并请求重新测量 |
| `TranscriptEntry` / `MarkdownEntry` / `ContentEntry` | `src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs:8,11,72` | 条目类型 |
| `MarkdownEntry.Stream` | `src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs:26` | 流式条目持有的 `MarkdownStream`；面板据此把**显示帧来源**交给节奏控制器（`TranscriptPanel.cs:389`） |
| `MarkdownEntry.FromText` / `.FromTextDeferred` / `.EnsureParsed` | `src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs:29,40,52` | 静态消息：立即解析 / 延迟解析 / 强制解析 |

## Key types and functions
| Symbol | File | Role |
|---|---|---|
| `EntryState` (private) | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:31` | 一条消息的状态：已应用快照 `Applied`、`RenderContext`、上次刷新视图用的 `RefreshedContext`、行 `Rows`、每个顶层块占的行数 `Slots`、块状态 `BlockStateStore`、宿主内容控件缓存 `ContentView` |
| `Row` (private) | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:45` | 一行：所属状态、块、分段序号 `Chunk/ChunkCount`、高度 `Height`、是否已实测 `Measured`、缓存纵坐标 `Top`、在 `_rows` 中的位置 `Index`（`EnsureRows` 时更新，行结构变化后可能过期） |
| `SyncEntries()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:281` | 按引用复用旧 `EntryState`（订阅/退订 `SnapshotReady`），重建 `_entries`，并在 `Entry` 序号变化时丢弃 `RenderContext`（第 320–326 行） |
| `ApplyAll()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:379` | 先把 `MarkdownStream.SetFrameSource(top)` 交给每条流式条目（第 389 行）；用 `SnapshotDiff.Compute` 把差异翻译成行的增删改；`latest` 与 `RefreshedContext` 都没变且首行有块时整条跳过（第 392–395 行） |
| `SnapshotReady` → `RequestFrame` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:332,334` | 后台快照通知经 `Dispatcher.UIThread.Post` + `TopLevel.RequestAnimationFrame` 合并，每帧最多应用一次 |
| `CaptureAnchor()` / `RestoreAnchor()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:463,480` | 记下视口内第一行及其相对视口位置（`PendingAnchor`，第 89 行），行结构变化后把它挪回原位 |
| `ParseVisibleDeferred(double)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:503` | 视口 ±600px 内尚未解析的延迟条目就地在界面线程解析并展开，最多 8 轮 |
| `EstimateText(string)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:532` | 延迟条目的占位行高：按每行字符数（宽度 / 字号 × 0.62）与空行估算 |
| `Estimate(MarkdownBlock, RenderContext)` / `Estimate(Row, RenderContext)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:702,689` | 未测量行与分段行的粗略高度（分段行把代码块头部 34 / 底部 14 拆给首段与末段） |
| `ChunkCount(block, context)` / `ApplyChunk` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:642,660` | 超长代码块（> 200 行，`ChunkThresholdLines` 第 638 行）按 128 行（`LinesPerChunk` 第 640 行）拆成多行；**折叠的代码块不分段**（`CodeBlockView.ShouldCollapse`） |
| `UpdateBlockRows(state, index, rowStart, block, context)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:557` | 一个顶层块的行原地更新；分段数变化时增删末尾的行（`DiffKind.Update` 与代码块展开/折叠共用） |
| `OnCodeExpandedChanged` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:595` | 监听 `CodeBlockView.ExpandedChangedEvent`（构造里注册，第 217 行）：按新的分段数重建该块的行 |
| `MarkTopsDirty(Row?)` / `EnsureTops()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:757,764` | 只从第一处变化的行（`_topsDirtyFrom`）重算 `Top` 与总高度；行结构已变或 `Index` 过期时从头算 |
| `EnsureRows()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:724` | 重建 `_rows` 扁平列表并刷新 `Row.Index` |
| `FirstRowAt(double)` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:788` | 在已排序的 `Top` 上二分，取第一个底边越过给定 y 的行 |
| `Realize()` / `Unrealize()` / `BuildContent()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:807,846,834` | 实例化一行（块视图经 `BlockViewFactory.Create`）、回收、宿主内容控件或数据模板构建 |
| `MeasureOverride` / `ArrangeOverride` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:858,972` | 宽度变化时作废全部实测高度；`RunPasses`（第 883 行）最多 3 轮"实例化 → 测量 → 更新 Top → 恢复锚点"；随后按 `_scrollTarget` 做字符级精定位（第 946–955 行）；按 `MaxContentWidth` 居中排布 |
| `UpdateTail()` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:984` | 只有最后一个流式条目的最后一段被标为 `IsDocumentTail`（渐显与光标，见 `Rendering/TextSegmentView.cs:58`） |
| `RowFor` / `TargetY` (private) | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:126,158` | 选区坐标 → 目标行（分段代码按偏移所在行选段）与目标在面板坐标中的 y（`segment.GetCharacterBounds` + `TranslatePoint`） |
| `OnLinkClicked` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:190` | 只处理 `#` 开头的文内链接：在**包含被点击视图的那条消息**里用 `DocumentNavigation.ResolveAnchor` 解析，命中就 `ScrollToPoint` 并标记已处理 |
| `OnCreateAutomationPeer` | `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:1117` | 返回 `Automation.MarkdownDocumentAutomationPeer`（`src/StreamMark.Avalonia/Automation/MarkdownAutomationPeers.cs:60`，类型为 `AutomationControlType.Document`） |

## Dependencies
- Uses: `core-streaming` via `StreamingDocument.Latest` / `SnapshotReady` / `LoadNow` / `GetText` — `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:391`（`markdown.Document.Latest`）、`TranscriptEntry.cs:56`（`Document.LoadNow`）、`TranscriptPanel.cs:1151`（`GetText`）
- Uses: `core-streaming` via `SnapshotDiff.Compute` 的 `Insert/Update/Remove` 语义（`Index` 是"正在变换的列表"中的位置） — `src/StreamMark.Core/Streaming/SnapshotDiff.cs:18`，调用点 `TranscriptPanel.cs:413`
- Uses: `avalonia-text-rendering` via `BlockViewFactory.Create` / `CanReuse`、`BlockView.Update` / `Segments` / `LastSegment` / `CurrentContext`、`RenderContext`、`MarkdownStyle.Resolve` — `TranscriptPanel.cs:671,679,822,361`
- Uses: `avalonia-text-rendering` via 块状态存储（`RenderContext.State`，每消息一份 `EntryState.State`）与 `CodeBlockView` 的分段/折叠契约（`SetChunk`、`ShouldCollapse`、`ExpandedChangedEvent`） — `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:41,645,664,217`
- Uses: `avalonia-selection` via `ISelectionHost` 的成员与 `SelectionController`、`SearchModel`、`DocumentNavigation`、`MarkdownContextMenu`、`TextPoint` / `SegmentKey` — 接口定义 `src/StreamMark.Avalonia/Selection/SelectionController.cs:13`，实现 `TranscriptPanel.cs:1113-1163`；`Search` 第 1120 行、`GetOutline` 第 176 行、菜单安装第 216 行
- Uses: `avalonia-extensions` via `MarkdownServices.GetServices` / `GetImageBaseUri` 进入 `RenderContext` — `src/StreamMark.Avalonia/Transcript/TranscriptPanel.cs:359-370`
- Uses: `MarkdownStream`（流式条目的节奏控制器） — `src/StreamMark.Avalonia/Transcript/TranscriptEntry.cs:20`，帧来源下发 `TranscriptPanel.cs:389`；`SetFrameSource` 是 `internal`（`src/StreamMark.Avalonia/MarkdownStream.cs:48`）
- Used by: `samples` — `samples/StreamMark.Gallery/Program.cs:87-89`（`TranscriptView` + `ContentEntry` + `MarkdownEntry.FromText`）、`:119-120`（流式 + `ScrollToEnd`）；冒烟检查读 `window.Transcript.Panel`（`samples/StreamMark.Gallery/SmokeTest.cs:90,109`）
- Used by: `bench` — `bench/StreamMark.CpuHarness/Program.cs:108,113-114,213-215`（历史消息用 `FromTextDeferred`）
- Used by: `tests` via `TranscriptView` / `TranscriptPanel` 的公开成员（`Entries`、`Panel.ApplyLatest`、`RowCount`、`RealizedCount`、`IsPinnedToBottom`、`Offset`、`Extent`、`Viewport`、`Search`、`GetOutline`、`ScrollToPoint`） — `tests/StreamMark.Avalonia.Tests/Transcript/TranscriptViewTests.cs:43-68`、`tests/StreamMark.Avalonia.Tests/Rendering/InteractionTests.cs:58-64`

## State it owns
- 行模型：`_rows` 是所有条目 `EntryState.Rows` 的扁平只读视图（`EnsureRows`，`TranscriptPanel.cs:724-754`）。每行一个 `Row`；Markdown 的每个顶层块至少一行（`ApplyAll` 按 `SnapshotDiff` 的 Insert/Update/Remove 增删行，第 420–450 行），超长代码块拆成 `ChunkCount` 行；`ContentEntry` 恒为一行（第 734–740 行）；未解析的 `MarkdownEntry` 是一行占位（`Block == null`，第 741–745 行）。
- 行高：未实测的行用估算（`Row.Height`，Insert 时 `Estimate`，第 428 行；`ContentEntry` 固定 60，第 738 行；延迟条目 `EstimateText`，第 744 行），实例化后由 `view.DesiredSize.Height` 覆盖（第 910–917 行）。真实高度是权威值，估算只在"从未进过视口"的行上存在。
- 派生副本：`Row.Top` 与 `_extent`（`EnsureTops` 第 764–786 行，**从 `_topsDirtyFrom` 起增量重算**）、`_viewport`（第 959 行）、`EntryState.Slots`（快照块 → 行数）、`EntryState.Applied`（= 上次应用的 `Document.Latest`）、`EntryState.RefreshedContext`（上次刷新已实例化视图用的上下文，用于整条跳过）、`EntryState.Context`（`RenderContext.Entry` 是消息序号，会随顺序变化作废重建）。
- 虚拟化窗口：`Overscan = 600`（第 59 行），实例化区间 `[offset - 600, offset + height + 600]`（第 891–892 行）；回收 = `_realized` 中本轮未保留的行（第 919–930 行），复用 `_keep` / `_unrealize` 两个集合避免每轮分配。
- 滚动锚定状态：`_pendingAnchor`（`PendingAnchor(Row, Delta, EntryDelta)`，第 89 行），由 `CaptureAnchor` 记录、`RestoreAnchor` 消费；用户直接改变 `Offset` 或 `SetOffsetInternal` 时清空（第 1059、1100 行）。
- 跳转目标：`_scrollTarget`（第 92 行），由 `ScrollToPoint` 设置，`MeasureOverride` 在目标行实例化后按字符实际位置消费（第 946–955 行；**只在 `TargetY` 命中时清除**）。
- 贴底状态：`_pinned`（初始 `true`，第 81 行）。`Offset` setter 里 `y >= max - 2` 即视为贴底（第 1058 行）；`ScrollToEnd()` 只置位（第 1020 行）。
- 其它：`_offset`、`_width`、`_rowsDirty` / `_topsDirty` + `_topsDirtyFrom`、`_lastScrollInfo`（`(extent, viewport, offset)` 不变时不发 `ScrollInvalidated`，第 962–968 行）、`_frameRequested`、`_settingOffset`（防止 `ScrollInvalidated` 回写重入）、`_tail`（当前渐显尾段）、`Selection` 与 `_selection`、惰性 `_search`。

## Contracts it exposes
- `TranscriptView.Entries`：`ObservableCollection<TranscriptEntry>`，可直接 `Add` / `Insert(0, …)` / `Remove`（测试中就这么用，`DeferredEntryTests.cs:82`）。面板监听 `CollectionChanged`（`SetEntries` 第 263–276 行），并把集合按 `IReadOnlyList` 保存；条目按**引用**身份复用状态（`TranscriptEntry` 未重写 `Equals`），因此同一个条目对象从集合里移除再放回会丢失 `EntryState`（挂起的快照订阅被退订、块状态清空）。
- `MarkdownEntry`：`Document` + 可选 `State`（流式渐显）+ 可选 `Stream`（节奏控制器）+ `Tag`（宿主数据）。构造后应把快照交给面板——面板自己订阅 `Document.SnapshotReady`（`SyncEntries` 第 297 行），宿主不需要手动通知；流式条目的**帧来源由显示它的视图设置**，宿主不需要（也不该）调用 `SetFrameSource`。
- 延迟条目（deferred）：`MarkdownEntry.FromTextDeferred(text, profile, tag)` 只保存文本，`IsParsed == false`、`DeferredText` 为原文；`EnsureParsed()` 用 `Interlocked.Exchange` 保证只解析一次（`TranscriptEntry.cs:52-58`）；解析走 `StreamingDocument.LoadNow`（同步、界面线程、要求文档无挂起工作，`StreamingDocument.cs:126`）。约定：语义等价于 `FromText`，只是解析时机推迟到"第一次需要它的行、或选区/复制/查找/跳转跨过它"；解析前按字符数与换行估算高度，解析后占位行被真实行替换（`ApplyAll` 第 396–404 行）。
- `ContentEntry(object content)`：`content` 是 `Control` 就直接用，否则用 `this.FindDataTemplate(content).Build(content)`，都没有时退化为 `TextBlock{Text = content.ToString()}`（`BuildContent` 第 834–844 行）。整条内容占一行，不参与分段。
- 导航：`GetOutline()` 返回**已解析**条目的标题（`DocumentNavigation.Outline`，`Selection/DocumentNavigation.cs:13`），按需条目未解析时不在其中——这是刻意的取舍，避免一次解析整个历史（`TranscriptPanel.cs:175-187`）。`ScrollToPoint(point)` 先对该条目 `EnsureParsed()` + `ApplyAll` + `EnsureRows` + `EnsureTops`，把 `_offset` 按 `row.Top - 视口高 × 0.3` 估算置位、清锚点、解除贴底，再在测量里按 `TargetY`（片段字符实际位置）精确定位（第 95–120、946–955 行）。文内 `#` 锚点链接（含 `#fn:` 脚注）由面板自己消费（`OnLinkClicked` 第 190 行），其余链接仍冒泡给宿主。
- 查找：`Search`（`SearchModel`，`Selection/SearchModel.cs`）在**块模型**上按片段搜索，结果用选区坐标表示并交给 `SelectionModel.SetHighlights` 画高亮；`Next` / `Previous` / `MoveTo` 经 `ISelectionHost.ScrollToPoint` 跳转（`SearchModel.cs:81`）。注意 `Find` 遍历所有条目并逐个 `GetSnapshot`，因此**会把尚未解析的按需条目全部解析**（与 `GetOutline` 的取舍相反）。
- 面板的 `ILogicalScrollable` 对外行为：`IsLogicalScrollEnabled => true`；`ScrollSize = (0, 48)`（每行 48px）、`PageScrollSize = (0, max(48, viewport.Height - 48))`；`Extent` / `Viewport` 在测量阶段更新；写 `Offset` 会被夹到 `[0, extent - viewport]`，相等（< 0.01）时直接返回，否则同时更新 `_pinned`、清锚点、请求测量；`_settingOffset` 期间忽略回写（第 1042–1062 行）。`BringIntoView` 只在**已实例化**的行里查找目标控件并滚动，未命中返回 `false`（第 1066–1089 行）；`GetControlInDirection` 恒返回 `null`（无键盘逐行导航，第 1091 行）。`ScrollInvalidated` 只在 `(extent, viewport, offset)` 真的变化时触发（第 962–968 行），`SetOffsetInternal` 后额外触发一次（第 1105 行）。
- 选区宿主：`ISelectionHost.HostControl = this`、`EntryCount = _entries.Count`、`RealizedSegments()` 只枚举 `_rows` 中已实例化的 `BlockView`（第 1126–1127 行）——所以被回收的块不在命中集里，但 `GetSnapshot` / `GetSource` 仍能从模型给出文本；跨到未解析条目时 `GetSnapshot` 会就地 `EnsureParsed()` 并请求下一帧（第 1133–1148 行）。`ISelectionHost` 新增的 `ScrollToPoint` 有默认空实现（`SelectionController.cs:37`），面板显式实现（第 1115 行）。
- 无障碍：面板的 automation peer 是 `AutomationControlType.Document`（第 1117 行），名称取自宿主的 `AutomationProperties.Name`。

## How to extend
- To add a 新的宿主条目类型（例如"工具调用卡片"）：先在 `TranscriptEntry.cs` 加 `TranscriptEntry` 子类（照 `ContentEntry`，`TranscriptEntry.cs:72`），再在 `TranscriptPanel.EnsureRows()` 给它建行并给初始高度（`TranscriptPanel.cs:724-747`，现有分支只认 `ContentEntry` 与未解析的 `MarkdownEntry`），最后在 `Realize()` 里构建控件（`TranscriptPanel.cs:807-832`），并在 `Unrealize()` 里决定是否像 `ContentView` 那样缓存控件以保住内部状态（`TranscriptPanel.cs:846-856`）。若新条目自带文档（像 `MarkdownEntry`），还要在 `SyncEntries()` 订阅/退订 `SnapshotReady`（`TranscriptPanel.cs:295-307`）并在 `ApplyAll()` 里跳过非文档条目（`TranscriptPanel.cs:385`）。**漏掉 `EnsureRows` 的那一步**的表现是：条目进了 `Entries`、`_entries` 里也有状态，但它 `state.Rows.Count == 0`，于是 `_rows` 里没有任何一行——面板既不显示它，`RowCount` 也不含它（`_rows.AddRange(state.Rows)`，第 746 行）。
- To add 一种影响行高的块类型（估算与实测必须同步的三处）：① `Estimate(MarkdownBlock, RenderContext)` 的 `switch` 加分支，用内容长度 / 行数乘上 `MarkdownStyle` 的行高（`TranscriptPanel.cs:702-722`）；② 若该块视图把高度放在**自身 Measure 之外**的装饰上（像 `CodeBlockView` 的 `HeaderHeight = 34` / `Padding = 14`，`CodeBlockView.cs:24-25`），还要同步 `Estimate(Row, context)` 里对应的 `34` / `14`（`TranscriptPanel.cs:689-697`）——这两个魔数是 `CodeBlockView.MeasureOverride`（`CodeBlockView.cs:296`）的重复；③ 若新块类型也要分段，改 `ChunkCount`（`TranscriptPanel.cs:642`，目前只认 `CodeBlock`）并在 `ApplyChunk`（`TranscriptPanel.cs:660`，目前只 `SetChunk` 到 `CodeBlockView`）里下发窗口，否则每段都会自己渲染整块。实测一侧不需要在面板里加代码：行的真实高度永远来自 `view.DesiredSize.Height`（`TranscriptPanel.cs:910-917`）。
- To add 一种"延迟加载"的宿主条目：只要求 `EnsureRows` 产出占位行、并在该行被实例化前用 `ParseVisibleDeferred` 的同类机制替换（`TranscriptPanel.cs:503-529` 是模板，注意 8 轮上限与外层 `ApplyAll` + `EnsureRows` + `EnsureTops` + `RestoreAnchor` 的组合）。
- To add 一种"会改变分段数的块级状态"（代码块折叠/展开是现有唯一一例）：在块视图里把新状态写进 `RenderContext.State` 后 `RaiseEvent` 一个路由事件（照 `CodeBlockView.ToggleExpanded` / `ExpandedChangedEvent`，`CodeBlockView.cs:115,137-146`），面板侧 `AddHandler`（构造里，`TranscriptPanel.cs:217`）→ `CaptureAnchor` → `UpdateBlockRows` → `InvalidateMeasure`（`OnCodeExpandedChanged`，第 595–621 行）。**不要**让视图自己改行结构或 `Row.Height`：面板的 `_rows` / `Slots` 会与视图脱节。

## Invariants and traps
- **变高行不能交给 `VirtualizingStackPanel`** — 它假设项高一致，变高项会让滚动条在测量时跳动；本模块因此自己实现 `ILogicalScrollable`，用自算的 `Extent`/`Offset` 驱动宿主的 `ScrollViewer`（`TranscriptPanel.cs:29`、`EnsureTops` 第 764 行、`Extent` 第 1038 行） — 决策记录见 `docs/decisions.md:12`；`TranscriptView` 只是把面板当作 `ScrollViewer.Content`（`TranscriptView.cs:17-21`）。
- **`Top` 的重算必须经 `MarkTopsDirty`** — 它把 `_topsDirtyFrom` 取"当前行索引与已有起点中的较小者"（第 757–762 行），`EnsureTops` 只从那一行往后累加（第 771–783 行）；直接置 `_topsDirty = true` 会让流式追加退回 O(行数) 全量重算，而随手传一个 `Index` 已过期的行会从错误位置起算——`MarkTopsDirty` 里的 `!ReferenceEquals(_rows[row.Index], row)` 守卫就是为此。
- **视口上方插入内容必须保持可见内容不动** — `SyncEntries` 先 `CaptureAnchor()` 再重建（`TranscriptPanel.cs:283`），`MeasureOverride` 每轮结束后按锚点回写 `_offset`（第 937–940 行）；`RestoreAnchor` 分两种情形：锚点行还在原位（第 491 行）或锚点行已被替换成真实行（延迟条目展开，第 495 行，改用条目顶部）。测试：`DeferredEntryTests.InsertAboveViewport_KeepsVisibleContentInPlace`（`tests/StreamMark.Avalonia.Tests/Transcript/DeferredEntryTests.cs:57`）。注意锚点只在**非贴底**时记录（`CaptureAnchor` 第 465 行提前返回），贴底时靠 `_offset = extent - height` 重新贴底。
- **行高估算错误会表现为跳变，陈旧行高会表现为重叠** — 估算只影响从未进过视口的行；一旦该行进入 ±600px 就会实测并改写 `Row.Height`，`_topsDirty` 使 `EnsureTops` 重算 `Top` 与 `Extent`，滚动条拇指长度随之变化（用户看到的是"滚过一段后滚动条跳一下"）。因此宽度变化时必须作废全部实测高度（第 863–871 行）。反向的陷阱是"实测有效但记录过期"：宿主内容控件自行展开/折叠时布局管理器先单独重测它，此时 `view.IsMeasureValid` 为真、`row.Measured` 也为真，所以代码**总是**拿 `DesiredSize.Height` 与 `row.Height` 比较（第 910–917 行，注释在第 910–911 行）——漏掉这一步的表现是后续行与展开内容重叠。测试：`TranscriptViewTests.ContentEntry_That_Grows_PushesFollowingRowsDown`（`TranscriptViewTests.cs:142`）。另一处同步点是估算用的 `_width` 在第一次测量前是 720 的兜底值（第 535、705 行）。
- **流式节奏跟着显示帧走** — `ApplyAll` 每次都会 `markdown.Stream?.SetFrameSource(top)`（`TranscriptPanel.cs:389`，`MarkdownView.cs:168` 是单条消息的对应点），此后 `MarkdownStream` 用 `TopLevel.RequestAnimationFrame` 出帧、每帧最多放出一次文本；窗口不出帧（最小化 / 无头未驱动）时退化为 `FrameFallback = 100ms` 的计时器，保证流总会结束（`MarkdownStream.cs:29,135-152`）。所以"无头测试里流不前进"通常是没有 `HeadlessApp.Settle()` 推帧，而不是节奏器坏了。
- **代码块折叠会改变分段数，必须走面板的重建路径** — `ChunkCount` 对折叠的代码块返回 1（`TranscriptPanel.cs:645`），展开后 `OnCodeExpandedChanged` 重建该块的行；测试 `ChatFeatureTests.Transcript_CollapsedLongCode_IsOneRow_ExpandRechunks`（`tests/StreamMark.Avalonia.Tests/Rendering/ChatFeatureTests.cs:154`，600 行 → 折叠 1 行、展开 5 段）。流式输出中的代码块不折叠（`CodeBlockView.ShouldCollapse` 要求已完成，`CodeBlockOptions.CollapseAfterLines` 默认 40，`Extensions/CodeBlockOptions.cs:12`）。
- **块状态在分段与视图回收后重建仍保留** — `CodeBlockState.For(context, topKey, language)` 把状态写进 `RenderContext.State`（`Rendering/CodeBlockState.cs:48`），而这个存储是 `EntryState.State`，每条消息一份、随 `EntryState` 一起跨行增删存活（`TranscriptPanel.cs:41`）。键是 `(顶层块 key, 状态名)`，源码只追加所以 key 稳定（`Plugins/BlockStateStore.cs:3-8`）。因此：代码块的换行开关 `code.wrap`（写 `CodeBlockView.cs:68`、读 `CodeBlockView.cs:200`）与展开状态 `code.expanded`（`CodeBlockView.cs:27,130,143`）在分段视图之间共享；`CodeTextView.OnAttachedToVisualTree` 会在重新挂载时 `ApplyWindow()` 取回回收期间产生的着色结果（`CodeBlockView.cs:769-781`）。反例警告：块状态属于 `EntryState`，把条目对象从 `Entries` 移除再放回去会得到新的 `EntryState`，状态（展开/折叠、换行）随之丢失。
- 分段代码块的**相邻分段之间不额外加间距**，块与块的间距也只在同一消息内用 `BlockSpacing`、跨消息用 `EntrySpacing`（`EnsureTops` 第 773–783 行）。分段序号变化（`DiffKind.Update`）时只增删末尾的行、其余行原地更新，但即使段数不变也会 `MarkTopsDirty`（`UpdateBlockRows` 第 591 行），因为块内容变化可能改变行高。
- 只有**最后一个** `MarkdownEntry { State: not null }` 的最后一段被标记为文档尾（`UpdateTail` 第 984–1011 行），流式光标与渐显只在那里渲染（`Rendering/TextSegmentView.cs:58,591`）；若该尾行恰好被回收，就没有光标。流结束（`latest.IsComplete`）时面板会单独 `_tail?.InvalidateVisual()`，因为末尾片段的内容可能没变（`TranscriptPanel.cs:455-459`）。
- `RenderContext.Entry` 是"消息在 `_entries` 中的序号"，也是选区坐标的第一维；`SyncEntries` 一旦改变顺序就丢弃上下文、下一帧重建（`TranscriptPanel.cs:320-326`），`TextPoint` 的比较序（`Selection/TextPoint.cs:10-24`）依赖它。
- `ApplyAll` 会为**所有**条目的所有顶层块建 `Row`，只有控件是窗口化的（10 万块 = 10 万 `Row`，`VirtualizationScaleTests.cs:32`）；`EnsureTops` 一次是 O(从 `_topsDirtyFrom` 起的行数)，但行结构变化（增删行、宽度变化）时退化为全量。
- `MaxContentWidth` 只影响内容列的居中宽度，`Extent.Width` 始终是完整可用宽度（`ArrangeOverride` 第 972–981 行、`MeasureOverride` 第 959–960 行）。
- `SetEntries` 会退订旧的 `INotifyCollectionChanged` 并订阅新的（`TranscriptPanel.cs:263-276`）；传同一个集合两次是幂等的，传新集合会整体重同步。
- `_scrollTarget` 只在 `TargetY` 命中时清除（`TranscriptPanel.cs:946-955`）：目标行始终没被实例化（例如估算高度与真实高度差距极大）时，这个跳转目标会一直留到下一次测量。

## Tests
- 位置：`tests/StreamMark.Avalonia.Tests/Transcript/`，5 个文件：`TranscriptViewTests.cs`（贴底、流式、跨消息选区、宿主内容增高）、`VirtualizationScaleTests.cs`（10 万块 + 追加）、`DeferredEntryTests.cs`（延迟解析、视口上方插入）、`LongCodeTests.cs`（3000 行代码分 24 段、跨分段复制、跳转耗时）、`SmoothStreamWrapTests.cs`（平滑渐显不产生硬换行，回归）。
- 面板的其余行为在别的目录里测：导航与查找 `tests/StreamMark.Avalonia.Tests/Rendering/InteractionTests.cs:50,96`（目录/锚点/脚注跳转、跨条目查找 + 高亮 + 跳转）；折叠重建分段 `Rendering/ChatFeatureTests.cs:154,180`；diff 显示方式跨虚拟化存活 `Rendering/DiffRenderingTests.cs:118`；块状态跨回收存活 `Plugins/PluginSystemTests.cs:180`；宿主内容上的按下不开始选择 `Rendering/InteractionRegressionTests.cs:45`。
- 命名模式：类名 `<主题>Tests`，方法用"行为_期望"的句子（`LongTranscript_RealizesOnlyVisibleRows_AndStartsPinnedToBottom`）；测试体一律包在 `await HeadlessApp.Run(async () => { … })` 里在界面线程执行（`tests/StreamMark.Avalonia.Tests/HeadlessApp.cs:55`），用 `HeadlessApp.Settle()` 推进帧（`HeadlessApp.cs:65`，`ForceRenderTimerTick` 同时驱动 `RequestAnimationFrame`），`HeadlessApp.Save(window, "…")` 落截图（目录由 `STREAMMARK_SNAPSHOT_DIR` 决定，`HeadlessApp.cs:36-49`）；程序集级禁用了并行（`HeadlessApp.cs:8`）。
- 只跑一个文件/类：`dotnet test StreamMark.slnx --filter "FullyQualifiedName~Transcript.VirtualizationScaleTests"`（同理把类名换成 `DeferredEntryTests` / `LongCodeTests`）；整个模块 `dotnet test StreamMark.slnx --filter "FullyQualifiedName~Tests.Transcript"`。仓库的既有命令是 `dotnet test StreamMark.slnx`（`README.md:91`、`.github/workflows/ci.yml:76`）。
- 性能断言以 CI 环境变量分层（`LongCodeTests.cs:52-56`：中位数 + 宽松最大值），机器繁忙不该让测试红。

## Related flows
- [transcript-virtualization-scroll](../flows/transcript-virtualization-scroll.md)
- [streaming-token-to-view](../flows/streaming-token-to-view.md)
- [text-selection-copy](../flows/text-selection-copy.md)
