using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Model;
using StreamMark.Core.Streaming;

namespace StreamMark.Avalonia;

/// <summary>
/// 渲染单个 Markdown 文档（非虚拟化，高度随内容增长）。设置 <see cref="Markdown"/> 显示静态文本，
/// 或设置 <see cref="Stream"/> / <see cref="Document"/> 显示流式文档。长会话请使用 TranscriptView。
/// </summary>
public class MarkdownView : Control, ISelectionHost
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<StreamingDocument?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownView, StreamingDocument?>(nameof(Document));

    public static readonly StyledProperty<MarkdownStream?> StreamProperty =
        AvaloniaProperty.Register<MarkdownView, MarkdownStream?>(nameof(Stream));

    /// <summary>链接点击（来自内部任一文本片段）。</summary>
    public static readonly RoutedEvent<LinkClickedEventArgs> LinkClickedEvent = TextSegmentView.LinkClickedEvent;

    private readonly DocumentPresenter _presenter;
    private readonly SelectionController _selectionController;
    private StreamingDocument? _subscribed;
    private StreamingDocument? _ownDocument;
    private RenderContext? _context;
    private bool _frameRequested;
    private readonly Plugins.BlockStateStore _state = new();

    static MarkdownView()
    {
        FocusableProperty.OverrideDefaultValue<MarkdownView>(true);
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        DocumentProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        StreamProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.OnSourceChanged());
        MarkdownServices.ServicesProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.InvalidateContext());
        MarkdownServices.ImageBaseUriProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.InvalidateContext());
    }

    public MarkdownView()
    {
        _presenter = new DocumentPresenter(
            (index, view) =>
            {
                LogicalChildren.Insert(index, view);
                VisualChildren.Insert(index, view);
                InvalidateMeasure();
            },
            (_, view) =>
            {
                LogicalChildren.Remove(view);
                VisualChildren.Remove(view);
                InvalidateMeasure();
            });
        _selectionController = new SelectionController(this);
        MarkdownContextMenu.Attach(this, _selectionController);
        AddHandler(TextSegmentView.LinkClickedEvent, OnAnchorLinkClicked);
        ResourcesChanged += (_, _) => InvalidateContext();
        ActualThemeVariantChanged += (_, _) => InvalidateContext();
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public StreamingDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public MarkdownStream? Stream
    {
        get => GetValue(StreamProperty);
        set => SetValue(StreamProperty, value);
    }

    public SelectionModel Selection { get; } = new();

    public SelectionController SelectionController => _selectionController;

    /// <summary>当前显示的快照。</summary>
    public DocumentSnapshot? Snapshot => _presenter.Applied;

    public event EventHandler<LinkClickedEventArgs>? LinkClicked
    {
        add => AddHandler(LinkClickedEvent, value);
        remove => RemoveHandler(LinkClickedEvent, value);
    }

    private StreamingDocument? EffectiveDocument => Stream?.Document ?? Document ?? _ownDocument;

    /// <summary>
    /// 视图里文档的标识：每次设置 <see cref="Markdown"/>、<see cref="Document"/> 或 <see cref="Stream"/> 都会递增
    /// （这些赋值会清空选区与块状态，相当于换了一个文档）。源码位置与 <see cref="ReadingAnchor"/> 只在同一个标识内有意义。
    /// 外部直接对已绑定的 <see cref="StreamingDocument"/> 调用 <c>Reset</c> 不会改变它，需要宿主重新赋值。
    /// </summary>
    public int DocumentId { get; private set; }

    private void OnSourceChanged()
    {
        DocumentId++;
        if (Stream is null && Document is null)
        {
            _ownDocument ??= new StreamingDocument(MarkdownServices.GetServices(this).Profile);
            _ownDocument.Load(Markdown ?? "");
        }
        else
        {
            _ownDocument = null;
        }
        Subscribe();
        _context = null;
        _presenter.Clear();
        _state.Clear();
        Selection.Clear();
        RequestFrame();
    }

    private void Subscribe()
    {
        var document = VisualRoot is null ? null : EffectiveDocument;
        if (ReferenceEquals(document, _subscribed))
        {
            return;
        }
        if (_subscribed is not null)
        {
            _subscribed.SnapshotReady -= OnSnapshotReady;
        }
        _subscribed = document;
        if (document is not null)
        {
            document.SnapshotReady += OnSnapshotReady;
        }
    }

    private void OnSnapshotReady(DocumentSnapshot snapshot) => Dispatcher.UIThread.Post(RequestFrame, DispatcherPriority.Render);

    private void RequestFrame()
    {
        if (_frameRequested)
        {
            return;
        }
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            ApplyLatest();
        });
    }

    /// <summary>立即应用最新快照（测试或需要同步刷新时使用）。</summary>
    public void ApplyLatest()
    {
        if (EffectiveDocument is not { } document)
        {
            return;
        }
        _context ??= CreateContext();
        Stream?.SetFrameSource(TopLevel.GetTopLevel(this));
        _presenter.Apply(document.Latest, _context);
    }

    private RenderContext CreateContext() =>
        new(MarkdownStyle.Resolve(this), MarkdownServices.GetServices(this), Selection, 0, Stream?.State, _state, MarkdownServices.GetImageBaseUri(this));

    private void InvalidateContext()
    {
        _context = null;
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Stream is null && Document is null && _ownDocument is null)
        {
            OnSourceChanged();
        }
        Subscribe();
        _context = null;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe();
    }

    private double Spacing => _context?.Style.BlockSpacing ?? 12;

    void ISelectionHost.ScrollToPoint(TextPoint point) => ScrollToPoint(point);

    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Automation.MarkdownDocumentAutomationPeer(this);

    /// <summary>文档内查找。</summary>
    public SearchModel Search => _search ??= new SearchModel(this);

    private SearchModel? _search;

    /// <summary>文档的目录（标题，按阅读顺序）。</summary>
    public List<OutlineItem> GetOutline() => _presenter.Applied is { } snapshot ? DocumentNavigation.Outline(snapshot, 0) : [];

    /// <summary>让选区坐标所在位置进入可视区域（由外层滚动容器滚动，例如 <see cref="ScrollViewer"/>）。找不到目标时返回 false。</summary>
    public bool ScrollToPoint(TextPoint point)
    {
        var key = new SegmentKey(point.Entry, point.BlockKey, point.Segment);
        foreach (var view in _presenter.Views)
        {
            if (view.TopKey != point.BlockKey)
            {
                continue;
            }
            var segment = view.Segments().FirstOrDefault(s => s.Key == key);
            if (segment is null)
            {
                view.BringIntoView(new Rect(view.Bounds.Size).WithHeight(Math.Min(view.Bounds.Height, 40)));
                return true;
            }
            var rect = segment.GetCharacterBounds(point.Offset);
            // 目标上方多留一些空间，避免紧贴顶部
            var target = new Rect(rect.X, rect.Y - 80, Math.Max(1, rect.Width), rect.Height + 160);
            segment.View.BringIntoView(target);
            return true;
        }
        return false;
    }

    /// <summary>滚动到某个顶层块的开头。</summary>
    public bool ScrollToBlock(int blockKey) => ScrollToPoint(new TextPoint(0, blockKey, 0, 0));

    /// <summary>
    /// 滚动到源码偏移处（UTF-16 下标，行 / 列换算见 <see cref="SourceText"/>），块级精度：列表项、引用等容器里的段落也能命中，
    /// 但不会细分到块内的行。偏移是相对当前已应用快照的源码而言的，<see cref="DocumentId"/> 或文本变了以后请重新换算。
    /// 还没有应用快照（刚设置 <see cref="Markdown"/> 时）返回 <see cref="SourceNavigationStatus.NoDocument"/>，
    /// 目标块没有排版完成时返回 <see cref="SourceNavigationStatus.LayoutPending"/>，两种情况都没有滚动，等下一帧再试。
    /// </summary>
    public SourceNavigationResult ScrollToSourceOffset(int offset)
    {
        if (_presenter.Applied is not { Count: > 0 } snapshot)
        {
            return new SourceNavigationResult(SourceNavigationStatus.NoDocument);
        }
        if (DocumentNavigation.ResolveSourceOffset(snapshot, 0, offset) is not { } target)
        {
            return new SourceNavigationResult(SourceNavigationStatus.OutOfRange);
        }
        if (!IsLayoutReady || !_presenter.Views.Any(v => v.TopKey == target.Point.BlockKey))
        {
            return new SourceNavigationResult(SourceNavigationStatus.LayoutPending, target);
        }
        ScrollToPoint(target.Point);
        return new SourceNavigationResult(SourceNavigationStatus.Navigated, target);
    }

    /// <summary>渲染位置 → 源码区间，块级精度：返回承载该文本片段的最深的块的完整区间。快照没有对应的块时返回 null。</summary>
    public SourceRange? GetSourceRange(TextPoint point) =>
        _presenter.Applied is { } snapshot ? DocumentNavigation.SourceRangeOf(snapshot, point) : null;

    /// <summary>当前选区覆盖的源码区间（两端各扩展到完整的块）；没有选区时返回 null。</summary>
    public SourceRange? GetSelectionSourceRange()
    {
        if (Selection.Range is not { } range || _presenter.Applied is not { } snapshot
            || DocumentNavigation.SourceRangeOf(snapshot, range.Start) is not { } first
            || DocumentNavigation.SourceRangeOf(snapshot, range.End) is not { } last)
        {
            return null;
        }
        return new SourceRange(first.Start, Math.Max(first.End, last.End) - first.Start);
    }

    /// <summary>
    /// 记录当前阅读位置：外层 <see cref="ScrollViewer"/> 可视区域顶部的那个字符，以及它相对顶边的像素位移。
    /// 没有外层滚动容器或还没有内容时返回 null。
    /// </summary>
    public ReadingAnchor? CaptureAnchor()
    {
        if (_presenter.Applied is null || this.FindAncestorOfType<ScrollViewer>() is not { } scroll || scroll.TranslatePoint(default, this) is not { } viewport)
        {
            return null;
        }
        var top = viewport.Y;
        foreach (var view in _presenter.Views)
        {
            if (view.Bounds.Bottom <= top)
            {
                continue;
            }
            foreach (var segment in view.Segments())
            {
                if (segment.View.TranslatePoint(default, this) is not { } origin || origin.Y + segment.View.Bounds.Height <= top)
                {
                    continue;
                }
                var offset = segment.HitTest(new Point(0, Math.Max(0, top - origin.Y)));
                var rect = segment.GetCharacterBounds(offset);
                return new ReadingAnchor(DocumentId, view.TopKey, segment.Key.Segment, offset, top - (origin.Y + rect.Top));
            }
            return new ReadingAnchor(DocumentId, view.TopKey, -1, 0, top - view.Bounds.Top);
        }
        return null;
    }

    /// <summary>
    /// 把 <see cref="CaptureAnchor"/> 记录的位置滚回可视区域顶部。锚点来自另一个文档时不恢复；
    /// 锚点所在的块已不存在时滚动到它之后最近的块（<see cref="AnchorRestoreStatus.Approximate"/>）。
    /// 版面还没有稳定（刚改过宽度、字体或内容，需要等一次布局）时返回 <see cref="AnchorRestoreStatus.LayoutPending"/>。
    /// </summary>
    public AnchorRestoreStatus RestoreAnchor(ReadingAnchor anchor)
    {
        if (anchor.DocumentId != DocumentId)
        {
            return AnchorRestoreStatus.WrongDocument;
        }
        var views = _presenter.Views;
        if (_presenter.Applied is not { Count: > 0 } || views.Count == 0)
        {
            return AnchorRestoreStatus.NoDocument;
        }
        if (this.FindAncestorOfType<ScrollViewer>() is not { } scroll)
        {
            return AnchorRestoreStatus.NoViewport;
        }
        if (!IsLayoutReady || scroll.TranslatePoint(default, this) is not { } viewport)
        {
            return AnchorRestoreStatus.LayoutPending;
        }
        var exact = views.FirstOrDefault(v => v.TopKey == anchor.BlockKey);
        var view = exact ?? views.FirstOrDefault(v => v.TopKey >= anchor.BlockKey) ?? views[^1];
        var key = new SegmentKey(0, anchor.BlockKey, anchor.Segment);
        double target;
        if (exact is not null && anchor.Segment >= 0 && view.Segments().FirstOrDefault(s => s.Key == key) is { } segment
            && segment.View.TranslatePoint(default, this) is { } origin)
        {
            target = origin.Y + segment.GetCharacterBounds(anchor.Offset).Top + anchor.Displacement;
        }
        else
        {
            target = view.Bounds.Top + (exact is null ? 0 : anchor.Displacement);
        }
        var maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + target - viewport.Y, 0, maxOffset));
        return exact is null ? AnchorRestoreStatus.Approximate : AnchorRestoreStatus.Restored;
    }

    private bool IsLayoutReady => IsMeasureValid && IsArrangeValid && Bounds.Height > 0;

    // 文内锚点（#标题、#fn:脚注）在文档内跳转，找不到时交给宿主
    private void OnAnchorLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        if (e.Url.StartsWith('#') && _presenter.Applied is { } snapshot && DocumentNavigation.ResolveAnchor(snapshot, 0, e.Url) is { } target)
        {
            ScrollToPoint(target);
            e.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        var views = _presenter.Views;
        for (var i = 0; i < views.Count; i++)
        {
            views[i].Measure(new Size(availableSize.Width, double.PositiveInfinity));
            height += views[i].DesiredSize.Height + (i > 0 ? Spacing : 0);
            width = Math.Max(width, views[i].DesiredSize.Width);
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        var views = _presenter.Views;
        for (var i = 0; i < views.Count; i++)
        {
            if (i > 0)
            {
                y += Spacing;
            }
            views[i].Arrange(new Rect(0, y, finalSize.Width, views[i].DesiredSize.Height));
            y += views[i].DesiredSize.Height;
        }
        return finalSize;
    }

    public override void Render(global::Avalonia.Media.DrawingContext context)
    {
        // 透明背景：块与块之间的空白也能开始拖拽选择
        context.FillRectangle(global::Avalonia.Media.Brushes.Transparent, new Rect(Bounds.Size));
    }

    Control ISelectionHost.HostControl => this;

    IEnumerable<ISelectableSegment> ISelectionHost.RealizedSegments() => _presenter.Views.SelectMany(v => v.Segments());

    int ISelectionHost.EntryCount => 1;

    Plugins.PluginRegistry? ISelectionHost.Plugins => MarkdownServices.GetServices(this).Registry;

    DocumentSnapshot? ISelectionHost.GetSnapshot(int entry) => entry == 0 ? _presenter.Applied : null;

    string ISelectionHost.GetSource(int entry, int start, int length) => EffectiveDocument?.GetText(start, length) ?? "";
}
