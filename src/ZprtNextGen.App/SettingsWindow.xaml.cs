#nullable enable

using System;
using System.Windows;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App;

/// <summary>Отдельное окно настроек: движок, поведение, обновления, проверка пресетов.</summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    public SettingsWindow(MainViewModel viewModel)
    {
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Автопроверка движка выполняется при старте приложения (см. MainViewModel.InitializeAsync).
    }

    private async void OnBrowseEngine(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку с движком winws",
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await _vm.ChangeEnginePathAsync(dialog.SelectedPath);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow(_vm.EngineVersion) { Owner = this };
        about.ShowDialog();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
