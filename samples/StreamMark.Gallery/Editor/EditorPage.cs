using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Selection;
using StreamMark.Core.Streaming;
using TextMateSharp.Grammars;
using DocumentSnapshot = StreamMark.Core.Streaming.DocumentSnapshot;

namespace StreamMark.Gallery.Editor;

/// <summary>
/// Markdown 编辑器页：左侧大纲、中间 AvaloniaEdit 源码编辑器、右侧 StreamMark 实时预览，底部状态栏。
/// 预览持有一个 <see cref="StreamingDocument"/>，每次修改后防抖重新加载：内容没变的块不会重建。
/// </summary>
public sealed class EditorPage : DockPanel
{
    public static readonly IReadOnlyList<(string File, string Title)> SampleList =
    [
        ("welcome.md", "欢迎"),
        ("basics.md", "基础语法"),
        ("gfm.md", "GFM 扩展"),
        ("math.md", "数学公式"),
        ("diagrams.md", "Mermaid 图表"),
        ("code.md", "代码与差异"),
        ("html.md", "HTML 与组件"),
        ("showcase.md", "综合演示"),
    ];

    private static readonly (string Label, int CharsPerSecond)[] ReplaySpeeds =
    [
        ("慢速", 60), ("正常", 200), ("快速", 600), ("极快", 2400),
    ];

    private readonly MarkdownServices _services;
    private readonly GallerySettings _settings;
    private StreamingDocument _document;
    private readonly DispatcherTimer _debounce;
    private readonly RegistryOptions _textMateRegistry;
    private readonly TextMate.Installation _textMate;
    private readonly Grid _body;
    private readonly ListBox _outline;
    private readonly ScrollViewer _previewScroll;
    private readonly Border _previewContent;
    private readonly Border _findBar;
    private readonly TextBox _findBox;
    private readonly TextBlock _findCount;
    private readonly TextBlock _statusPosition = StatusText();
    private readonly TextBlock _statusWords = StatusText();
    private readonly TextBlock _statusBlocks = StatusText();
    private readonly TextBlock _statusFile = StatusText();
    private readonly Button _replayButton;
    private readonly ComboBox _replaySpeed;
    private readonly Dictionary<ViewMode, ToggleButton> _viewButtons = [];
    private string? _filePath;
    private string _untitledName = "未命名.md";
    private bool _crlf;
    private bool _dirty;
    private long _loadStarted;
    private double _parseMs;
    private bool _syncPending;
    private bool _updatingOutline;
    private CancellationTokenSource? _replay;
    private UndoStack? _undoStack;

