using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace StreamMark.Gallery;

public sealed class GalleryApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(GallerySettings.Load());
            // 命令行或 Finder「打开方式」传入的 .md 文件
            var file = desktop.Args?.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
            if (file is not null)
            {
                window.Opened += async (_, _) => await window.Editor.OpenFileAsync(file);
            }
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(ThemeChoice choice)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = choice switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
