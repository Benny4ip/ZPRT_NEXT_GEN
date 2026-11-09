#nullable enable

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

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
