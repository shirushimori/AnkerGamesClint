using System.Text.Json.Serialization;

namespace AnkerGamesClient.Models;

public class GameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("exe")]
    public string ExePath { get; set; } = string.Empty;

    /// <summary>Local path to the scraped banner image (assets/game.png etc.).</summary>
    [JsonPropertyName("banner_path")]
    public string BannerPath { get; set; } = string.Empty;

    /// <summary>The page URL the download originated from, used for banner scraping.</summary>
    [JsonPropertyName("page_url")]
    public string PageUrl { get; set; } = string.Empty;
}
