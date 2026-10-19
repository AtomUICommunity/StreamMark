using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamMark.Gallery;

public enum ThemeChoice { System, Light, Dark }

public enum ViewMode { Split, Editor, Preview }

/// <summary>用户设置：保存在应用数据目录的 settings.json（源生成 JSON，NativeAOT 安全）。</summary>
public sealed class GallerySettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public ViewMode ViewMode { get; set; } = ViewMode.Split;
    public bool WordWrap { get; set; } = true;
    public bool ShowLineNumbers { get; set; } = true;
    public bool ShowOutline { get; set; } = true;
    public bool ScrollSync { get; set; } = true;
    public double EditorFontSize { get; set; } = 14;
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 860;
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>冒烟测试关闭持久化，避免读写用户目录。</summary>
    public static bool Persist { get; set; } = true;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamMark.Gallery", "settings.json");

    public static GallerySettings Load()
    {
        if (!Persist)
        {
            return new GallerySettings();
        }
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), GallerySettingsContext.Default.GallerySettings) ?? new GallerySettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 损坏或不可读的设置文件按默认值处理
        }
        return new GallerySettings();
    }

    public void Save()
    {
        if (!Persist)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, GallerySettingsContext.Default.GallerySettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不了设置不影响使用
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GallerySettings))]
internal sealed partial class GallerySettingsContext : JsonSerializerContext;
