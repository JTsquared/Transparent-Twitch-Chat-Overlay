using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TransparentTwitchChatWPF.Blaze;
using TransparentTwitchChatWPF.Utils;
using Path = System.IO.Path;

namespace TransparentTwitchChatWPF.View.Settings;

/// <summary>
/// Interaction logic for ChatSettingsPage.xaml
/// </summary>
public partial class ChatSettingsPage : UserControl
{
    public event Action TwitchConnectionPageRequested;
    public event Action AppearancePageRequested;
    public event Action RestoreNativeChatDefaultsRequested;

    public ChatSettingsPage()
    {
        InitializeComponent();

        tbPopoutCSS.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("CSS");
        tbCSS2.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("CSS");
    }

    public void SetupValues()
    {
        LoadSoundClips();
        var comboxBoxItem = comboChatSound.Items.OfType<ComboBoxItem>().
            FirstOrDefault(x => x.Content.ToString() == App.Settings.GeneralSettings.ChatNotificationSound);
        if (comboxBoxItem == null)
            this.comboChatSound.SelectedIndex = 0;
        else
            this.comboChatSound.SelectedIndex = this.comboChatSound.Items.IndexOf(comboxBoxItem);

        this.comboChatSound2.SelectedIndex = this.comboChatSound.SelectedIndex;

        this.tbUsername.Text = App.Settings.GeneralSettings.Username;
        this.tb_nativeChatUsername.Text = App.Settings.jChatSettings.Channel;
        this.tbUsername2.Text = App.Settings.GeneralSettings.Username;
        this.tbTwitchPopoutUsername.Text = App.Settings.GeneralSettings.Username;
        this.cbRedemptions.IsChecked = App.Settings.GeneralSettings.RedemptionsEnabled;
        this.cbRedemptions2.IsChecked = App.Settings.GeneralSettings.RedemptionsEnabled;
        this.btGetChannelID.IsEnabled = App.Settings.GeneralSettings.RedemptionsEnabled;
        this.btGetChannelID2.IsEnabled = App.Settings.GeneralSettings.RedemptionsEnabled;
        this.tbUsername2.IsEnabled = App.Settings.GeneralSettings.RedemptionsEnabled;
        this.cbFade.IsChecked = App.Settings.GeneralSettings.FadeChat;
        this.tbFadeTime.Text = App.Settings.GeneralSettings.FadeTime;
        this.tbFadeTime.IsEnabled = App.Settings.GeneralSettings.FadeChat;

        //this.cbBotActivity.IsChecked = App.Settings.GeneralSettings.ShowBotActivity;
        this.comboTheme.SelectedIndex = App.Settings.GeneralSettings.ThemeIndex;

        // Twitch Popout Chat settings
        LoadTwitchPopoutCssSettings();

        this.cbBetterTtv.IsChecked = App.Settings.GeneralSettings.BetterTtv;
        this.cbBetterTtv_7tv.IsChecked = App.Settings.GeneralSettings.BetterTtv_7tv;
        this.cbBetterTtv_AdvMenu.IsChecked = App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu;
        this.cbFfz.IsChecked = App.Settings.GeneralSettings.FrankerFaceZ;


        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
        {
            var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;

            // Hide all panels first, then show the active one
            this.kapChatGrid.Visibility = Visibility.Hidden;
            this.twitchPopoutChat.Visibility = Visibility.Hidden;
            this.customURLGrid.Visibility = Visibility.Hidden;
            this.jChatGrid.Visibility = Visibility.Hidden;
            this.blazeChatGrid.Visibility = Visibility.Hidden;

            if (chatType == ChatTypes.NativeChat)
            {
                this.jChatGrid.Visibility = Visibility.Visible;
            }
            else if (chatType == ChatTypes.CustomURL)
            {
                this.customURLGrid.Visibility = Visibility.Visible;
                this.tbURL.Text = App.Settings.GeneralSettings.CustomURL;
                this.tbCSS2.Text = App.Settings.GeneralSettings.CustomCSS;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                this.twitchPopoutChat.Visibility = Visibility.Visible;
            }
            else if (chatType == ChatTypes.KapChat)
            {
                this.kapChatGrid.Visibility = Visibility.Visible;
                this.tbURL.Text = string.Empty;

                if (string.IsNullOrEmpty(App.Settings.GeneralSettings.CustomCSS))
                {
                    this.tbCSS.Text = CustomCSS_Defaults.NoneTheme_CustomCSS;
                }
                else
                {
                    this.tbCSS.Text = App.Settings.GeneralSettings.CustomCSS;
                }
            }
            else if (chatType == ChatTypes.BlazeChat)
            {
                this.blazeChatGrid.Visibility = Visibility.Visible;
                LoadBlazeSettingsToUI();
            }
        }
    }

