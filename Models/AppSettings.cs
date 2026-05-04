using System.Text.Json.Serialization;

namespace AnkerGamesClient.Models;

public class AppSettings
{
    [JsonPropertyName("install_path")]
    public string InstallPath { get; set; } = @"C:\games\AnkerGames";

    [JsonPropertyName("desktop_shortcut")]
    public bool DesktopShortcut { get; set; } = true;

    [JsonPropertyName("start_shortcut")]
    public bool StartShortcut { get; set; } = true;

    [JsonPropertyName("seven_zip_path")]
    public string SevenZipPath { get; set; } = @"C:\Program Files\7-Zip\7z.exe";

    [JsonPropertyName("qbittorrent_path")]
    public string QBittorrentPath { get; set; } = @"C:\Program Files\qBittorrent\qbittorrent.exe";
}
