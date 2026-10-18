using Avalonia;
using StreamMark.Avalonia.Extensions;
using StreamMark.Avalonia.Images;
using StreamMark.Avalonia.Plugins;
using StreamMark.Highlighting.TextMate;
using StreamMark.Mermaid;
using StreamMark.RaTeX;

namespace StreamMark.Gallery;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke"))
        {
            return SmokeTest.Run(args);
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GalleryApp>().UsePlatformDetect();

    /// <summary>RaTeX 原生库能否加载（缺库时公式会在界面线程抛 DllNotFoundException，所以先探测）。</summary>
    public static bool MathAvailable { get; } = ProbeMath();

    private static bool ProbeMath()
    {
        try
        {
            return RaTeXEngine.Layout("x", displayMode: false).IsSuccess;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine($"RaTeX native library unavailable, math disabled: {ex.Message}");
            return false;
        }
    }

    /// <summary>渲染扩展：公式（RaTeX）、代码高亮（TextMate）、图表（Mermaid）；图片支持 SVG，本地图片按打开的文件目录放行。</summary>
    public static MarkdownServices CreateServices()
    {
        IMarkdownPlugin[] plugins = MathAvailable
            ? [new RaTeXPlugin(), new TextMatePlugin(prewarm: ["csharp", "python", "typescript", "json", "bash"]), new MermaidPlugin()]
            : [new TextMatePlugin(prewarm: ["csharp", "python", "typescript", "json", "bash"]), new MermaidPlugin()];
        return new MarkdownServices
        {
            Plugins = plugins,
            Images = new ImagePipeline(new ImagePipelineOptions { SvgRasterizer = new SvgImageRasterizer() }),
        };
    }

    public static string Sample(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream($"Samples.{name}")
                           ?? throw new InvalidOperationException($"Missing sample {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
