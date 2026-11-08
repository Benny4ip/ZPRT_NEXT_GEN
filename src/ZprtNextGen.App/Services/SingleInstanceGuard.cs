#nullable enable

using System;
using System.Security.Principal;
using System.Threading;

namespace ZprtNextGen.App.Services;

/// <summary>
/// Гарантирует запуск только одного экземпляра приложения через именованный мьютекс <c>Global\ZPRT_NEXT_GEN_&lt;name&gt;</c>.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string Prefix = @"Global\ZPRT_NEXT_GEN_";

    private readonly Mutex _mutex;
    private readonly bool _owned;

    /// <summary>Создатьmutex и попытаться им владеть.</summary>
    /// <param name="name">Имя экземпляра (без префикса).</param>
    public SingleInstanceGuard(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _mutex = new Mutex(initiallyOwned: true, Prefix + name, out var createdNew);
        _owned = createdNew;
    }

    /// <summary><c>true</c>, если этот экземпляр owns mutex (приложение запущено первым).</summary>
    public bool IsOwner => _owned;

    /// <summary>Признак того, что текущий пользователь — администратор (требуется для WinDivert).</summary>
    public static bool IsElevated()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Лучшая попытка освободитьmutex.
            }
        }

        _mutex.Dispose();
    }
}
