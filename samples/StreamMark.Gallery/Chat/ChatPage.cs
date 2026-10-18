using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StreamMark.Avalonia;
using StreamMark.Avalonia.Transcript;
using StreamMark.Core.Parsing;

namespace StreamMark.Gallery.Chat;

/// <summary>会话演示：虚拟化的长会话、跨消息选区，顶部按钮模拟一条流式回答。</summary>
public sealed class ChatPage : DockPanel
{
    private readonly MarkdownProfile _profile;

    public ChatPage(MarkdownProfile profile)
    {
        _profile = profile;
        Transcript = new TranscriptView { MaxContentWidth = 820, Padding = new Thickness(0, 12, 0, 24) };
        Transcript.Entries.Add(new ContentEntry(Bubble("展示一下 StreamMark 支持的内容")));
        Transcript.Entries.Add(MarkdownEntry.FromText(Program.Sample("showcase.md"), profile));

        var stream = new Button { Content = "模拟流式回答", Margin = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left };
        stream.Click += (_, _) => _ = SimulateAsync();
        var clear = new Button { Content = "清空会话", Margin = new Thickness(0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => Transcript.Entries.Clear();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Children = { stream, clear } };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(Transcript);
    }

    public TranscriptView Transcript { get; }

    public static Control Bubble(string text)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 9),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Gallery.BubbleBackground"));
        return border;
    }

    /// <summary>以不均匀的分片模拟网络流，展示平滑输出与渐显。</summary>
    public async Task SimulateAsync(int delayMs = 30)
    {
        Transcript.Entries.Add(new ContentEntry(Bubble("再来一遍，这次流式输出")));
        var markdownStream = new MarkdownStream(profile: _profile);
        Transcript.Entries.Add(new MarkdownEntry(markdownStream));
        Transcript.ScrollToEnd();
        var text = Program.Sample("showcase.md");
        var random = new Random(7);
        for (var i = 0; i < text.Length;)
        {
            var size = Math.Min(random.Next(2, 24), text.Length - i);
            markdownStream.Push(text.Substring(i, size));
            i += size;
            if (delayMs > 0)
            {
                await Task.Delay(random.Next(delayMs / 2, delayMs * 2));
            }
        }
        markdownStream.Complete();
    }
}
