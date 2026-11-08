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
/// Потокобезопасен.
/// </summary>
public class AppLogger : IAppLogger
{
    private readonly string _directory;
    private readonly object _lock = new();

    /// <summary>
    /// Создать логгер. Директория создаётся, если отсутствует.
    /// </summary>
    /// <param name="directory">Директория для файлов логов.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <c>null</c>.</exception>
    public AppLogger(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    private string LogFilePath =>
        Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");

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
        var line = $"{timestamp} [{level}] {message}";
        var path = LogFilePath;

        lock (_lock)
        {
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));

            if (exception is not null)
            {
                File.AppendAllText(path, $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}", new UTF8Encoding(false));

                var stackTrace = exception.StackTrace;
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    File.AppendAllText(path, stackTrace, new UTF8Encoding(false));
                    if (!stackTrace.EndsWith(Environment.NewLine))
                        File.AppendAllText(path, Environment.NewLine, new UTF8Encoding(false));
                }
            }
        }
    }
}
