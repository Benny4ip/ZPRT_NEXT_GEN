#nullable enable

using System.Diagnostics;
using System.Windows;
using ZprtNextGen.App.Services;

namespace ZprtNextGen.App;

/// <summary>Окно прогресса обновления движка (кружок прогресса + статус).</summary>
public partial class EngineUpdateProgressWindow : Window
{
    public EngineUpdateProgressWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    /// <summary>Обновить текст статуса.</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Завершить прогресс: скрыть полосу и при необходимости показать кнопку GitHub.</summary>
    public void Finish(bool showGitHubButton = false)
    {
        ProgressBar.Visibility = Visibility.Collapsed;
        OpenGitHubButton.Visibility = showGitHubButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenGitHub(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/Flowseal/zapret-discord-youtube/releases")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Лучшая попытка открыть браузер.
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
