#nullable enable

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App;

/// <summary>Interaction logic for <see cref="MainWindow"/>.</summary>
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int DwmwaUseImmersiveDarkMode = 20;

    private readonly MainViewModel _viewModel;
    private bool _exiting = false;

    /// <summary>Создать главное окно.</summary>
    /// <param name="viewModel">Модель представления (обязательна).</param>
    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.LogLines.CollectionChanged += OnLogLinesChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Тёмная заголовок окна (DWMWA_USE_IMMERSIVE_DARK_MODE = 20). Лучшая попытка: ошибки игнорируются.
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var value = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // Игнорируем: стандартная заголовок остаётся светлой.
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    /// <summary>Разрешить выход из приложения: следующее закрытие окна не будет свёрнуто в трей.</summary>
    public void RequestExit() => _exiting = true;

    private async void OnClosing(object sender, CancelEventArgs e)
    {
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

        await _viewModel.ShutdownAsync(stopEngine);
    }

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

    private async void OnBrowseEngine(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку с движком winws",
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await _viewModel.ChangeEnginePathAsync(dialog.SelectedPath);
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow(_viewModel.EngineVersion) { Owner = this };
        about.ShowDialog();
    }
}
