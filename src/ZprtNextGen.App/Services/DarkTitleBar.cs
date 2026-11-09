#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZprtNextGen.App.Services;

/// <summary>Тёмная системная рамка и заголовок окна (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Включить тёмный заголовок. Ошибки игнорируются: остаётся системный светлый.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var value = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // Лучшая попытка: на старых сборках Windows атрибут может быть недоступен.
        }
    }
}