    public void SaveValues()
    {
        //this.config.RedemptionsEnabled = false;

        if (Enum.IsDefined(typeof(ChatTypes), App.Settings.GeneralSettings.ChatType))
        {
            var chatType = (ChatTypes)App.Settings.GeneralSettings.ChatType;

            if (chatType == ChatTypes.CustomURL)
            {
                App.Settings.GeneralSettings.CustomURL = this.tbURL.Text;

                if (!string.IsNullOrWhiteSpace(this.tbCSS2.Text) && !string.IsNullOrEmpty(this.tbCSS2.Text)
                    && (this.tbCSS2.Text.ToLower() != "css"))
                {
                    App.Settings.GeneralSettings.CustomCSS = this.tbCSS2.Text;
                }
                else
                    App.Settings.GeneralSettings.CustomCSS = string.Empty;
            }
            else if (chatType == ChatTypes.TwitchPopout)
            {
                if (string.IsNullOrEmpty(this.tbTwitchPopoutUsername.Text) || string.IsNullOrWhiteSpace(this.tbTwitchPopoutUsername.Text))
                {
                    this.tbTwitchPopoutUsername.Text = "username";
                }
                App.Settings.GeneralSettings.Username = this.tbTwitchPopoutUsername.Text;

                if (this.cbUseDefaultPopoutCSS.IsChecked ?? false)
                {
                    App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS = true;
                }
                else
                {
                    App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS = false;
                    App.Settings.GeneralSettings.TwitchPopoutCSS = this.tbPopoutCSS.Text;
                }

                App.Settings.GeneralSettings.BetterTtv = this.cbBetterTtv.IsChecked ?? false;
                App.Settings.GeneralSettings.BetterTtv_7tv = this.cbBetterTtv_7tv.IsChecked ?? false;
                App.Settings.GeneralSettings.BetterTtv_AdvEmoteMenu = this.cbBetterTtv_AdvMenu.IsChecked ?? false;
                App.Settings.GeneralSettings.FrankerFaceZ = this.cbFfz.IsChecked ?? false;
            }
            else if (chatType == ChatTypes.KapChat)
            {
                App.Settings.GeneralSettings.Username = this.tbUsername.Text;
                App.Settings.GeneralSettings.RedemptionsEnabled = this.cbRedemptions.IsChecked ?? false;
                App.Settings.GeneralSettings.FadeChat = this.cbFade.IsChecked ?? false;
                App.Settings.GeneralSettings.FadeTime = this.tbFadeTime.Text;
                //App.Settings.GeneralSettings.ShowBotActivity = this.cbBotActivity.IsChecked ?? false;
                App.Settings.GeneralSettings.ChatNotificationSound = this.comboChatSound.SelectedValue.ToString();
                App.Settings.GeneralSettings.ThemeIndex = this.comboTheme.SelectedIndex;

                if (App.Settings.GeneralSettings.ThemeIndex == 0)
                {
                    App.Settings.GeneralSettings.CustomCSS = this.tbCSS.Text;
                }
            }
            else if (chatType == ChatTypes.NativeChat)
            {
                App.Settings.GeneralSettings.Username = this.tb_nativeChatUsername.Text;
                App.Settings.jChatSettings.Channel = this.tb_nativeChatUsername.Text;
                App.Settings.GeneralSettings.jChatURL = string.Empty;
                App.Settings.GeneralSettings.RedemptionsEnabled = this.cbRedemptions2.IsChecked ?? false;
                if (App.Settings.GeneralSettings.RedemptionsEnabled)
                    App.Settings.GeneralSettings.Username = this.tbUsername2.Text;
                App.Settings.GeneralSettings.ChatNotificationSound = this.comboChatSound2.SelectedValue.ToString();
            }
            else if (chatType == ChatTypes.BlazeChat)
            {
                SaveBlazeSettingsFromUI();
            }
        }
    }

