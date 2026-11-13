#nullable enable

using System;
using System.Windows;
using ZprtNextGen.App.Services;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.App;

/// <summary>Редактор пользовательского пресета: имя, описание и аргументы winws.</summary>
public partial class PresetEditorWindow : Window
{
    private readonly CustomPreset? _original;

    public PresetEditorWindow()
    {
        InitializeComponent();
    }

    public PresetEditorWindow(CustomPreset preset)
        : this()
    {
        _original = preset ?? throw new ArgumentNullException(nameof(preset));
    }

    /// <summary>Результат редактирования; <c>null</c>, если пользователь отменил.</summary>
    public CustomPreset? Result { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_original is not null)
        {
            NameBox.Text = _original.Name;
            DescriptionBox.Text = _original.Description;
            ArgsBox.Text = _original.Args;
            Title = "Изменить пресет";
        }
        else
        {
            Title = "Новый пресет";
        }

        NameBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var description = DescriptionBox.Text.Trim();
        var args = ArgsBox.Text.Trim();

        if (name.Length == 0)
        {
            ShowError("Укажи название пресета.");
            return;
        }

        if (args.Length == 0)
        {
            ShowError("Укажи аргументы winws.");
            return;
        }

        if (!args.Contains("--wf-", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Аргументы должны содержать хотя бы один фильтр --wf-tcp или --wf-udp.");
            return;
        }

        var id = _original?.Id ?? "custom:" + Guid.NewGuid().ToString("N")[..6];
        Result = new CustomPreset(id, name, description, args);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
