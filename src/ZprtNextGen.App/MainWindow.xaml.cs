#nullable enable

using System;
using System.ComponentModel;
using System.Windows;
using ZprtNextGen.App.Services;
using ZprtNextGen.App.ViewModels;

namespace ZprtNextGen.App;

/// <summary>Главное окно: четыре угла и орб по центру, без списков и логов.</summary>
public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private bool _exiting;

    public MainWindow(DashboardViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.ErrorRaised += OnErrorRaised;
        _viewModel.Refresh();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.Refresh();

    /// <summary>Инициировать выход: окно закроется, движок остановится в <see cref="App.OnExit"/>.</summary>
    public void RequestExit()
    {
        _exiting = true;
        Close();
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

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_exiting)
            return;

        if (_viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (!_viewModel.IsConnected)
            return;

        var result = MessageBox.Show(
            this,
            "Выключить обход и закрыть приложение?",
            "ZPRT NEXT GEN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.No)
        {
            e.Cancel = true;
            return;
        }

        _exiting = true;
    }
}