    public void OnTwitchConnectionStatusChanged(TwitchConnectionStatus twitchConnectionStatus)
    {
        lblTwitchConnected.Foreground = Brushes.Blue;
        lblTwitchConnected2.Foreground = Brushes.Blue;

        Visibility getChannelButtonVisibility = Visibility.Hidden;

        if (twitchConnectionStatus.StatusState == TwitchConnectionStatusState.NotConnected)
        {
            getChannelButtonVisibility = Visibility.Visible;

            lblTwitchConnected.Foreground = Brushes.Gray;
            lblTwitchConnected2.Foreground = Brushes.Gray;
        }
        else if (twitchConnectionStatus.StatusState == TwitchConnectionStatusState.Active)
        {
            lblTwitchConnected.Foreground = Brushes.Green;
            lblTwitchConnected2.Foreground = Brushes.Green;
        }
        else if (twitchConnectionStatus.StatusState == TwitchConnectionStatusState.Inactive)
        {
            lblTwitchConnected.Foreground = Brushes.Gray;
            lblTwitchConnected2.Foreground = Brushes.Gray;
        }
        else if (twitchConnectionStatus.StatusState == TwitchConnectionStatusState.Error)
        {
            getChannelButtonVisibility = Visibility.Visible;

            lblTwitchConnected.Foreground = Brushes.Red;
            lblTwitchConnected2.Foreground = Brushes.Red;
        }

        btGetChannelID.Visibility = getChannelButtonVisibility;
        btGetChannelID2.Visibility = getChannelButtonVisibility;

        lblTwitchConnected.Content = twitchConnectionStatus.Message;
        lblTwitchConnected2.Content = twitchConnectionStatus.Message;
    }

