#nullable enable

using System;
using System.Collections.Generic;

namespace ZprtNextGen.App;

/// <summary>Аргументы командной строки приложения.</summary>
public sealed record AppStartupOptions(bool StartInTray, bool AutoConnect, bool PortableCheck)
{
    /// <summary>Разобрать аргументы. Неизвестные аргументы игнорируются.</summary>
    public static AppStartupOptions Parse(IReadOnlyList<string>? args)
    {
        var startInTray = false;
        var autoConnect = false;
        var portableCheck = false;

        foreach (var argument in args ?? [])
        {
            switch (argument.Trim().ToLowerInvariant())
            {
                case "--tray":
                    startInTray = true;
                    break;
                case "--autoconnect":
                    autoConnect = true;
                    break;
                case "--portable-check":
                    portableCheck = true;
                    break;
            }
        }

        return new AppStartupOptions(startInTray, autoConnect, portableCheck);
    }

    /// <summary>Аргументы по умолчанию (обычный запуск).</summary>
    public static AppStartupOptions Default { get; } = new(false, false, false);
}
