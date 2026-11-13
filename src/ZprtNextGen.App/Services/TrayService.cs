#nullable enable

using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace ZprtNextGen.App.Services;

/// <summary>
/// Трей-иконка: «Развернуть», «Включить/Выключить обход» или «Отменить подбор»,
/// «Fix Discord» и «Выход». Двойной и одиночный левый клик разворачивают окно.
/// </summary>
public sealed class TrayService : IDisposable
{
    private const int MaxTooltipLength = 63;
    private const string AppText = "ZPRT NEXT GEN";

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly bool _ownsIcon;
    private readonly Action _toggle;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly Action _fixDiscord;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Forms.ToolStripMenuItem _fixItem;

    /// <summary>Создать трей-иконку и действия меню.</summary>
    public TrayService(Icon? icon, Action show, Action toggle, Action fixDiscord, Action exit)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _fixDiscord = fixDiscord ?? throw new ArgumentNullException(nameof(fixDiscord));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        _icon = icon ?? SystemIcons.Application;
        _ownsIcon = icon is not null;

        var menu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("Развернуть");
        showItem.Click += (_, _) => _show();
        menu.Items.Add(showItem);

        _toggleItem = new Forms.ToolStripMenuItem("Включить обход");
        _toggleItem.Click += (_, _) => _toggle();
        menu.Items.Add(_toggleItem);

        _fixItem = new Forms.ToolStripMenuItem("Fix Discord");
        _fixItem.Click += (_, _) => _fixDiscord();
        menu.Items.Add(_fixItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => _exit();
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = AppText,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _show();
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                _show();
        };
    }

    /// <summary>Обновить подсказку трея (максимум 63 символа).</summary>
    public void UpdateStatus(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        var text = string.IsNullOrWhiteSpace(tooltip) ? AppText : tooltip;
        _notifyIcon.Text = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    /// <summary>Обновить состояние пунктов меню.</summary>
    /// <param name="toggleText">«Включить обход», «Выключить обход» или «Отменить подбор».</param>
    /// <param name="busy">Идёт операция: пункты, меняющие движок, блокируются.</param>
    /// <param name="fixingDiscord">Идёт Fix Discord.</param>
    public void UpdateMenu(string toggleText, bool busy, bool fixingDiscord)
    {
        _toggleItem.Text = string.IsNullOrWhiteSpace(toggleText) ? "Включить обход" : toggleText;
        _toggleItem.Enabled = !busy;
        _fixItem.Enabled = !busy && !fixingDiscord;
    }

    /// <summary>Показать короткое уведомление рядом с треем.</summary>
    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);
        }
        catch (Exception)
        {
            // Уведомление — вспомогательное действие.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        if (_ownsIcon)
            _icon.Dispose();
    }
}
