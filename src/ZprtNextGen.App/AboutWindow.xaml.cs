#nullable enable

using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using ZprtNextGen.App.Services;

namespace ZprtNextGen.App;

/// <summary>Окно «О программе»: версия, движок, ссылки на апстрим, лицензия, предупреждение, дисклеймер.</summary>
public partial class AboutWindow : Window
{
    /// <summary>Создать окно «О программе».</summary>
    /// <param name="engineVersion">Версия движка из каталога пресетов.</param>
    public AboutWindow(string engineVersion)
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionText.Text = version?.ToString() ?? "неизвестно";
        EngineVersionText.Text = engineVersion ?? string.Empty;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    private void OnZapretDiscordYoutube(object sender, RoutedEventArgs e) =>
        OpenLink("https://github.com/Flowseal/zapret-discord-youtube");

    private void OnZapret(object sender, RoutedEventArgs e) =>
        OpenLink("https://github.com/bol-van/zapret");

    private void OnTelegram(object sender, RoutedEventArgs e) =>
        OpenLink("https://t.me/benny_4ip");

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось открыть ссылку: {ex.Message}",
                "ZPRT NEXT GEN",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
