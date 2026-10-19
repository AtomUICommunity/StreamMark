using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StreamMark.Avalonia.Rendering;
using StreamMark.Avalonia.Transcript;
using StreamMark.Core.Model;
using StreamMark.Core.Pacing;
using StreamMark.Core.Streaming;
using StreamMark.Gallery.Editor;
using StreamMark.RaTeX;

namespace StreamMark.Gallery;

/// <summary>
/// NativeAOT 冒烟测试：在发布后的原生二进制里跑一遍关键路径（RaTeX 原生调用 + JSON 解析等），
/// 任何一步失败返回非零退出码。
/// </summary>
internal static class SmokeTest
{
    public static int Run(string[] args)
    {
        GallerySettings.Persist = false;
        var failures = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail is null ? "" : " — " + detail)}");
            if (!ok)
            {
                failures++;
            }
        }

        var math = RaTeXEngine.Layout(@"\ce{H2SO4 + 2NaOH -> Na2SO4 + 2H2O}", displayMode: true);
        Check("ratex.layout", math.IsSuccess && math.DisplayList!.Items.Count > 0, math.Error ?? $"{math.DisplayList!.Items.Count} items, width {math.DisplayList.Width:F2}em");

        var broken = RaTeXEngine.Layout(@"\frac{1}{", displayMode: false);
        Check("ratex.error", !broken.IsSuccess && !string.IsNullOrEmpty(broken.Error), broken.Error);

        // 流式模拟：节奏控制器 → 流式文档 → 差异，验证 Core 在 AOT 下的完整路径
        const string markdown = "# Title\n\nSome **bold** text and \\(x^2\\).\n\n```csharp\nvar x = 1;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n";
        var document = new StreamingDocument();
        var pacer = new StreamPacer();
        var now = TimeSpan.Zero;
        for (var i = 0; i < markdown.Length; i += 5)
        {
            pacer.Push(markdown.AsSpan(i, System.Math.Min(5, markdown.Length - i)), now);
            now += TimeSpan.FromMilliseconds(10);
            document.Append(pacer.Tick(now));
        }
        pacer.Finish(now);
        while (!pacer.IsDrained)
        {
            now += TimeSpan.FromMilliseconds(16);
            document.Append(pacer.Tick(now));
        }
        document.Complete();
        document.WhenIdle().GetAwaiter().GetResult();
        var final = document.Latest;
        var kinds = string.Join(",", final.Blocks.Select(b => b.GetType().Name));
        Check("stream.complete", final.IsComplete && final.SourceLength == markdown.Length, kinds);
        Check("stream.blocks", final.Count == 5 && final[1] is ParagraphBlock { Content.Objects.Length: 1 } && final[2] is CodeBlock { Language: "csharp" } && final[3] is TableBlock, kinds);
        Check("stream.diff", SnapshotDiff.Compute(null, final).Count == final.Count);

        CheckEditorCommands(Check);

        // 界面：在无头平台上用 Skia 真实渲染会话视图与编辑器（公式、高亮、图表、表格），并截图
        try
        {
            RenderGallery(args, Check);
        }
        catch (Exception ex)
        {
            Check("ui.render", false, ex.ToString());
        }

        Console.WriteLine(failures == 0 ? "SMOKE OK" : $"SMOKE FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }

    // 编辑命令是纯函数：输入源码与选区，输出一次替换
    private static void CheckEditorCommands(Action<string, bool, string?> check)
    {
        void Expect(string name, string source, TextEdit edit, string expected)
        {
            var actual = edit.ApplyTo(source);
            check(name, actual == expected, actual == expected ? null : $"got {Escape(actual)}, expected {Escape(expected)}");
        }
        static string Escape(string s) => "\"" + s.Replace("\n", "\\n") + "\"";

        Expect("editor.bold", "make it bold", MarkdownCommands.ToggleWrap("make it bold", 8, 4, "**", "x"), "make it **bold**");
        Expect("editor.unbold", "make it **bold**", MarkdownCommands.ToggleWrap("make it **bold**", 10, 4, "**", "x"), "make it bold");
        var italic = MarkdownCommands.ToggleWrap("", 0, 0, "*", "斜体");
        Expect("editor.placeholder", "", italic, "*斜体*");
        check("editor.placeholder.selection", italic is { SelectionStart: 1, SelectionLength: 2 }, $"{italic.SelectionStart}+{italic.SelectionLength}");
        Expect("editor.heading", "Title\nbody", MarkdownCommands.SetHeading("Title\nbody", 2, 0, 2), "## Title\nbody");
        Expect("editor.heading.toggle", "## Title", MarkdownCommands.SetHeading("## Title", 4, 0, 2), "Title");
        Expect("editor.heading.change", "# Title", MarkdownCommands.SetHeading("# Title", 3, 0, 3), "### Title");
        Expect("editor.bullets", "a\nb", MarkdownCommands.ToggleLinePrefix("a\nb", 0, 3, MarkdownCommands.LinePrefix.Bullet), "- a\n- b");
        Expect("editor.ordered", "- a\n- b", MarkdownCommands.ToggleLinePrefix("- a\n- b", 0, 7, MarkdownCommands.LinePrefix.Ordered), "1. a\n2. b");
        Expect("editor.quote.toggle", "> a\n> b", MarkdownCommands.ToggleLinePrefix("> a\n> b", 0, 7, MarkdownCommands.LinePrefix.Quote), "a\nb");
        Expect("editor.link", "see https://x.dev", MarkdownCommands.InsertLink("see https://x.dev", 4, 13), "see [链接文字](https://x.dev)");
        var list = MarkdownCommands.ContinueList("- item", 6);
        check("editor.list.continue", list is { } l1 && l1.ApplyTo("- item") == "- item\n- " && l1.SelectionStart == 9, list?.ApplyTo("- item"));
        var ordered = MarkdownCommands.ContinueList("1. one", 6);
        check("editor.list.ordered", ordered?.ApplyTo("1. one") == "1. one\n2. ", ordered?.ApplyTo("1. one"));
        var task = MarkdownCommands.ContinueList("- [x] done", 10);
        check("editor.list.task", task?.ApplyTo("- [x] done") == "- [x] done\n- [ ] ", task?.ApplyTo("- [x] done"));
        var exit = MarkdownCommands.ContinueList("- a\n- ", 6);
        check("editor.list.exit", exit?.ApplyTo("- a\n- ") == "- a\n", exit?.ApplyTo("- a\n- "));
        check("editor.list.plain", MarkdownCommands.ContinueList("plain text", 10) is null && MarkdownCommands.ContinueList("---", 3) is null, null);
        Expect("editor.indent", "- a\n- b", MarkdownCommands.Indent("- a\n- b", 4, 3, outdent: false), "- a\n  - b");
        Expect("editor.outdent", "- a\n  - b", MarkdownCommands.Indent("- a\n  - b", 6, 0, outdent: true), "- a\n- b");
        Expect("editor.block", "text", MarkdownCommands.InsertBlock("text", 4, "---"), "text\n\n---\n");
        var stats = TextStatistics.Compute("Hello world，你好世界\n第二行");
        check("editor.stats", stats is { Words: 9, Lines: 2 }, $"{stats.Words} words, {stats.Lines} lines");
    }

    private static void RenderGallery(string[] args, Action<string, bool, string?> check)
    {
        AppBuilder.Configure<GalleryApp>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        // 高度刻意小于会话总高：虚拟化必须生效（ui.virtualized）
        var window = new MainWindow(new GallerySettings { Theme = ThemeChoice.Dark }) { Width = 1000, Height = 2400 };
        window.Show();
        RenderChat(window, check);
        RenderEditor(window, check);

        using var frame = window.CaptureRenderedFrame();
        check("ui.frame", frame is not null, null);
        var output = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?["--out=".Length..];
        if (frame is not null && output is not null)
        {
            using var file = File.Create(output);
            frame.Save(file, new PngBitmapEncoderOptions());
            Console.WriteLine($"saved {output}");
        }
    }

    private static void RenderChat(MainWindow window, Action<string, bool, string?> check)
    {
        window.ShowPage(editor: false);
        var chat = window.Chat;
        var simulation = chat.SimulateAsync(delayMs: 0);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var stream = chat.Transcript.Entries.OfType<MarkdownEntry>().Last();
        while (DateTime.UtcNow < deadline && !(simulation.IsCompleted && stream.Document.Latest.IsComplete))
        {
            Pump();
        }
        check("ui.stream.complete", stream.Document.Latest.IsComplete, $"{stream.Document.Latest.Count} blocks");

        // 回到顶部看完整的静态演示
        chat.Transcript.Offset = new Vector(0, 0);
        DiagramBlockView? diagram = null;
        while (DateTime.UtcNow < deadline)
        {
            Pump();
            diagram = chat.GetVisualDescendants().OfType<DiagramBlockView>().FirstOrDefault();
            if (diagram is { IsShowingDiagram: true, Bounds.Height: > 80 })
            {
                break;
            }
        }
        var panel = chat.Transcript.Panel;
        check("ui.virtualized", panel.RealizedCount > 0 && panel.RealizedCount < panel.RowCount, $"{panel.RealizedCount}/{panel.RowCount} rows realized");
        check("ui.math", chat.GetVisualDescendants().OfType<MathBlockView>().Any(), null);
        check("ui.diagram", diagram is { IsShowingDiagram: true, Bounds.Height: > 80 }, diagram is null ? "no diagram view" : $"showing {diagram.IsShowingDiagram}, height {diagram.Bounds.Height:F0}");
        check("ui.code", chat.GetVisualDescendants().OfType<CodeBlockView>().Any(), null);
        check("ui.components", chat.GetVisualDescendants().OfType<StreamMark.Avalonia.Plugins.CollapsibleBlockView>().Any(), null);
        check("ui.html", chat.GetVisualDescendants().OfType<HtmlBoxBlockView>().Count() >= 2, null);
    }

    private static void RenderEditor(MainWindow window, Action<string, bool, string?> check)
    {
        // 编辑器与预览都需要能滚动，滚动同步才有意义
        window.Height = 900;
        window.ShowPage(editor: true);
        var editor = window.Editor;
        var deadline = DateTime.UtcNow.AddSeconds(30);

        // 载入示例 → 预览出块
        editor.LoadSample("code.md");
        bool PreviewCurrent() => editor.Preview.Snapshot is { IsComplete: true } s && s.SourceLength == editor.Editor.Document.TextLength && s.Count > 0;
        while (DateTime.UtcNow < deadline && !PreviewCurrent())
        {
            Pump();
        }
        Pump();
        var snapshot = editor.Preview.Snapshot;
        check("ui.editor.preview", PreviewCurrent(), snapshot is null ? "no snapshot" : $"{snapshot.Count} blocks, {snapshot.SourceLength}/{editor.Editor.Document.TextLength} chars");
        check("ui.editor.code", editor.Preview.GetVisualDescendants().OfType<CodeBlockView>().Count() >= 5, null);
        check("ui.editor.diff", editor.Preview.GetVisualDescendants().OfType<DiffBlockView>().Any(), null);
        check("ui.editor.outline", editor.Outline.Count >= 4 && editor.Outline[0].Text == "代码与差异", string.Join(" / ", editor.Outline.Select(o => o.Text)));
        check("ui.editor.highlight", editor.Editor.TextArea.TextView.LineTransformers.Count > 0, $"{editor.Editor.TextArea.TextView.LineTransformers.Count} line transformers");

        // 在文档中间插入一张表：编辑点之前的块视图必须原样保留，之后出现表格
        var firstView = editor.Preview.GetVisualChildren().First();
        var at = editor.Editor.Text.IndexOf("## 多种语言", StringComparison.Ordinal);
        editor.Editor.CaretOffset = at;
        editor.InsertBlock(MarkdownCommands.Snippets.Table);
        editor.Reparse();
        while (DateTime.UtcNow < deadline && !(PreviewCurrent() && editor.Preview.GetVisualDescendants().OfType<TableBlockView>().Any()))
        {
            Pump();
        }
        check("ui.editor.edit", editor.Preview.GetVisualDescendants().OfType<TableBlockView>().Any() && editor.IsDirty, $"dirty {editor.IsDirty}");
        check("ui.editor.reuse", ReferenceEquals(firstView, editor.Preview.GetVisualChildren().First()), null);

        // 滚动同步：编辑器滚到「差异」一节，预览跟着离开顶部
        var diffLine = editor.Editor.Document.GetLineByOffset(editor.Editor.Text.IndexOf("## 差异", StringComparison.Ordinal)).LineNumber;
        var textView = editor.Editor.TextArea.TextView;
        // AvaloniaEdit 12 的 ScrollToVerticalOffset 是空实现，用 ScrollTo 把该行放到顶部
        editor.Editor.ScrollTo(diffLine, 0, AvaloniaEdit.Rendering.VisualYPosition.LineTop, 0, 0);
        for (var i = 0; i < 5; i++)
        {
            Pump();
        }
        editor.SyncPreviewToEditor();
        Pump();
        var scroller = editor.Preview.FindAncestorOfType<ScrollViewer>();
        check("ui.editor.sync", scroller is { Offset.Y: > 100 }, scroller is null ? "no scroll viewer"
            : $"editor offset {textView.ScrollOffset.Y:F0} (line {diffLine}), preview offset {scroller.Offset.Y:F0} of {scroller.Extent.Height - scroller.Viewport.Height:F0}");

        // 撤销回到原始内容 → 不再是「已修改」
        editor.Editor.Undo();
        check("ui.editor.undo", !editor.IsDirty, null);

        // 流式回放：把文档流式推入同一个预览
        var replay = editor.ReplayAsync(charsPerSecond: 1_000_000);
        while (DateTime.UtcNow < deadline && !(replay.IsCompleted && editor.PreviewDocument.Latest.IsComplete))
        {
            Pump();
        }
        var streamed = editor.PreviewDocument.Latest;
        check("ui.editor.replay", replay.IsCompletedSuccessfully && streamed.IsComplete && streamed.SourceLength == editor.Editor.Document.TextLength && !editor.IsReplaying,
            $"{streamed.Count} blocks, {streamed.SourceLength}/{editor.Editor.Document.TextLength} chars");

        // 视图切换
        editor.ApplyViewMode(ViewMode.Preview);
        Pump();
        var previewOnly = !editor.Editor.IsEffectivelyVisible && editor.Preview.IsEffectivelyVisible;
        editor.ApplyViewMode(ViewMode.Split);
        Pump();
        check("ui.editor.viewmode", previewOnly && editor.Editor.IsEffectivelyVisible, null);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(16);
    }
}
