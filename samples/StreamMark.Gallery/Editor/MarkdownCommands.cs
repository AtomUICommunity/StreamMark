using System.Text;

namespace StreamMark.Gallery.Editor;

/// <summary>一次文本替换：把 [<see cref="Offset"/>, +<see cref="Length"/>) 换成 <see cref="Text"/>，之后选中 [<see cref="SelectionStart"/>, +<see cref="SelectionLength"/>)。</summary>
public readonly record struct TextEdit(int Offset, int Length, string Text, int SelectionStart, int SelectionLength)
{
    public string ApplyTo(string source) => string.Concat(source.AsSpan(0, Offset), Text, source.AsSpan(Offset + Length));
}

/// <summary>
/// 编辑器的格式化命令。全部是纯函数：输入源码与选区，输出一次替换；由编辑器负责应用并合并撤销。
/// </summary>
public static class MarkdownCommands
{
    /// <summary>行内包裹（粗体、斜体、删除线、行内代码）：已包裹则去掉，否则包裹；没有选区时插入占位文字并选中。</summary>
    public static TextEdit ToggleWrap(string text, int start, int length, string marker, string placeholder)
    {
        var m = marker.Length;
        // 选区外侧已有标记：**[选区]**
        if (start >= m && start + length + m <= text.Length
            && text.AsSpan(start - m, m).SequenceEqual(marker) && text.AsSpan(start + length, m).SequenceEqual(marker)
            && !IsLongerRun(text, start - m, start + length + m, marker))
        {
            return new TextEdit(start - m, length + 2 * m, text.Substring(start, length), start - m, length);
        }
        // 选区本身带标记：[**文字**]
        if (length >= 2 * m && text.AsSpan(start, m).SequenceEqual(marker) && text.AsSpan(start + length - m, m).SequenceEqual(marker))
        {
            var inner = text.Substring(start + m, length - 2 * m);
            return new TextEdit(start, length, inner, start, inner.Length);
        }
        if (length == 0)
        {
            return new TextEdit(start, 0, marker + placeholder + marker, start + m, placeholder.Length);
        }
        // 选区两端的空白留在标记外面：「 文字 」→「 **文字** 」
        var selected = text.AsSpan(start, length);
        var lead = selected.Length - selected.TrimStart().Length;
        var core = selected.Trim().ToString();
        if (core.Length == 0)
        {
            return new TextEdit(start + length, 0, marker + placeholder + marker, start + length + m, placeholder.Length);
        }
        var wrapped = marker + core + marker;
        return new TextEdit(start + lead, core.Length, wrapped, start + lead + m, core.Length);
    }

    // 「*」与「**」共用字符：选区外是「***」时只算「**」的一部分不足以判定，按更长的连续标记处理
    private static bool IsLongerRun(string text, int before, int after, string marker)
    {
        var c = marker[0];
        if (marker.Length != 1 || (c != '*' && c != '_'))
        {
            return false;
        }
        var leftDouble = before > 0 && text[before - 1] == c;
        var rightDouble = after < text.Length && text[after] == c;
        return leftDouble != rightDouble;
    }

    /// <summary>链接：选区是网址则作为地址，否则作为文字；选中需要用户填写的部分。</summary>
    public static TextEdit InsertLink(string text, int start, int length, bool image = false)
    {
        var selected = text.Substring(start, length);
        var bang = image ? "!" : "";
        if (LooksLikeUrl(selected))
        {
            var label = image ? "图片描述" : "链接文字";
            return new TextEdit(start, length, $"{bang}[{label}]({selected})", start + bang.Length + 1, label.Length);
        }
        var display = length > 0 ? selected : (image ? "图片描述" : "链接文字");
        const string url = "https://";
        var insert = $"{bang}[{display}]({url})";
        return length > 0
            ? new TextEdit(start, length, insert, start + bang.Length + display.Length + 3, url.Length)
            : new TextEdit(start, 0, insert, start + bang.Length + 1, display.Length);
    }

