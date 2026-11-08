using System.Collections.Generic;
using ZprtNextGen.Core.Settings;

namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Фейковое хранилище настроек для тестов.</summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    public string Path { get; set; } = "settings.json";

    public AppSettings Settings { get; set; } = new();

    public List<AppSettings> Saved { get; } = new();

    public AppSettings Load() => Settings;

    public void Save(AppSettings settings)
    {
        Saved.Add(settings);
    }
}
