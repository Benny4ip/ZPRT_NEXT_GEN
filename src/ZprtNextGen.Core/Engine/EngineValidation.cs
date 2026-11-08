namespace ZprtNextGen.Core.Engine;

/// <summary>
/// Результат поиска и валидации директории движка zapret (winws).
/// </summary>
public record EngineValidation(bool IsValid, string? EngineRoot, IReadOnlyList<string> MissingFiles);