    private static bool LooksLikeUrl(string s) =>
        s.Length > 0 && !s.Contains(' ') && !s.Contains('\n')
        && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));

    /// <summary>把选区覆盖的各行设为 <paramref name="level"/> 级标题（0 = 正文）；已是同级则取消。</summary>
    public static TextEdit SetHeading(string text, int start, int length, int level)
    {
        var (lineStart, lineEnd) = LineSpan(text, start, length);
        var lines = text[lineStart..lineEnd].Split('\n');
        var allSame = level > 0 && lines.All(l => HeadingLevel(l) == level);
        var target = allSame ? 0 : level;
        var prefix = target > 0 ? new string('#', target) + " " : "";
        var result = string.Join('\n', lines.Select(l => l.Length == 0 && lines.Length > 1 ? l : prefix + StripHeading(l)));
        return LineEdit(text, lineStart, lineEnd, result, start, length, lines.Length == 1);
    }

    public static int HeadingLevel(string line)
    {
        var n = 0;
        while (n < line.Length && n < 7 && line[n] == '#')
        {
            n++;
        }
        return n is >= 1 and <= 6 && (n == line.Length || line[n] == ' ') ? n : 0;
    }

    private static string StripHeading(string line)
    {
        var level = HeadingLevel(line);
        return level == 0 ? line : line[Math.Min(line.Length, level + 1)..];
    }

    public enum LinePrefix { Quote, Bullet, Ordered, Task }

    /// <summary>引用 / 无序 / 有序 / 任务列表：各行都已有该前缀则去掉，否则（替换其它列表前缀后）加上。</summary>
    public static TextEdit ToggleLinePrefix(string text, int start, int length, LinePrefix kind)
    {
        var (lineStart, lineEnd) = LineSpan(text, start, length);
        var lines = text[lineStart..lineEnd].Split('\n');
        var content = lines.Where(l => l.Trim().Length > 0).ToArray();
        var allHave = content.Length > 0 && content.All(l => HasPrefix(l, kind));
        var sb = new StringBuilder();
        var number = 1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            var line = lines[i];
            if (line.Trim().Length == 0 && lines.Length > 1)
            {
                sb.Append(line);
                continue;
            }
            if (allHave)
            {
                sb.Append(RemovePrefix(line, kind));
                continue;
            }
            var indent = line.Length - line.TrimStart().Length;
            var body = kind == LinePrefix.Quote ? line : StripListMarker(line.TrimStart());
            var marker = kind switch
            {
                LinePrefix.Quote => "> ",
                LinePrefix.Bullet => "- ",
                LinePrefix.Ordered => $"{number++}. ",
                _ => "- [ ] ",
            };
            sb.Append(kind == LinePrefix.Quote ? marker + body : line[..indent] + marker + body);
        }
        return LineEdit(text, lineStart, lineEnd, sb.ToString(), start, length, lines.Length == 1);
    }

    private static bool HasPrefix(string line, LinePrefix kind) => kind switch
    {
        LinePrefix.Quote => line.StartsWith('>'),
        LinePrefix.Task => ParseListItem(line) is { Task: true },
        LinePrefix.Bullet => ParseListItem(line) is { Ordered: false, Task: false },
        _ => ParseListItem(line) is { Ordered: true },
    };

    private static string RemovePrefix(string line, LinePrefix kind)
    {
        if (kind == LinePrefix.Quote)
        {
            return line.StartsWith("> ", StringComparison.Ordinal) ? line[2..] : line[1..];
        }
        var indent = line.Length - line.TrimStart().Length;
        return line[..indent] + StripListMarker(line.TrimStart());
    }

    private static string StripListMarker(string trimmed)
    {
        var item = ParseListItem(trimmed);
        return item is null ? trimmed : trimmed[(item.Value.MarkerLength)..];
    }

    /// <summary>围栏代码块：包住选区覆盖的各行（没有选区时插入空代码块），并选中语言位置。</summary>
    public static TextEdit InsertCodeBlock(string text, int start, int length)
    {
        if (length == 0)
        {
            return InsertBlock(text, start, "```\n\n```", selectFrom: 3, selectLength: 0);
        }
        var (lineStart, lineEnd) = LineSpan(text, start, length);
        var body = text[lineStart..lineEnd];
        var fence = body.Contains("```", StringComparison.Ordinal) ? "````" : "```";
        var replaced = $"{fence}\n{body}\n{fence}";
        return new TextEdit(lineStart, lineEnd - lineStart, replaced, lineStart + fence.Length, 0);
    }

    /// <summary>在光标处插入一个块级片段（表格、公式、图表…），前后补足空行；选中片段内的 [selectFrom, +selectLength)。</summary>
    public static TextEdit InsertBlock(string text, int caret, string snippet, int selectFrom = 0, int selectLength = 0)
    {
        // 光标在行中间时插在本行之后
        var at = caret;
        if (at > 0 && at < text.Length && text[at - 1] != '\n')
        {
            var next = text.IndexOf('\n', at);
            at = next < 0 ? text.Length : next;
        }
        else if (at == text.Length && at > 0 && text[at - 1] != '\n')
        {
            at = text.Length;
        }
        var before = at == 0 ? "" : at >= 2 && text[at - 1] == '\n' && text[at - 2] == '\n' ? "" : text[at - 1] == '\n' ? "\n" : "\n\n";
        var after = at < text.Length && text[at] == '\n' ? (at + 1 < text.Length && text[at + 1] == '\n' ? "" : "\n") : at < text.Length ? "\n\n" : "\n";
        var insert = before + snippet + after;
        return new TextEdit(at, 0, insert, at + before.Length + selectFrom, selectLength);
    }

    public static class Snippets
    {
        public const string Table = "| 列 1 | 列 2 | 列 3 |\n|:-----|:----:|-----:|\n| 内容 | 内容 | 内容 |\n| 内容 | 内容 | 内容 |";
        public const string MathBlock = "$$\n\\int_0^1 x^2\\,dx = \\frac{1}{3}\n$$";
        public const string Mermaid = "```mermaid\nflowchart LR\n    A[开始] --> B{判断}\n    B -->|是| C[处理]\n    B -->|否| D[结束]\n```";
        public const string Details = "<details>\n<summary>标题</summary>\n\n折叠的内容\n</details>";
        public const string Alert = "> [!NOTE]\n> 提示内容";
        public const string Rule = "---";
    }

    /// <summary>
    /// 列表回车续写：在列表项（含引用里的列表）末尾回车时插入下一项的标记；空项回车则删除标记、退出列表。
    /// 不是列表行时返回 null，交给编辑器默认处理。
    /// </summary>
    public static TextEdit? ContinueList(string text, int caret)
    {
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var lineEndIndex = text.IndexOf('\n', caret);
        var lineEnd = lineEndIndex < 0 ? text.Length : lineEndIndex;
        var line = text[lineStart..lineEnd];
        var quote = QuotePrefix(line);
        var rest = line[quote.Length..];
        var item = ParseListItem(rest);
        if (item is null)
        {
            // 引用里回车续写引用标记
            if (quote.Length > 0)
            {
                if (rest.Trim().Length == 0)
                {
                    return new TextEdit(lineStart, line.Length, "", lineStart, 0);
                }
                return new TextEdit(caret, 0, "\n" + quote, caret + 1 + quote.Length, 0);
            }
            return null;
        }
        var it = item.Value;
        if (caret < lineStart + quote.Length + it.MarkerLength)
        {
            return null;
        }
        // 空项：退出列表（嵌套项则先减少一级缩进）
        if (rest[it.MarkerLength..].Trim().Length == 0)
        {
            if (it.Indent.Length > 0)
            {
                var outdented = quote + it.Indent[Math.Min(it.Indent.Length, IndentUnit(it.Indent))..] + rest.TrimStart();
                return new TextEdit(lineStart, line.Length, outdented, lineStart + outdented.Length, 0);
            }
            return new TextEdit(lineStart, line.Length, quote.TrimEnd(), lineStart + quote.TrimEnd().Length, 0);
        }
        var marker = it.Ordered ? $"{it.Number + 1}{it.Delimiter} " : $"{it.Bullet} ";
        if (it.Task)
        {
            marker += "[ ] ";
        }
        var insert = "\n" + quote + it.Indent + marker;
        // 光标后的文字跟随到新项（去掉前导空白）
        var tail = text[caret..lineEnd];
        var trimmedTail = tail.TrimStart();
        var removed = tail.Length - trimmedTail.Length;
        return new TextEdit(caret, removed, insert, caret + insert.Length, 0);
    }

    private static int IndentUnit(string indent) => indent.StartsWith('\t') ? 1 : Math.Min(indent.Length, 2);

    private static string QuotePrefix(string line)
    {
        var i = 0;
        while (i < line.Length && line[i] == '>')
        {
            i++;
            if (i < line.Length && line[i] == ' ')
            {
                i++;
            }
        }
        return line[..i];
    }

    public readonly record struct ListItem(string Indent, bool Ordered, char Bullet, int Number, char Delimiter, bool Task, int MarkerLength);

    /// <summary>解析行首的列表标记；<see cref="ListItem.MarkerLength"/> 含缩进、标记、空格与任务框。</summary>
    public static ListItem? ParseListItem(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }
        var indent = line[..i];
        if (i >= line.Length)
        {
            return null;
        }
        bool ordered;
        char bullet = '-', delimiter = '.';
        var number = 0;
        if (line[i] is '-' or '*' or '+')
        {
            ordered = false;
            bullet = line[i];
            i++;
        }
        else if (char.IsAsciiDigit(line[i]))
        {
            var digits = i;
            while (i < line.Length && char.IsAsciiDigit(line[i]) && i - digits < 9)
            {
                i++;
            }
            if (i >= line.Length || (line[i] != '.' && line[i] != ')'))
            {
                return null;
            }
            number = int.Parse(line.AsSpan(digits, i - digits));
            delimiter = line[i];
            ordered = true;
            i++;
        }
        else
        {
            return null;
        }
        if (i < line.Length && line[i] != ' ')
        {
            return null;
        }
        // 「---」「***」是分隔线，不是列表
        if (!ordered && line.AsSpan(indent.Length).Trim().ToString() is { Length: >= 3 } t && t.All(c => c == bullet || c == ' '))
        {
            return null;
        }
        if (i < line.Length)
        {
            i++;
        }
        var task = false;
        if (line.Length >= i + 3 && line[i] == '[' && (line[i + 1] is ' ' or 'x' or 'X') && line[i + 2] == ']')
        {
            task = true;
            i += 3;
            if (i < line.Length && line[i] == ' ')
            {
                i++;
            }
        }
        return new ListItem(indent, ordered, bullet, number, delimiter, task, i);
    }

    /// <summary>Tab / Shift+Tab：增减选区所在各行的缩进（两个空格）。</summary>
    public static TextEdit Indent(string text, int start, int length, bool outdent)
    {
        var (lineStart, lineEnd) = LineSpan(text, start, length);
        var lines = text[lineStart..lineEnd].Split('\n');
        var firstDelta = 0;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            var line = lines[i];
            int delta;
            if (outdent)
            {
                var remove = line.StartsWith('\t') ? 1 : line.StartsWith("  ", StringComparison.Ordinal) ? 2 : line.StartsWith(' ') ? 1 : 0;
                sb.Append(line[remove..]);
                delta = -remove;
            }
            else
            {
                sb.Append("  ").Append(line);
                delta = 2;
            }
            if (i == 0)
            {
                firstDelta = delta;
            }
        }
        var replaced = sb.ToString();
        var newStart = Math.Max(lineStart, start + firstDelta);
        var newEnd = start + length + (replaced.Length - (lineEnd - lineStart));
        return new TextEdit(lineStart, lineEnd - lineStart, replaced, newStart, Math.Max(0, newEnd - newStart));
    }

    /// <summary>选区覆盖的整行范围 [行首, 行尾)（不含最后的换行）。</summary>
    public static (int Start, int End) LineSpan(string text, int start, int length)
    {
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var endAnchor = start + length;
        // 选区刚好停在下一行行首时不算那一行
        if (length > 0 && endAnchor > 0 && text[endAnchor - 1] == '\n')
        {
            endAnchor--;
        }
        var lineEndIndex = text.IndexOf('\n', Math.Max(endAnchor, lineStart));
        return (lineStart, lineEndIndex < 0 ? text.Length : lineEndIndex);
    }

    // 整行替换后的选区：单行时保持光标相对行尾的位置，多行时选中整个结果
    private static TextEdit LineEdit(string text, int lineStart, int lineEnd, string replaced, int start, int length, bool singleLine)
    {
        if (singleLine)
        {
            var delta = replaced.Length - (lineEnd - lineStart);
            var caret = Math.Clamp(start + length + delta, lineStart, lineStart + replaced.Length);
            var selStart = length == 0 ? caret : Math.Clamp(start + delta, lineStart, caret);
            return new TextEdit(lineStart, lineEnd - lineStart, replaced, selStart, caret - selStart);
        }
        return new TextEdit(lineStart, lineEnd - lineStart, replaced, lineStart, replaced.Length);
    }
}
