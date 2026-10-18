using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using StreamMark.Avalonia.Extensions;
using StreamMark.Gallery.Chat;
using StreamMark.Gallery.Editor;

namespace StreamMark.Gallery;

/// <summary>主窗口：顶栏切换「编辑器 / 会话演示」与主题，两页共用同一套渲染服务。</summary>
public sealed class MainWindow : Window
{
    private readonly GallerySettings _settings;
    private readonly ToggleButton _editorTab;
    private readonly ToggleButton _chatTab;
    private bool _closeConfirmed;

    public MainWindow(GallerySettings settings)
    {
        _settings = settings;
        Title = "StreamMark Gallery";
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        MinWidth = 720;
        MinHeight = 480;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Gallery.WindowBackground"));
        GalleryApp.ApplyTheme(settings.Theme);

        Services = Program.CreateServices();
        MarkdownServices.SetServices(this, Services);

        Editor = new EditorPage(Services, settings);
        Chat = new ChatPage(Services.Profile) { IsVisible = false };
        Editor.TitleChanged += (_, _) => UpdateTitle();

        _editorTab = Tab("编辑器", true);
        _chatTab = Tab("会话演示", false);
        _editorTab.Click += (_, _) => ShowPage(editor: true);
        _chatTab.Click += (_, _) => ShowPage(editor: false);

        var theme = new ComboBox
        {
            ItemsSource = new[] { "跟随系统", "亮色", "暗色" },
            SelectedIndex = (int)settings.Theme,
            MinWidth = 110,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(theme, "主题");
        theme.SelectionChanged += (_, _) =>
        {
            _settings.Theme = (ThemeChoice)Math.Max(0, theme.SelectedIndex);
            GalleryApp.ApplyTheme(_settings.Theme);
            _settings.Save();
        };

        var brand = new TextBlock { Text = "StreamMark", FontWeight = FontWeight.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0) };
        var header = new DockPanel { Margin = new Thickness(10, 6) };
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _editorTab, _chatTab } };
        DockPanel.SetDock(tabs, Dock.Left);
        header.Children.Add(tabs);
        DockPanel.SetDock(theme, Dock.Right);
        header.Children.Add(theme);
        header.Children.Add(new Control());

        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Gallery.ChromeBackground"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Gallery.Divider"));

        var root = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);
        root.Children.Add(new Panel { Children = { Editor, Chat } });
        Content = root;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        UpdateTitle();
    }

    public MarkdownServices Services { get; }

    public EditorPage Editor { get; }

    public ChatPage Chat { get; }

    public void ShowPage(bool editor)
    {
        Editor.IsVisible = editor;
        Chat.IsVisible = !editor;
        _editorTab.IsChecked = editor;
        _chatTab.IsChecked = !editor;
    }

    private static ToggleButton Tab(string text, bool selected) => new()
    {
        Content = text,
        IsChecked = selected,
        Padding = new Thickness(14, 5),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void UpdateTitle()
    {
        Title = $"{(Editor.IsDirty ? "• " : "")}{Editor.DisplayName} — StreamMark Gallery";
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (file is null)
        {
            return;
        }
        ShowPage(editor: true);
        await Editor.OpenFileAsync(file);
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !Editor.IsDirty)
        {
            SaveWindowSettings();
            return;
        }
        e.Cancel = true;
        if (await Editor.ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void SaveWindowSettings()
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.Save();
    }
}
