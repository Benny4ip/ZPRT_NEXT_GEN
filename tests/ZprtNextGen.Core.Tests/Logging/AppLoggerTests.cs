using System.IO;
using System.Text.RegularExpressions;
using ZprtNextGen.Core.Logging;

namespace ZprtNextGen.Core.Tests.Logging;

public class AppLoggerTests
{
    [Fact]
    public void Logger_CreatesDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));

        var logger = new AppLogger(directory);

        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void Logger_WritesDailyFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logger = new AppLogger(directory);

        logger.Info("привет");
        logger.Error("бум", new Exception("x"));

        var files = Directory.GetFiles(directory);
        var expectedFile = $"app-{DateTime.Now:yyyyMMdd}.log";
        Assert.Single(files);
        Assert.Equal(expectedFile, Path.GetFileName(files[0]));

        var content = File.ReadAllText(files[0]);
        Assert.Contains("[INFO] привет", content);
        Assert.Contains("[ERROR] бум", content);
        Assert.Contains("x", content);
    }

    [Fact]
    public void Logger_ConcurrentWrites_NoCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logger = new AppLogger(directory);

        Parallel.For(0, 100, _ => logger.Info("привет"));

        var lines = File.ReadAllLines(Directory.GetFiles(directory)[0]);
        Assert.Equal(100, lines.Length);

        var linePattern = new Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] привет$");
        foreach (var line in lines)
            Assert.True(linePattern.IsMatch(line), $"Некорректная строка лога: {line}");
    }
}
