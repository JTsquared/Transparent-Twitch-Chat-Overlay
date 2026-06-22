using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;
using TransparentTwitchChatWPF.Blaze;

namespace TransparentTwitchChatWPF.ChatProviders;

public class BlazeChatProvider : IChatProvider
{
    private static readonly string BlazeHostname = "blazechat.overlay";
    private static readonly BlazeTokenService TokenService = new();

    public Uri GetNavigationUri()
    {
        return new Uri($"https://{BlazeHostname}/blaze-chat/index.html");
    }

    public async Task ConfigureAsync(CoreWebView2 coreWebView2)
    {
        string? accessToken = await TokenService.GetAccessTokenAsync();

        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.WriteLine("Blaze: Could not obtain access token.");
        }

        var s = App.Settings.GeneralSettings;

        var payload = new
        {
            Channel = s.BlazeChannel,
            TwitchChannel = s.TwitchChannel,
            KickChannel = s.KickChannel,
            ArenaChannel = s.ArenaChannel,
            ClientId = BlazeCredentialManager.ClientId,
            AccessToken = accessToken ?? "",
            TextSize = s.BlazeTextSize,
            FontFamily = s.BlazeFontFamily,
            TextColor = s.BlazeTextColor,
            BgEnabled = s.BlazeBgEnabled,
            BgOpacity = s.BlazeBgOpacity,
            FadeTimeout = s.BlazeFadeTimeout
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var message = new { Type = "blazeConfig", Payload = payload };
        coreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, options));
    }
}
