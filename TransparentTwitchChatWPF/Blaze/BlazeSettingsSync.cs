using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentTwitchChatWPF.Blaze;

internal class BlazeSettingsSync
{
    private const string SettingsBaseUrl = "https://blazegames.store/suco/api/settings/";
    private static readonly HttpClient HttpClient = new();

    public async Task<BlazeOverlaySettings?> LoadFromServerAsync(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;

        try
        {
            var response = await HttpClient.GetAsync(SettingsBaseUrl + Uri.EscapeDataString(channel));
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<BlazeOverlaySettings>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load Blaze settings from server: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SaveToServerAsync(string channel, BlazeOverlaySettings settings)
    {
        if (string.IsNullOrWhiteSpace(channel)) return false;

        try
        {
            var json = JsonSerializer.Serialize(settings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(SettingsBaseUrl + Uri.EscapeDataString(channel), content);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Failed to save Blaze settings to server ({response.StatusCode}): {err}");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save Blaze settings to server: {ex.Message}");
            return false;
        }
    }
}

internal class BlazeOverlaySettings
{
    [JsonPropertyName("bgEnabled")]
    public bool BgEnabled { get; set; } = false;

    [JsonPropertyName("bgOpacity")]
    public int BgOpacity { get; set; } = 50;

    [JsonPropertyName("textSize")]
    public int TextSize { get; set; } = 18;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Noto Sans";

    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#ffffff";

    [JsonPropertyName("overlayWidth")]
    public int OverlayWidth { get; set; } = 400;

    [JsonPropertyName("overlayHeight")]
    public int OverlayHeight { get; set; } = 600;

    [JsonPropertyName("fadeTimeout")]
    public int FadeTimeout { get; set; } = 0;
}
