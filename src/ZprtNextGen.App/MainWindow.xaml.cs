#nullable enable

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App;

/// <summary>Interaction logic for <see cref="MainWindow"/>.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _exiting = false;
    private bool _shutdownInProgress;

    /// <summary>Создать главное окно.</summary>
    /// <param name="viewModel">Модель представления (обязательна).</param>
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.LogLines.CollectionChanged += OnLogLinesChanged;
        _viewModel.ErrorRaised += OnErrorRaised;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Тёмный заголовок окна. Лучшая попытка: ошибки игнорируются.
        DarkTitleBar.Apply(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private bool _errorDialogOpen;

    /// <summary>Инициировать выход: окно закроется, движок остановится.</summary>
    public void RequestExit()
    {
        _exiting = true;
        Close();
    }

    private void OnErrorRaised(object? sender, string message)
    {
        if (_errorDialogOpen)
            return;

        _errorDialogOpen = true;
        try
        {
            MessageBox.Show(this, message, "ZPRT NEXT GEN — ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    private async void OnClosing(object sender, CancelEventArgs e)
    {
        // Повторный Close() после ожидания остановки движка: просто закрываемся.
        if (_shutdownInProgress)
            return;

        if (_viewModel.MinimizeToTray && !_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        var stopEngine = true;
        if (_viewModel.IsRunning)
        {
            var result = MessageBox.Show(
                "Остановить движок перед выходом?",
                "ZPRT NEXT GEN",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            if (result == MessageBoxResult.Cancel)
            {
                _exiting = false;
                e.Cancel = true;
                return;
            }

            stopEngine = result == MessageBoxResult.Yes;
        }

        // Закрытие откладываем до завершения остановки движка: иначе окно и процесс
        // приложения исчезнут раньше, чем winws будет остановлен.
        e.Cancel = true;
        _shutdownInProgress = true;
        try
        {
            await _viewModel.ShutdownAsync(stopEngine);
        }
        finally
        {
            _exiting = true;
            Close();
        }
    }

    private SettingsWindow? _settingsWindow;
    private bool _scrollQueued;

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Нельзя звать ScrollIntoView прямо из CollectionChanged: он форсирует UpdateLayout
        // посреди уведомления, и ItemContainerGenerator рассинхронизируется
        // ("накопленное количество N отличается от фактического M").
        // Откладываем прокрутку до конца обработки очереди диспетчера и схлопываем повторы.
        if (_scrollQueued)
            return;

        _scrollQueued = true;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                _scrollQueued = false;
                var count = LogList.Items.Count;
                if (count > 0)
                    LogList.ScrollIntoView(LogList.Items[count - 1]);
            }));
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join(Environment.NewLine, _viewModel.LogLines);
            if (string.IsNullOrEmpty(text))
            {
                _viewModel.StatusText = "Лог пока пуст";
                return;
            }

            Clipboard.SetText(text);
            _viewModel.StatusText = "Лог скопирован в буфер обмена";
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "Не удалось скопировать лог: " + ex.Message;
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_viewModel) { Owner = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
}
