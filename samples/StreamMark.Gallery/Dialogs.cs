using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StreamMark.Gallery;

/// <summary>简单的模态对话框（Avalonia 没有内置 MessageBox）。</summary>
public static class Dialogs
{
    /// <summary>显示消息与按钮，返回被点击按钮的序号；关闭窗口返回 -1。</summary>
    public static async Task<int> ShowAsync(Window owner, string title, string message, params string[] buttons)
    {
        var result = -1;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.Bind(Window.BackgroundProperty, owner.GetResourceObservable("Gallery.ChromeBackground"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
            if (i == 0)
            {
                button.Classes.Add("accent");
            }
            button.Click += (_, _) =>
            {
                result = index;
                dialog.Close();
            };
            row.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                row,
            },
        };
        await dialog.ShowDialog(owner);
        return result;
    }
}
