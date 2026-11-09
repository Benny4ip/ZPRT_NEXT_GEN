namespace ZprtNextGen.Core.Logging;

using System;
using System.IO;
using System.Text;

/// <summary>Интерфейс файлового логгера приложения.</summary>
public interface IAppLogger
{
    /// <summary>Записать информационное сообщение.</summary>
    void Info(string message);

    /// <summary>Записать сообщение об ошибке с опциональным исключением.</summary>
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Пишет логи в дневной файл <c>app-YYYYMMDD.log</c> в указанной директории (UTF-8 без BOM).
/// Потокобезопасен. При ошибке записи в основную директорию пробует fallback рядом с приложением.
/// </summary>
public class AppLogger : IAppLogger
{
    private readonly string _directory;
    private readonly string _fallbackDirectory;
    private readonly object _lock = new();
    private bool _useFallback;

    /// <summary>
    /// Создать логгер. Директория создаётся, если отсутствует.
    /// </summary>
    /// <param name="directory">Директория для файлов логов.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <c>null</c>.</exception>
    public AppLogger(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        _fallbackDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        TryEnsureDirectory(_directory);
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string LogFilePath(string directory) =>
        Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var text = $"{timestamp} [{level}] {message}{Environment.NewLine}";
        if (exception is not null)
        {
            text += $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}";
            var stackTrace = exception.StackTrace;
            if (!string.IsNullOrEmpty(stackTrace))
            {
                text += stackTrace;
                if (!stackTrace.EndsWith(Environment.NewLine))
                    text += Environment.NewLine;
            }
        }

        lock (_lock)
        {
            // Если папку логов удалили/она недоступна — пробуем создать заново.
            var primary = _useFallback ? _fallbackDirectory : _directory;
            var secondary = _useFallback ? _directory : _fallbackDirectory;
            foreach (var dir in new[] { primary, secondary })
            {
                try
                {
                    TryEnsureDirectory(dir);
                    File.AppendAllText(LogFilePath(dir), text, new UTF8Encoding(false));
                    _useFallback = !string.Equals(dir, _directory, StringComparison.OrdinalIgnoreCase);
                    return;
                }
                catch
                {
                    // Пробуем следующий путь (fallback).
                }
            }
            // Не можем записать лог — игнорируем, не удваиваем ошибку.
        }
    }
}
