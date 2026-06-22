using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace TransparentTwitchChatWPF.ChatProviders;

public class BlazeChatProvider : IChatProvider
{
    private static readonly string BlazeHostname = "blazechat.overlay";

    public Uri GetNavigationUri()
    {
        return new Uri($"https://{BlazeHostname}/blaze-chat/index.html");
    }

    public Task ConfigureAsync(CoreWebView2 coreWebView2)
    {
        var payload = new
        {
            Channel = App.Settings.GeneralSettings.BlazeChannel,
            ClientId = App.Settings.GeneralSettings.BlazeClientId,
            AccessToken = App.Settings.GeneralSettings.BlazeAccessToken,
            FadeTimeout = App.Settings.GeneralSettings.FadeChat
                ? int.TryParse(App.Settings.GeneralSettings.FadeTime, out var ft) ? ft : 0
                : 0
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var message = new { Type = "blazeConfig", Payload = payload };
        coreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, options));

        return Task.CompletedTask;
    }
}
