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
            Debug.WriteLine("Blaze: Could not obtain access token. Is the Client Secret configured?");
            // Still send the config so the JS can show an error
        }

        var payload = new
        {
            Channel = App.Settings.GeneralSettings.BlazeChannel,
            ClientId = BlazeCredentialManager.ClientId,
            AccessToken = accessToken ?? "",
            FadeTimeout = App.Settings.GeneralSettings.FadeChat
                ? int.TryParse(App.Settings.GeneralSettings.FadeTime, out var ft) ? ft : 0
                : 0,
            TextSize = App.Settings.GeneralSettings.BlazeTextSize
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var message = new { Type = "blazeConfig", Payload = payload };
        coreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, options));
    }
}