    private string GetSoundClipsFolder()
    {
        string path = App.Settings.GeneralSettings.SoundClipsFolder;

        string baseDirectory = AppContext.BaseDirectory;
        string defaultAssetsPath = Path.Combine(baseDirectory, "assets");

        if (path == "Default" || !Directory.Exists(path))
        {
            path = defaultAssetsPath;
        }

        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create directory '{path}': {ex.Message}");
                // Handle the error appropriately, maybe fall back to a known good path or show a message
            }
        }

        Debug.WriteLine($"Sound Clips Folder: '{path}'");

        return path;
    }

    public void LoadSoundClips()
    {
        comboChatSound.Items.Clear();
        comboChatSound2.Items.Clear();

        comboChatSound.Items.Add(new ComboBoxItem() { Content = "None" });
        comboChatSound2.Items.Add(new ComboBoxItem() { Content = "None" });

        comboChatSound.SelectedIndex = 0;
        comboChatSound2.SelectedIndex = 0;

        string path = GetSoundClipsFolder();

        if (!Directory.Exists(path)) return;

        string[] filesWav = Directory.GetFiles(path, "*.wav");
        string[] filesMp3 = Directory.GetFiles(path, "*.mp3");

        foreach (string file in filesWav)
        {
            string fileName = Path.GetFileName(file);
            comboChatSound.Items.Add(new ComboBoxItem() { Content = fileName });
            comboChatSound2.Items.Add(new ComboBoxItem() { Content = fileName });
        }

        foreach (string file in filesMp3)
        {
            string fileName = Path.GetFileName(file);
            comboChatSound.Items.Add(new ComboBoxItem() { Content = fileName });
            comboChatSound2.Items.Add(new ComboBoxItem() { Content = fileName });
        }
    }

    private void PlayAudioFile(string file)
    {
        if (File.Exists(file))
        {
            var audioFileReader = new AudioFileReader(file);
            {
                audioFileReader.Volume = App.Settings.GeneralSettings.OutputVolume;
                var waveOutDevice = new WaveOutEvent();
                {
                    waveOutDevice.Init(audioFileReader);
                    waveOutDevice.PlaybackStopped += (s, e) =>
                    {
                        audioFileReader.Dispose();
                        waveOutDevice.Dispose();
                    };
                    waveOutDevice.Play();
                }
            }
        }
    }

    public void ChatTypeChanged(ChatTypes chatType)
    {
        App.Settings.GeneralSettings.ChatType = (int)chatType;

        // Hide all panels first
        this.kapChatGrid.Visibility = Visibility.Hidden;
        this.customURLGrid.Visibility = Visibility.Hidden;
        this.twitchPopoutChat.Visibility = Visibility.Hidden;
        this.jChatGrid.Visibility = Visibility.Hidden;
        this.blazeChatGrid.Visibility = Visibility.Hidden;

        switch (chatType)
        {
            case ChatTypes.NativeChat:
                this.jChatGrid.Visibility = Visibility.Visible;
                break;
            case ChatTypes.TwitchPopout:
                this.twitchPopoutChat.Visibility = Visibility.Visible;
                LoadTwitchPopoutCssSettings();
                break;
            case ChatTypes.KapChat:
                this.kapChatGrid.Visibility = Visibility.Visible;
                break;
            case ChatTypes.CustomURL:
                this.customURLGrid.Visibility = Visibility.Visible;
                break;
            case ChatTypes.BlazeChat:
                this.blazeChatGrid.Visibility = Visibility.Visible;
                LoadBlazeSettingsToUI();
                break;
        }
    }

    // --- Event Handlers --------------------------------------------------------------------------------

    private void cbRedemptions_Unchecked(object sender, RoutedEventArgs e)
    {
        bool isActive = false;

        if (!string.IsNullOrEmpty(App.Settings.GeneralSettings.ChannelID) &&
            !string.IsNullOrEmpty(App.Settings.GeneralSettings.OAuthToken))
        {
            isActive = true;
        }

        string status = "Connected (Inactive)";
        if (!isActive) status = "Not Connected";

        this.lblTwitchConnected.Content = status;
        this.lblTwitchConnected.Foreground = Brushes.Gray;
        this.btGetChannelID.Visibility = Visibility.Hidden;

        this.lblTwitchConnected2.Content = status;
        this.lblTwitchConnected2.Foreground = Brushes.Gray;
        this.btGetChannelID2.Visibility = Visibility.Hidden;
    }

    private void comboChatSound_DropDownClosed(object sender, EventArgs e)
    {
        string file = Path.Combine(GetSoundClipsFolder(), this.comboChatSound.SelectedValue.ToString());

        PlayAudioFile(file);
    }

    private void comboChatSound_DropDownClosed2(object sender, EventArgs e)
    {
        string file = Path.Combine(GetSoundClipsFolder(), this.comboChatSound2.SelectedValue.ToString());

        PlayAudioFile(file);
    }

    private void cbRedemptions_Checked(object sender, RoutedEventArgs e)
    {
        bool isActive = false;

        if (!string.IsNullOrEmpty(App.Settings.GeneralSettings.ChannelID) &&
            !string.IsNullOrEmpty(App.Settings.GeneralSettings.OAuthToken))
        {
            isActive = true;
        }

        string status = "Connected (Active)";
        if (!isActive) status = "Not Connected";

        this.lblTwitchConnected.Content = status;
        this.lblTwitchConnected.Foreground = isActive ? Brushes.Green : Brushes.Gray;
        this.btGetChannelID.Visibility = isActive ? Visibility.Hidden : Visibility.Visible;
        this.btGetChannelID.IsEnabled = !isActive;

        this.lblTwitchConnected2.Content = status;
        this.lblTwitchConnected2.Foreground = isActive ? Brushes.Green : Brushes.Gray;
        this.btGetChannelID2.Visibility = isActive ? Visibility.Hidden : Visibility.Visible;
        this.btGetChannelID2.IsEnabled = !isActive;

        App.Settings.GeneralSettings.RedemptionsEnabled = cbRedemptions.IsChecked ?? false;
    }

    private void comboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (comboTheme.SelectedIndex == 0)
        {
            tbCSS.Visibility = Visibility.Visible;
            lblCSS.Visibility = Visibility.Visible;
        }
        else
        {
            tbCSS.Visibility = Visibility.Hidden;
            lblCSS.Visibility = Visibility.Hidden;
        }
    }

    private void cbFade_Checked(object sender, RoutedEventArgs e)
    {
        this.tbFadeTime.IsEnabled = true;
    }

    private void cbFade_Unchecked(object sender, RoutedEventArgs e)
    {
        this.tbFadeTime.IsEnabled = false;
    }

    private void btGetChannelID_Click(object sender, RoutedEventArgs e)
    {
        TwitchConnectionPageRequested?.Invoke();
    }

    private void btOpenChatFilterSettings_Click(object sender, RoutedEventArgs e)
    {
        ChatFilters chatFiltersWindow = new ChatFilters();
        chatFiltersWindow.ShowDialog();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        ShellHelper.OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void btRestoreNativeChatDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will overwrite all NativeChat overlay files with the built-in defaults.\n\n" +
            "Any direct edits you made to those files on disk will be lost.\n\nContinue?",
            "Restore NativeChat Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            RestoreNativeChatDefaultsRequested?.Invoke();
    }

    private void HyperlinkButtonAppearanceSettings_Click(object sender, RoutedEventArgs e)
    {
        AppearancePageRequested?.Invoke();
    }
    
    private void cbUseDefaultPopoutCSS_Checked(object sender, RoutedEventArgs e)
    {
        tbPopoutCSS.Text = CustomCSS_Defaults.TwitchPopoutChat;
        tbPopoutCSS.IsReadOnly = true;
    }

    private void cbUseDefaultPopoutCSS_Unchecked(object sender, RoutedEventArgs e)
    {
        tbPopoutCSS.Text = string.IsNullOrEmpty(App.Settings.GeneralSettings.TwitchPopoutCSS)
            ? CustomCSS_Defaults.TwitchPopoutChat
            : App.Settings.GeneralSettings.TwitchPopoutCSS;
        tbPopoutCSS.IsReadOnly = false;
    }

    private void LoadTwitchPopoutCssSettings()
    {
        bool useDefaultCss = App.Settings.GeneralSettings.UseDefaultTwitchPopoutCSS;
        cbUseDefaultPopoutCSS.IsChecked = useDefaultCss;
        tbPopoutCSS.Text = useDefaultCss || string.IsNullOrEmpty(App.Settings.GeneralSettings.TwitchPopoutCSS)
            ? CustomCSS_Defaults.TwitchPopoutChat
            : App.Settings.GeneralSettings.TwitchPopoutCSS;
        tbPopoutCSS.IsReadOnly = useDefaultCss;
    }

    // --- Blaze settings helpers ---

    private readonly BlazeSettingsSync _blazeSync = new();

    private void LoadBlazeSettingsToUI()
    {
        var s = App.Settings.GeneralSettings;
        this.tbBlazeChannel.Text = s.BlazeChannel;
        this.tbTwitchChannel.Text = s.TwitchChannel;
        this.tbKickChannel.Text = s.KickChannel;
        this.slBlazeTextSize.Value = s.BlazeTextSize;
        this.lblBlazeTextSize.Text = s.BlazeTextSize + "px";
        this.cbBlazeSync.IsChecked = s.BlazeSyncEnabled;
        this.cbBlazeBgEnabled.IsChecked = s.BlazeBgEnabled;
        this.slBlazeBgOpacity.Value = s.BlazeBgOpacity;
        this.lblBlazeBgOpacity.Text = s.BlazeBgOpacity + "%";
        this.tbBlazeTextColor.Text = s.BlazeTextColor;
        this.slBlazeFade.Value = s.BlazeFadeTimeout;
        this.lblBlazeFade.Text = s.BlazeFadeTimeout == 0 ? "Off" : s.BlazeFadeTimeout + "s";

        // Set font combo
        for (int i = 0; i < cbBlazeFont.Items.Count; i++)
        {
            if (cbBlazeFont.Items[i] is ComboBoxItem item &&
                item.Content.ToString() == s.BlazeFontFamily)
            {
                cbBlazeFont.SelectedIndex = i;
                break;
            }
        }
    }

    private void SaveBlazeSettingsFromUI()
    {
        var s = App.Settings.GeneralSettings;
        s.BlazeChannel = this.tbBlazeChannel.Text;
        s.TwitchChannel = this.tbTwitchChannel.Text;
        s.KickChannel = this.tbKickChannel.Text;
        s.BlazeTextSize = (int)this.slBlazeTextSize.Value;
        s.BlazeSyncEnabled = this.cbBlazeSync.IsChecked ?? false;
        s.BlazeBgEnabled = this.cbBlazeBgEnabled.IsChecked ?? false;
        s.BlazeBgOpacity = (int)this.slBlazeBgOpacity.Value;
        s.BlazeTextColor = this.tbBlazeTextColor.Text;
        s.BlazeFadeTimeout = (int)this.slBlazeFade.Value;
        s.BlazeFontFamily = (cbBlazeFont.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Noto Sans";

        // Sync to server if enabled
        if (s.BlazeSyncEnabled && !string.IsNullOrWhiteSpace(s.BlazeChannel))
        {
            _ = SyncSettingsToServer(s);
        }
    }

    private async Task SyncSettingsToServer(GeneralSettings s)
    {
        var settings = new BlazeOverlaySettings
        {
            BgEnabled = s.BlazeBgEnabled,
            BgOpacity = s.BlazeBgOpacity,
            TextSize = s.BlazeTextSize,
            FontFamily = s.BlazeFontFamily,
            TextColor = s.BlazeTextColor,
            FadeTimeout = s.BlazeFadeTimeout
        };

        bool ok = await _blazeSync.SaveToServerAsync(s.BlazeChannel, settings);
        if (ok)
            Debug.WriteLine("Blaze settings synced to server.");
        else
            Debug.WriteLine("Failed to sync Blaze settings to server.");
    }

    private async void cbBlazeSync_Checked(object sender, RoutedEventArgs e)
    {
        // When sync is turned on, pull settings from server
        string channel = this.tbBlazeChannel.Text?.Trim();
        if (string.IsNullOrWhiteSpace(channel)) return;

        var serverSettings = await _blazeSync.LoadFromServerAsync(channel);
        if (serverSettings != null)
        {
            var s = App.Settings.GeneralSettings;
            s.BlazeTextSize = serverSettings.TextSize;
            s.BlazeBgEnabled = serverSettings.BgEnabled;
            s.BlazeBgOpacity = serverSettings.BgOpacity;
            s.BlazeFontFamily = serverSettings.FontFamily;
            s.BlazeTextColor = serverSettings.TextColor;
            s.BlazeFadeTimeout = serverSettings.FadeTimeout;
            LoadBlazeSettingsToUI();
        }
    }

    private void cbBlazeSync_Unchecked(object sender, RoutedEventArgs e)
    {
        // Nothing to do — local settings remain as-is
    }

    private void slBlazeTextSize_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblBlazeTextSize != null)
            lblBlazeTextSize.Text = (int)e.NewValue + "px";
    }

    private void slBlazeBgOpacity_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblBlazeBgOpacity != null)
            lblBlazeBgOpacity.Text = (int)e.NewValue + "%";
    }

    private void slBlazeFade_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblBlazeFade != null)
            lblBlazeFade.Text = (int)e.NewValue == 0 ? "Off" : (int)e.NewValue + "s";
    }
}
