#nullable enable

using System;
using System.Windows;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.App;

/// <summary>Окно настроек: подключение, игровой профиль, пресеты, трей, движок, диагностика.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.ErrorRaised += OnErrorRaised;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Настройки уже загружены моделью; здесь только обновляем производные значения.
        _viewModel.Reload();
    }

    private bool _errorDialogOpen;

    private void OnErrorRaised(object? sender, string message)
    {
        if (_errorDialogOpen)
            return;

        _errorDialogOpen = true;
        try
        {
            MessageBox.Show(this, message, "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private void OnBrowseEngine(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выбери папку движка (внутри должны быть bin\\winws.exe и lists)",
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _viewModel.ChangeEnginePath(dialog.SelectedPath);
    }

    private void OnAddCustomPreset(object sender, RoutedEventArgs e)
    {
        var editor = new PresetEditorWindow { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
            _viewModel.AddCustomPreset(editor.Result);
    }

    private void OnEditCustomPreset(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not CustomPreset preset)
            return;

        var editor = new PresetEditorWindow(preset) { Owner = this };
        if (editor.ShowDialog() == true && editor.Result is not null)
            _viewModel.UpdateCustomPreset(preset.Id, editor.Result);
    }

    private void OnDeleteCustomPreset(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not CustomPreset preset)
            return;

        var result = MessageBox.Show(
            this,
            $"Удалить пресет «{preset.Name}»?",
            "ZPRT NEXT GEN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            _viewModel.RemoveCustomPreset(preset.Id);
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _viewModel.BuildLogText();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Лог пока пуст.", "ZPRT NEXT GEN",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось скопировать лог: " + ex.Message, "ZPRT NEXT GEN",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow(_viewModel.EngineVersion) { Owner = this };
        window.ShowDialog();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
