namespace ZprtNextGen.App.Tests.Fakes;

/// <summary>Подтверждение пользователя с фиксированным результатом.</summary>
public sealed class ConfirmAlways
{
    private readonly bool _result;

    public ConfirmAlways(bool result) => _result = result;

    public bool Invoke(string? message = null) => _result;
}
