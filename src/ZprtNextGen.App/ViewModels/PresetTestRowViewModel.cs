#nullable enable

using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.App.ViewModels;

/// <summary>Строка результата теста обхода: YouTube, Discord, пинг.</summary>
public sealed class PresetTestRowViewModel
{
    public PresetTestRowViewModel(PresetTestResult result)
    {
        PresetId = result.PresetId;
        Name = result.Name;
        YoutubeOk = result.YoutubeOk;
        DiscordOk = result.DiscordOk;
        Qualified = result.Qualified;
        YoutubeMs = result.YoutubeMs;
        DiscordMs = result.DiscordMs;
        PingText = result.Qualified && result.ScoreMs is { } score ? $"{score} мс" : "—";
        Detail = result.Detail;
    }

    public string PresetId { get; }

    public string Name { get; }

    public bool YoutubeOk { get; }

    public bool DiscordOk { get; }

    public bool Qualified { get; }

    public long? YoutubeMs { get; }

    public long? DiscordMs { get; }

    public string PingText { get; }

    public string? Detail { get; }

    public string YoutubeText => YoutubeOk ? "YouTube ✓" : "YouTube —";

    public string DiscordText => DiscordOk ? "Discord ✓" : "Discord —";
}
