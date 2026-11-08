using System.Collections.Generic;
using ZprtNextGen.Core.Logging;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковый логгер, фиксирующий вызовы Info и Error.</summary>
public sealed class FakeLogger : IAppLogger
{
    public List<string> Infos { get; } = new();

    public List<(string Message, System.Exception? Exception)> Errors { get; } = new();

    public void Info(string message)
    {
        Infos.Add(message);
    }

    public void Error(string message, System.Exception? exception = null)
    {
        Errors.Add((message, exception));
    }
}
