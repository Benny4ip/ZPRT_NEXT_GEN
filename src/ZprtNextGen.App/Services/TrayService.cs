#nullable enable

using System;
using System.Drawing;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace ZprtNextGen.App.Services;

/// <summary>
/// Трей-иконка приложения: текст на русском, меню «Включить/Выключить», «Открыть», «Выход»,
/// двойной клик восстанавливает окно.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const int MaxTooltipLength = 63;
    private const string AppText = "ZPRT NEXT GEN";

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly Action _toggle;
    private readonly Action _show;
    private readonly Action _exit;

    /// <summary>Создать трей-иконку и действия меню.</summary>
    /// <param name="icon">Иконка трея.</param>
    /// <param name="toggle">Действие «включить/выключить обход» (вызывается без ожидания, с try/catch снаружи).</param>
    /// <param name="show">Действие «показать главное окно».</param>
    /// <param name="exit">Действие «инициировать выход из приложения».</param>
    public TrayService(Icon icon, Action toggle, Action show, Action exit)
    {
        _icon = icon ?? throw new ArgumentNullException(nameof(icon));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        var menu = new Forms.ContextMenuStrip();

        var toggleItem = new Forms.ToolStripMenuItem("Включить/Выключить");
        toggleItem.Click += (_, _) => _toggle();
        menu.Items.Add(toggleItem);

        var showItem = new Forms.ToolStripMenuItem("Открыть");
        showItem.Click += (_, _) => _show();
        menu.Items.Add(showItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => _exit();
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = AppText,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _show();
    }

    /// <summary>Обновить подсказку трея (максимум 63 символа).</summary>
    /// <param name="tooltip">Текст подсказки.</param>
    public void UpdateStatus(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        _notifyIcon.Text = tooltip.Length > MaxTooltipLength
            ? tooltip[..MaxTooltipLength]
            : tooltip;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
