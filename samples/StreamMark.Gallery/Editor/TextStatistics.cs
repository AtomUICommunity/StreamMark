namespace StreamMark.Gallery.Editor;

/// <summary>状态栏的字数统计：中日韩字符每字算一个词，其它按空白与标点分隔的单词计。</summary>
public readonly record struct TextStatistics(int Words, int Characters, int Lines)
{
    /// <summary>阅读时间（分钟，至少 1）：中文约 400 字 / 分钟，英文约 200 词 / 分钟，混排按词数折中取 300。</summary>
    public int ReadingMinutes => Math.Max(1, (int)Math.Ceiling(Words / 300.0));

    public static TextStatistics Compute(string text)
    {
        int words = 0, chars = 0, lines = text.Length == 0 ? 0 : 1;
        var inWord = false;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
                continue;
            }
            chars++;
            if (IsCjk(c))
            {
                words++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }
        return new TextStatistics(words, chars, lines);
    }

    private static bool IsCjk(char c) =>
        c is (>= '一' and <= '鿿') or (>= '㐀' and <= '䶿') or (>= '぀' and <= 'ヿ') or (>= '가' and <= '힯') or (>= '豈' and <= '﫿');
}