    public EditorPage(MarkdownServices services, GallerySettings settings)
    {
        _services = services;
        _settings = settings;
        _document = new StreamingDocument(services.Profile);
        _document.SnapshotReady += OnSnapshotReady;

        // —— 源码编辑器 ——
        Editor = new TextEditor
        {
            ShowLineNumbers = settings.ShowLineNumbers,
            WordWrap = settings.WordWrap,
            FontSize = settings.EditorFontSize,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, SF Mono, Menlo, JetBrains Mono, DejaVu Sans Mono, monospace"),
            Padding = new Thickness(8, 8, 4, 8),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.AllowScrollBelowDocument = true;
        _textMateRegistry = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = Editor.InstallTextMate(_textMateRegistry);
        _textMate.SetGrammar(_textMateRegistry.GetScopeByLanguageId("markdown"));
        Editor.TextChanged += (_, _) => OnTextChanged();
        Editor.DocumentChanged += (_, _) => AttachUndoStack();
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => RequestScrollSync();
        Editor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

        // —— 预览 ——
        Preview = new MarkdownView { Document = _document, Margin = new Thickness(0, 0, 0, 48) };
        Preview.LinkClicked += OnLinkClicked;
        Preview.AddHandler(MarkdownContextMenu.FindRequestedEvent, (_, _) => ShowFindBar());
        _previewContent = new Border { Padding = new Thickness(32, 24), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Stretch, Child = Preview };
        _previewScroll = new ScrollViewer
        {
            Content = _previewContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        (_findBar, _findBox, _findCount) = CreateFindBar();
        var previewPane = new DockPanel();
        DockPanel.SetDock(_findBar, Dock.Top);
        previewPane.Children.Add(_findBar);
        previewPane.Children.Add(_previewScroll);
        previewPane.Bind(BackgroundProperty, previewPane.GetResourceObservable("Gallery.WindowBackground"));

        // —— 大纲 ——
        _outline = new ListBox { Background = Brushes.Transparent, Padding = new Thickness(4) };
        _outline.SelectionChanged += (_, _) =>
        {
            if (!_updatingOutline && _outline.SelectedItem is ListBoxItem { Tag: OutlineItem item })
            {
                JumpTo(item);
            }
        };
        var outlineTitle = new TextBlock { Text = "大纲", FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 4) };
        var outlinePane = new DockPanel();
        DockPanel.SetDock(outlineTitle, Dock.Top);
        outlinePane.Children.Add(outlineTitle);
        outlinePane.Children.Add(_outline);
        outlinePane.Bind(BackgroundProperty, outlinePane.GetResourceObservable("Gallery.PaneBackground"));

        var editorPane = new Border { Child = Editor };
        editorPane.Bind(Border.BackgroundProperty, editorPane.GetResourceObservable("Gallery.PaneBackground"));

        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("220,Auto,*,Auto,*") };
        AddColumn(outlinePane, 0);
        AddColumn(Splitter(), 1);
        AddColumn(editorPane, 2);
        AddColumn(Splitter(), 3);
        AddColumn(previewPane, 4);

        // —— 工具栏与状态栏 ——
        (_replayButton, _replaySpeed) = (new Button(), new ComboBox());
        var toolbar = CreateToolbar();
        var status = CreateStatusBar();
        SetDock(toolbar, Dock.Top);
        SetDock(status, Dock.Bottom);
        Children.Add(toolbar);
        Children.Add(status);
        Children.Add(_body);

        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) => Reparse());
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        ApplyEditorTheme();
        ApplyViewMode(settings.ViewMode);
        ApplyOutlineVisibility();
        ApplyZoom();
        AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Tunnel);

        LoadText(Program.Sample("welcome.md"), path: null, untitledName: "欢迎.md");
    }

    public TextEditor Editor { get; }

    public MarkdownView Preview { get; }

    /// <summary>预览当前使用的文档（实时预览或流式回放）。</summary>
    public StreamingDocument PreviewDocument => Preview.Stream?.Document ?? _document;

    public bool IsReplaying => _replay is not null;

    public bool IsDirty => _dirty;

    public string? FilePath => _filePath;

    public string DisplayName => _filePath is null ? _untitledName : Path.GetFileName(_filePath);

    public ViewMode ViewMode { get; private set; }

    public event EventHandler? TitleChanged;

    // ———————————————————— 文档 ————————————————————

    private void LoadText(string text, string? path, string untitledName = "未命名.md")
    {
        StopReplay();
        _crlf = text.Contains("\r\n", StringComparison.Ordinal);
        // 每个文件一个新文档：MarkdownView 换文档时清空块视图与块状态（折叠、并排 diff 等按源码偏移记录）
        _document.SnapshotReady -= OnSnapshotReady;
        _document = new StreamingDocument(_services.Profile);
        _document.SnapshotReady += OnSnapshotReady;
        Preview.Document = _document;
        Editor.Document = new TextDocument(text.Replace("\r\n", "\n"));
        _filePath = path;
        _untitledName = untitledName;
        SetImageRoot(path);
        Editor.ScrollToHome();
        Editor.CaretOffset = 0;
        _previewScroll.Offset = default;
        Reparse();
        SetDirty(false);
        UpdatePosition();
    }

    // 相对路径图片按文件所在目录解析；本地文件默认被图片策略拦截，只放行该目录
    private void SetImageRoot(string? path)
    {
        var directory = path is null ? null : Path.GetDirectoryName(Path.GetFullPath(path));
        _services.Images.Policy = _services.Images.Policy with { AllowedFileRoots = directory is null ? [] : [directory] };
        MarkdownServices.SetImageBaseUri(Preview, directory);
    }

    private void AttachUndoStack()
    {
        if (_undoStack is not null)
        {
            _undoStack.PropertyChanged -= OnUndoStackChanged;
        }
        _undoStack = Editor.Document?.UndoStack;
        if (_undoStack is not null)
        {
            _undoStack.PropertyChanged += OnUndoStackChanged;
        }
    }

    // 撤销回到保存时的状态也算「未修改」
    private void OnUndoStackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile) && _undoStack is not null)
        {
            SetDirty(!_undoStack.IsOriginalFile);
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _statusFile.Text = (_filePath ?? DisplayName) + (dirty ? "（已修改）" : "");
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task NewDocumentAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            LoadText("", path: null);
            Editor.TextArea.Focus();
        }
    }

    public async Task LoadSampleAsync(string file, string title)
    {
        if (await ConfirmDiscardAsync())
        {
            LoadText(Program.Sample(file), path: null, untitledName: $"{title}.md");
        }
    }

    /// <summary>直接载入示例（不询问），供冒烟测试使用。</summary>
    public void LoadSample(string file) => LoadText(Program.Sample(file), path: null, untitledName: file);

    public async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync() || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 Markdown 文件",
            AllowMultiple = false,
            FileTypeFilter = [MarkdownFileType, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await OpenFileCoreAsync(path);
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (await ConfirmDiscardAsync())
        {
            await OpenFileCoreAsync(path);
        }
    }

    private async Task OpenFileCoreAsync(string path)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path);
            LoadText(text, path);
            _settings.AddRecent(Path.GetFullPath(path));
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("无法打开文件", $"{path}\n\n{ex.Message}");
            _settings.RecentFiles.Remove(path);
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (_filePath is null)
        {
            return await SaveAsAsync();
        }
        return await WriteAsync(_filePath);
    }

    public async Task<bool> SaveAsAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return false;
        }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 Markdown 文件",
            SuggestedFileName = DisplayName,
            DefaultExtension = "md",
            FileTypeChoices = [MarkdownFileType],
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return false;
        }
        if (!await WriteAsync(path))
        {
            return false;
        }
        _filePath = path;
        SetImageRoot(path);
        _settings.AddRecent(Path.GetFullPath(path));
        _settings.Save();
        SetDirty(false);
        return true;
    }

    private async Task<bool> WriteAsync(string path)
    {
        try
        {
            var text = Editor.Text;
            await File.WriteAllTextAsync(path, _crlf ? text.Replace("\n", "\r\n") : text, new UTF8Encoding(false));
            Editor.Document.UndoStack.MarkAsOriginalFile();
            SetDirty(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("保存失败", $"{path}\n\n{ex.Message}");
            return false;
        }
    }

    /// <summary>有未保存的修改时询问；返回 true 表示可以丢弃当前内容继续。</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!_dirty || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return true;
        }
        var choice = await Dialogs.ShowAsync(owner, "未保存的修改", $"「{DisplayName}」有未保存的修改，是否保存？", "保存", "不保存", "取消");
        return choice switch
        {
            0 => await SaveAsync(),
            1 => true,
            _ => false,
        };
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            await Dialogs.ShowAsync(owner, title, message, "确定");
        }
    }

    private static readonly FilePickerFileType MarkdownFileType = new("Markdown")
    {
        Patterns = ["*.md", "*.markdown", "*.mdown", "*.txt"],
        AppleUniformTypeIdentifiers = ["net.daringfireball.markdown", "public.plain-text"],
        MimeTypes = ["text/markdown", "text/plain"],
    };

    // ———————————————————— 预览 ————————————————————

    private void OnTextChanged()
    {
        if (Preview.Stream is not null && !IsReplaying)
        {
            // 回放结束后第一次编辑：回到实时预览
            Preview.Stream = null;
        }
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>立即用编辑器内容重新解析预览（平时由防抖定时器触发）。</summary>
    public void Reparse()
    {
        _debounce.Stop();
        var text = Editor.Text;
        Volatile.Write(ref _loadStarted, Stopwatch.GetTimestamp());
        _document.Load(text);
        var stats = TextStatistics.Compute(text);
        _statusWords.Text = $"{stats.Words:N0} 字 · {stats.Characters:N0} 字符 · {stats.Lines:N0} 行 · 约 {stats.ReadingMinutes} 分钟";
    }

    // 后台线程
    private void OnSnapshotReady(DocumentSnapshot snapshot)
    {
        if (!snapshot.IsComplete)
        {
            return;
        }
        var ms = Stopwatch.GetElapsedTime(Volatile.Read(ref _loadStarted)).TotalMilliseconds;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_document.Latest, snapshot))
            {
                return;
            }
            _parseMs = ms;
            if (Preview.Stream is null)
            {
                // 立即应用（不等下一帧），大纲、查找与滚动同步读到的都是这一版
                Preview.ApplyLatest();
                if (_findBar.IsVisible && Preview.Search.Query.Length > 0)
                {
                    Preview.Search.Find(Preview.Search.Query, Preview.Search.MatchCase);
                }
            }
            UpdateOutline(snapshot);
            _statusBlocks.Text = $"{snapshot.Count} 块 · 解析 {_parseMs:F1} ms";
            RequestScrollSync();
        });
    }

    private void UpdateOutline(DocumentSnapshot snapshot)
    {
        var items = DocumentNavigation.Outline(snapshot, 0);
        var selectedKey = (_outline.SelectedItem as ListBoxItem)?.Tag is OutlineItem s ? s.Target.BlockKey : -1;
        var rows = new List<ListBoxItem>(items.Count);
        foreach (var item in items)
        {
            rows.Add(new ListBoxItem
            {
                Tag = item,
                Padding = new Thickness(8 + (item.Level - 1) * 14, 4, 8, 4),
                Content = new TextBlock
                {
                    Text = item.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = item.Level <= 2 ? FontWeight.SemiBold : FontWeight.Normal,
                    FontSize = item.Level == 1 ? 14 : 13,
                },
            });
        }
        _updatingOutline = true;
        try
        {
            _outline.ItemsSource = rows;
            _outline.SelectedItem = rows.FirstOrDefault(r => r.Tag is OutlineItem o && o.Target.BlockKey == selectedKey);
        }
        finally
        {
            _updatingOutline = false;
        }
    }

    public IReadOnlyList<OutlineItem> Outline =>
        (_outline.ItemsSource as IEnumerable<ListBoxItem>)?.Select(r => (OutlineItem)r.Tag!).ToList() ?? [];

    /// <summary>大纲跳转：编辑器光标移到标题行，预览滚动到标题。</summary>
    public void JumpTo(OutlineItem item)
    {
        var offset = Math.Clamp(item.Target.BlockKey, 0, Editor.Document.TextLength);
        var line = Editor.Document.GetLineByOffset(offset);
        Editor.CaretOffset = offset;
        // 让标题行出现在编辑器顶部附近（AvaloniaEdit 12 的 ScrollToVerticalOffset 是空实现，只能用 ScrollTo）
        Editor.ScrollTo(line.LineNumber, 0, VisualYPosition.LineTop, 24, 0);
        Preview.ScrollToPoint(item.Target);
    }

    private void RequestScrollSync()
    {
        if (_syncPending || !_settings.ScrollSync || ViewMode != ViewMode.Split)
        {
            return;
        }
        _syncPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncPending = false;
            SyncPreviewToEditor();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 滚动同步：取编辑器顶部可见行的源码偏移，找到包含它的顶层块（块 key 就是源码起点），
    /// 按块内比例换算成预览里的纵坐标。预览的子视图与快照里的顶层块一一对应、顺序相同。
    /// </summary>
    public void SyncPreviewToEditor()
    {
        if (Preview.Snapshot is not { Count: > 0 } snapshot || Editor.Document is null)
        {
            return;
        }
        var textView = Editor.TextArea.TextView;
        var scrollable = Editor.TextArea.TextView.DocumentHeight - textView.Bounds.Height;
        var maxPreview = _previewScroll.Extent.Height - _previewScroll.Viewport.Height;
        if (maxPreview <= 0)
        {
            return;
        }
        if (scrollable > 0 && textView.ScrollOffset.Y >= scrollable - 1)
        {
            _previewScroll.Offset = new Vector(0, maxPreview);
            return;
        }
        var line = textView.GetDocumentLineByVisualTop(textView.ScrollOffset.Y);
        if (line is null)
        {
            return;
        }
        // 行内的像素位置也计入，滚动更平滑
        var lineTop = textView.GetVisualTopByDocumentLine(line.LineNumber);
        var nextTop = line.LineNumber < Editor.Document.LineCount ? textView.GetVisualTopByDocumentLine(line.LineNumber + 1) : lineTop + 1;
        var within = nextTop > lineTop ? Math.Clamp((textView.ScrollOffset.Y - lineTop) / (nextTop - lineTop), 0, 1) : 0;
        var offset = line.Offset + within * Math.Max(1, line.TotalLength);

        var views = Preview.GetVisualChildren().OfType<Control>().ToList();
        if (views.Count != snapshot.Count)
        {
            return;
        }
        var index = 0;
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Key <= offset)
            {
                index = i;
            }
            else
            {
                break;
            }
        }
        var block = snapshot[index];
        var view = views[index];
        var blockEnd = index + 1 < snapshot.Count ? snapshot[index + 1].Key : block.Key + block.Range.Length;
        var fraction = Math.Clamp((offset - block.Key) / Math.Max(1.0, blockEnd - block.Key), 0, 1);
        var spacing = index + 1 < views.Count ? views[index + 1].Bounds.Y - view.Bounds.Bottom : 0;
        if (view.TranslatePoint(default, _previewContent) is not { } origin)
        {
            return;
        }
        var y = origin.Y + fraction * (view.Bounds.Height + Math.Max(0, spacing));
        _previewScroll.Offset = new Vector(0, Math.Clamp(y - 16, 0, maxPreview));
    }

    private async void OnLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        e.Handled = true;
        var url = e.Url;
        // 相对链接指向本地 Markdown 文件时在编辑器里打开
        if (_filePath is not null && !url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var local = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_filePath)!, Uri.UnescapeDataString(url.Split('#')[0])));
            if (File.Exists(local))
            {
                await OpenFileAsync(local);
            }
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }
        if (e.RequiresConfirmation
            && await Dialogs.ShowAsync(owner, "打开链接", $"链接文字与实际地址不一致，确认要打开吗？\n\n{uri}", "打开", "取消") != 0)
        {
            return;
        }
        await owner.Launcher.LaunchUriAsync(uri);
    }

    // ———————————————————— 流式回放 ————————————————————

    /// <summary>把编辑器里的文档当成 AI 回答，按每秒 <paramref name="charsPerSecond"/> 个字符流式推入预览。</summary>
    public async Task ReplayAsync(int charsPerSecond)
    {
        StopReplay();
        var text = Editor.Text;
        var cts = new CancellationTokenSource();
        _replay = cts;
        Editor.IsReadOnly = true;
        UpdateReplayButton();
        var stream = new MarkdownStream(profile: _services.Profile);
        Preview.Stream = stream;
        _previewScroll.Offset = default;
        var random = new Random(7);
        var clock = Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < text.Length && !cts.IsCancelled();)
            {
                var size = Math.Min(random.Next(2, 24), text.Length - i);
                stream.Push(text.Substring(i, size));
                i += size;
                var due = TimeSpan.FromSeconds(i / (double)Math.Max(1, charsPerSecond));
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cts.Token).ConfigureAwait(true);
                }
                else if (i % 2048 < size)
                {
                    await Task.Yield();
                }
                if (_settings.ScrollSync && ViewMode != ViewMode.Editor)
                {
                    _previewScroll.ScrollToEnd();
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
        stream.Complete();
        if (ReferenceEquals(_replay, cts))
        {
            _replay = null;
            Editor.IsReadOnly = false;
            UpdateReplayButton();
        }
    }

    public void StopReplay()
    {
        if (_replay is null)
        {
            return;
        }
        _replay.Cancel();
        _replay = null;
        Editor.IsReadOnly = false;
        Preview.Stream = null;
        UpdateReplayButton();
    }

    private void UpdateReplayButton()
    {
        _replayButton.Content = IsReplaying ? "■ 停止回放" : "▶ 流式回放";
        _replaySpeed.IsEnabled = !IsReplaying;
    }

    // ———————————————————— 编辑命令 ————————————————————

    /// <summary>应用一次格式化编辑（一步撤销），并把焦点还给编辑器。</summary>
    public void Apply(TextEdit edit)
    {
        if (Editor.IsReadOnly)
        {
            return;
        }
        var document = Editor.Document;
        document.BeginUpdate();
        try
        {
            document.Replace(edit.Offset, edit.Length, edit.Text);
        }
        finally
        {
            document.EndUpdate();
        }
        Editor.Select(Math.Clamp(edit.SelectionStart, 0, document.TextLength), Math.Max(0, Math.Min(edit.SelectionLength, document.TextLength - edit.SelectionStart)));
        Editor.TextArea.Caret.BringCaretToView();
        Editor.TextArea.Focus();
    }

    private void Run(Func<string, int, int, TextEdit> command) => Apply(command(Editor.Text, Editor.SelectionStart, Editor.SelectionLength));

    public void Bold() => Run((t, s, l) => MarkdownCommands.ToggleWrap(t, s, l, "**", "粗体文字"));
    public void Italic() => Run((t, s, l) => MarkdownCommands.ToggleWrap(t, s, l, "*", "斜体文字"));
    public void Strikethrough() => Run((t, s, l) => MarkdownCommands.ToggleWrap(t, s, l, "~~", "删除的文字"));
    public void InlineCode() => Run((t, s, l) => MarkdownCommands.ToggleWrap(t, s, l, "`", "code"));
    public void InlineMath() => Run((t, s, l) => MarkdownCommands.ToggleWrap(t, s, l, "$", "x^2"));
    public void Link() => Run((t, s, l) => MarkdownCommands.InsertLink(t, s, l));
    public void Image() => Run((t, s, l) => MarkdownCommands.InsertLink(t, s, l, image: true));
    public void Heading(int level) => Run((t, s, l) => MarkdownCommands.SetHeading(t, s, l, level));
    public void LinePrefix(MarkdownCommands.LinePrefix kind) => Run((t, s, l) => MarkdownCommands.ToggleLinePrefix(t, s, l, kind));
    public void CodeBlock() => Run(MarkdownCommands.InsertCodeBlock);
    public void InsertBlock(string snippet, int selectFrom = 0, int selectLength = 0) =>
        Run((t, s, l) => MarkdownCommands.InsertBlock(t, s + l, snippet, selectFrom, selectLength));

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (Editor.IsReadOnly)
        {
            return;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && Editor.SelectionLength == 0)
        {
            if (MarkdownCommands.ContinueList(Editor.Text, Editor.CaretOffset) is { } edit)
            {
                Apply(edit);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            var text = Editor.Text;
            var (lineStart, lineEnd) = MarkdownCommands.LineSpan(text, Editor.SelectionStart, Editor.SelectionLength);
            var multiLine = text.AsSpan(lineStart, lineEnd - lineStart).Contains('\n');
            if (multiLine || MarkdownCommands.ParseListItem(text[lineStart..lineEnd]) is not null)
            {
                Apply(MarkdownCommands.Indent(text, Editor.SelectionStart, Editor.SelectionLength, e.KeyModifiers == KeyModifiers.Shift));
                e.Handled = true;
            }
        }
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        var mod = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        var inEditor = Editor.IsKeyboardFocusWithin;
        if (e.KeyModifiers == mod)
        {
            Action? action = e.Key switch
            {
                Key.N => () => _ = NewDocumentAsync(),
                Key.O => () => _ = OpenAsync(),
                Key.S => () => _ = SaveAsync(),
                Key.OemPipe or Key.OemBackslash => () => CycleViewMode(),
                Key.OemPlus or Key.Add => () => Zoom(+1),
                Key.OemMinus or Key.Subtract => () => Zoom(-1),
                Key.F when !inEditor => ShowFindBar,
                Key.B when inEditor => Bold,
                Key.I when inEditor => Italic,
                Key.K when inEditor => Link,
                Key.E when inEditor => InlineCode,
                >= Key.D1 and <= Key.D6 when inEditor => () => Heading(e.Key - Key.D0),
                Key.D0 when inEditor => () => Heading(0),
                _ => null,
            };
            if (action is not null)
            {
                action();
                e.Handled = true;
            }
        }
        else if (e.KeyModifiers == (mod | KeyModifiers.Shift))
        {
            Action? action = e.Key switch
            {
                Key.S => () => _ = SaveAsAsync(),
                Key.X when inEditor => Strikethrough,
                Key.K when inEditor => CodeBlock,
                Key.F => ShowFindBar,
                _ => null,
            };
            if (action is not null)
            {
                action();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape && _findBar.IsVisible)
        {
            HideFindBar();
            e.Handled = true;
        }
    }

    // ———————————————————— 视图 ————————————————————

    public void ApplyViewMode(ViewMode mode)
    {
        ViewMode = mode;
        _settings.ViewMode = mode;
        var columns = _body.ColumnDefinitions;
        columns[2].Width = mode == ViewMode.Preview ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        columns[4].Width = mode == ViewMode.Editor ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _body.Children[3].IsVisible = mode == ViewMode.Split;
        _body.Children[2].IsVisible = mode != ViewMode.Preview;
        _body.Children[4].IsVisible = mode != ViewMode.Editor;
        foreach (var (m, button) in _viewButtons)
        {
            button.IsChecked = m == mode;
        }
        RequestScrollSync();
    }

    public void CycleViewMode() => ApplyViewMode(ViewMode switch
    {
        ViewMode.Split => ViewMode.Preview,
        ViewMode.Preview => ViewMode.Editor,
        _ => ViewMode.Split,
    });

    private void ApplyOutlineVisibility()
    {
        _body.ColumnDefinitions[0].Width = _settings.ShowOutline ? new GridLength(220) : new GridLength(0);
        _body.Children[0].IsVisible = _settings.ShowOutline;
        _body.Children[1].IsVisible = _settings.ShowOutline;
    }

    private void Zoom(int direction)
    {
        _settings.EditorFontSize = direction == 0 ? 14 : Math.Clamp(_settings.EditorFontSize + direction, 9, 32);
        ApplyZoom();
        _settings.Save();
    }

    // 编辑器与预览一起缩放：预览字号按编辑器字号的比例换算
    private void ApplyZoom()
    {
        Editor.FontSize = _settings.EditorFontSize;
        Preview.Resources[MarkdownResourceKeys.FontSize] = Math.Round(15 * _settings.EditorFontSize / 14, 1);
    }

    private void ApplyEditorTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        _textMate.SetTheme(_textMateRegistry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        Editor.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xD4, 0xD4, 0xD4) : Color.FromRgb(0x1F, 0x23, 0x28));
        Editor.LineNumbersForeground = new SolidColorBrush(dark ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x70, 0, 0, 0));
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0, 0, 0));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(dark ? Color.FromArgb(0x60, 0x26, 0x4F, 0x78) : Color.FromArgb(0x50, 0xAD, 0xD6, 0xFF));
    }

    // ———————————————————— 查找（预览） ————————————————————

    private (Border Bar, TextBox Box, TextBlock Count) CreateFindBar()
    {
        var box = new TextBox { PlaceholderText = "在预览中查找", MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 60, Margin = new Thickness(8, 0) };
        var matchCase = new CheckBox { Content = "区分大小写", VerticalAlignment = VerticalAlignment.Center };
        var previous = new Button { Content = "↑", Padding = new Thickness(10, 4) };
        var next = new Button { Content = "↓", Padding = new Thickness(10, 4) };
        var close = new Button { Content = "✕", Padding = new Thickness(10, 4) };
        ToolTip.SetTip(previous, "上一个（Shift+Enter）");
        ToolTip.SetTip(next, "下一个（Enter）");
        void Find() => Preview.Search.Find(box.Text ?? "", matchCase.IsChecked == true);
        box.TextChanged += (_, _) => Find();
        matchCase.IsCheckedChanged += (_, _) => Find();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    Preview.Search.Previous();
                }
                else
                {
                    Preview.Search.Next();
                }
                e.Handled = true;
            }
        };
        previous.Click += (_, _) => Preview.Search.Previous();
        next.Click += (_, _) => Preview.Search.Next();
        close.Click += (_, _) => HideFindBar();
        Preview.Search.Changed += () =>
            count.Text = Preview.Search.Count == 0 ? (string.IsNullOrEmpty(Preview.Search.Query) ? "" : "无结果") : $"{Preview.Search.CurrentIndex + 1}/{Preview.Search.Count}";
        var bar = new Border
        {
            IsVisible = false,
            Padding = new Thickness(10, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { box, count, matchCase, previous, next, close } },
        };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("Gallery.ChromeBackground"));
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Gallery.Divider"));
        return (bar, box, count);
    }

    public void ShowFindBar()
    {
        if (ViewMode == ViewMode.Editor)
        {
            ApplyViewMode(ViewMode.Split);
        }
        _findBar.IsVisible = true;
        _findBox.Focus();
        _findBox.SelectAll();
    }

    private void HideFindBar()
    {
        _findBar.IsVisible = false;
        Preview.Search.Clear();
        _findCount.Text = "";
    }

    // ———————————————————— 布局 ————————————————————

    private void AddColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        _body.Children.Add(control);
    }

    private static GridSplitter Splitter()
    {
        var splitter = new GridSplitter { Width = 1, ResizeDirection = GridResizeDirection.Columns, MinWidth = 1 };
        splitter.Bind(BackgroundProperty, splitter.GetResourceObservable("Gallery.Divider"));
        return splitter;
    }

    private static TextBlock StatusText() => new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 18, 0) };

    private Control CreateStatusBar()
    {
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { _statusWords, _statusBlocks, _statusPosition } };
        var bar = new DockPanel { Margin = new Thickness(12, 4) };
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        _statusFile.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
        bar.Children.Add(_statusFile);
        var border = new Border { Child = bar, BorderThickness = new Thickness(0, 1, 0, 0) };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Gallery.ChromeBackground"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Gallery.Divider"));
        border.Bind(TextBlock.ForegroundProperty, border.GetResourceObservable("Gallery.SecondaryText"));
        return border;
    }

    private void UpdatePosition()
    {
        var caret = Editor.TextArea.Caret;
        var selection = Editor.SelectionLength > 0 ? $"（已选 {Editor.SelectionLength}）" : "";
        _statusPosition.Text = $"行 {caret.Line}，列 {caret.Column}{selection}";
    }

    private Control CreateToolbar()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4) };
        void Add(Control c) => panel.Children.Add(c);
        void Separator() => Add(new Border { Width = 1, Height = 20, Margin = new Thickness(6, 0), Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)) });

        Add(FileMenuButton());
        Add(Tool("打开", "打开文件（Ctrl/⌘+O）", () => _ = OpenAsync()));
        Add(Tool("保存", "保存（Ctrl/⌘+S）", () => _ = SaveAsync()));
        Separator();
        Add(Tool("B", "粗体（Ctrl/⌘+B）", Bold, FontWeight.Bold));
        Add(Tool("I", "斜体（Ctrl/⌘+I）", Italic, style: FontStyle.Italic));
        Add(Tool("S", "删除线（Ctrl/⌘+Shift+X）", Strikethrough, decorations: TextDecorations.Strikethrough));
        Add(Tool("</>", "行内代码（Ctrl/⌘+E）", InlineCode));
        Add(MenuTool("标题", "标题级别（Ctrl/⌘+1…6，Ctrl/⌘+0 正文）",
            [.. Enumerable.Range(1, 6).Select(l => Item($"{new string('#', l)} 标题 {l}", () => Heading(l))), Item("正文", () => Heading(0))]));
        Add(Tool("链接", "链接（Ctrl/⌘+K）", Link));
        Add(Tool("图片", "图片", Image));
        Separator();
        Add(Tool("引用", "引用", () => LinePrefix(MarkdownCommands.LinePrefix.Quote)));
        Add(Tool("• 列表", "无序列表", () => LinePrefix(MarkdownCommands.LinePrefix.Bullet)));
        Add(Tool("1. 列表", "有序列表", () => LinePrefix(MarkdownCommands.LinePrefix.Ordered)));
        Add(Tool("☐ 任务", "任务列表", () => LinePrefix(MarkdownCommands.LinePrefix.Task)));
        Add(Tool("代码块", "代码块（Ctrl/⌘+Shift+K）", CodeBlock));
        Add(MenuTool("插入", "插入块级内容",
        [
            Item("表格", () => InsertBlock(MarkdownCommands.Snippets.Table, 2, 3)),
            Item("行内公式", InlineMath),
            Item("公式块", () => InsertBlock(MarkdownCommands.Snippets.MathBlock, 3, MarkdownCommands.Snippets.MathBlock.Length - 6)),
            Item("Mermaid 图表", () => InsertBlock(MarkdownCommands.Snippets.Mermaid, 11, MarkdownCommands.Snippets.Mermaid.Length - 15)),
            Item("折叠块", () => InsertBlock(MarkdownCommands.Snippets.Details, 19, 2)),
            Item("提示块", () => InsertBlock(MarkdownCommands.Snippets.Alert, 12, 4)),
            Item("分隔线", () => InsertBlock(MarkdownCommands.Snippets.Rule, 3)),
        ]));
        Separator();
        Add(MenuTool("示例", "打开示例文档", [.. SampleList.Select(s => Item(s.Title, () => _ = LoadSampleAsync(s.File, s.Title)))]));
        _replayButton.Padding = new Thickness(10, 5);
        _replayButton.Margin = new Thickness(2);
        ToolTip.SetTip(_replayButton, "把当前文档当成 AI 回答，流式输出到预览");
        _replayButton.Click += (_, _) =>
        {
            if (IsReplaying)
            {
                StopReplay();
            }
            else
            {
                _ = ReplayAsync(ReplaySpeeds[Math.Max(0, _replaySpeed.SelectedIndex)].CharsPerSecond);
            }
        };
        UpdateReplayButton();
        _replaySpeed.ItemsSource = ReplaySpeeds.Select(s => $"{s.Label} {s.CharsPerSecond} 字/秒").ToArray();
        _replaySpeed.SelectedIndex = 1;
        _replaySpeed.Margin = new Thickness(2);
        _replaySpeed.VerticalAlignment = VerticalAlignment.Center;
        Add(_replayButton);
        Add(_replaySpeed);
        Separator();
        foreach (var (mode, label) in new[] { (ViewMode.Editor, "编辑"), (ViewMode.Split, "分栏"), (ViewMode.Preview, "预览") })
        {
            var button = new ToggleButton { Content = label, Padding = new Thickness(10, 5), Margin = new Thickness(1, 2) };
            ToolTip.SetTip(button, "切换视图（Ctrl/⌘+\\）");
            button.Click += (_, _) => ApplyViewMode(mode);
            _viewButtons[mode] = button;
            Add(button);
        }
        Add(ViewMenuButton());
        Add(Tool("查找", "在预览中查找（Ctrl/⌘+Shift+F）", ShowFindBar));

        var border = new Border { Child = panel, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Gallery.ChromeBackground"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Gallery.Divider"));
        return border;
    }

    private Button FileMenuButton()
    {
        var button = new Button { Content = "文件 ▾", Padding = new Thickness(10, 5), Margin = new Thickness(2) };
        var flyout = new MenuFlyout();
        flyout.Opening += (_, _) =>
        {
            var recent = new MenuItem { Header = "最近打开" };
            foreach (var path in _settings.RecentFiles)
            {
                recent.Items.Add(Item(path, () => _ = OpenFileAsync(path)));
            }
            recent.IsEnabled = recent.Items.Count > 0;
            flyout.Items.Clear();
            flyout.Items.Add(Item("新建（Ctrl/⌘+N）", () => _ = NewDocumentAsync()));
            flyout.Items.Add(Item("打开…（Ctrl/⌘+O）", () => _ = OpenAsync()));
            flyout.Items.Add(recent);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(Item("保存（Ctrl/⌘+S）", () => _ = SaveAsync()));
            flyout.Items.Add(Item("另存为…（Ctrl/⌘+Shift+S）", () => _ = SaveAsAsync()));
        };
        flyout.Items.Add(new MenuItem { Header = "…" });
        button.Flyout = flyout;
        return button;
    }

    private Button ViewMenuButton()
    {
        var button = new Button { Content = "选项 ▾", Padding = new Thickness(10, 5), Margin = new Thickness(2) };
        MenuItem Check(string header, Func<bool> get, Action<bool> set)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = get() };
            item.Click += (_, _) =>
            {
                set(!get());
                item.IsChecked = get();
                _settings.Save();
            };
            return item;
        }
        var flyout = new MenuFlyout();
        flyout.Items.Add(Check("显示大纲", () => _settings.ShowOutline, v => { _settings.ShowOutline = v; ApplyOutlineVisibility(); }));
        flyout.Items.Add(Check("滚动同步", () => _settings.ScrollSync, v => { _settings.ScrollSync = v; RequestScrollSync(); }));
        flyout.Items.Add(Check("自动换行", () => _settings.WordWrap, v => { _settings.WordWrap = v; Editor.WordWrap = v; }));
        flyout.Items.Add(Check("行号", () => _settings.ShowLineNumbers, v => { _settings.ShowLineNumbers = v; Editor.ShowLineNumbers = v; }));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("放大（Ctrl/⌘+=）", () => Zoom(+1)));
        flyout.Items.Add(Item("缩小（Ctrl/⌘+-）", () => Zoom(-1)));
        flyout.Items.Add(Item("恢复默认字号", () => Zoom(0)));
        button.Flyout = flyout;
        return button;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static Button MenuTool(string label, string tip, MenuItem[] items)
    {
        var button = new Button { Content = label + " ▾", Padding = new Thickness(10, 5), Margin = new Thickness(2) };
        ToolTip.SetTip(button, tip);
        var flyout = new MenuFlyout();
        foreach (var item in items)
        {
            flyout.Items.Add(item);
        }
        button.Flyout = flyout;
        return button;
    }

    private static Button Tool(string label, string tip, Action action, FontWeight? weight = null, FontStyle? style = null, TextDecorationCollection? decorations = null)
    {
        var text = new TextBlock { Text = label, FontWeight = weight ?? FontWeight.Normal, FontStyle = style ?? FontStyle.Normal, TextDecorations = decorations };
        var button = new Button { Content = text, Padding = new Thickness(10, 5), Margin = new Thickness(2), MinWidth = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}

internal static class CancellationExtensions
{
    public static bool IsCancelled(this CancellationTokenSource cts) => cts.IsCancellationRequested;
}
