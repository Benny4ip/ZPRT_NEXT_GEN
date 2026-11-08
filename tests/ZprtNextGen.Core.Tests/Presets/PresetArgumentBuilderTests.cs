using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetArgumentBuilderTests
{
    private static Preset Preset(string arguments) =>
        new("general", "general", "Основной", "Тестовый пресет", arguments);

    [Theory]
    [InlineData(GameFilterMode.Disabled, "--wf-tcp=80,12 --wf-udp=443,12")]
    [InlineData(GameFilterMode.Tcp, "--wf-tcp=80,1024-65535 --wf-udp=443,12")]
    [InlineData(GameFilterMode.Udp, "--wf-tcp=80,12 --wf-udp=443,1024-65535")]
    [InlineData(GameFilterMode.All, "--wf-tcp=80,1024-65535 --wf-udp=443,1024-65535")]
    public void Build_GameFilterModes_ReplaceTokensExactly(GameFilterMode mode, string expected)
    {
        var preset = Preset("--wf-tcp=80,{{GameFilterTCP}} --wf-udp=443,{{GameFilterUDP}}");

        var result = PresetArgumentBuilder.Build(preset, @"C:\Engine", mode);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Build_ResolvesBinAndListsPaths()
    {
        var preset = Preset("--hostlist=\"{{LISTS}}list-general.txt\" --fake-tls=\"{{BIN}}stun.bin\"");

        var result = PresetArgumentBuilder.Build(preset, @"C:\Program Files\Запрет", GameFilterMode.Disabled);

        Assert.Contains(@"C:\Program Files\Запрет\lists\list-general.txt", result);
        Assert.Contains(@"C:\Program Files\Запрет\bin\stun.bin", result);
    }

    [Fact]
    public void Build_UnresolvedPlaceholder_ThrowsInvalidOperationException()
    {
        var preset = Preset("--wf-tcp=80,{{NOPE}}");

        var exception = Assert.Throws<InvalidOperationException>(
            () => PresetArgumentBuilder.Build(preset, @"C:\Engine", GameFilterMode.Disabled));

        Assert.Contains("{{NOPE}}", exception.Message);
    }

    [Fact]
    public void Build_EmptyArguments_ThrowsInvalidOperationException()
    {
        var preset = Preset("");

        var exception = Assert.Throws<InvalidOperationException>(
            () => PresetArgumentBuilder.Build(preset, @"C:\Engine", GameFilterMode.Disabled));

        Assert.NotNull(exception);
    }
}
