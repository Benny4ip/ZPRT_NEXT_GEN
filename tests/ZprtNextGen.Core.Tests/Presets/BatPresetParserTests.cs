using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Presets;

public class BatPresetParserTests
{
    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_GeneralFixture_ReturnsExpectedArguments()
    {
        var batText = ReadFixture("general.bat");
        var result = BatPresetParser.ParseArguments(batText, "general");

        Assert.StartsWith("--wf-tcp=80,443", result);
        Assert.Contains("\"{{LISTS}}list-general.txt\"", result);
        Assert.Contains("--dpi-desync-fake-tls=!", result);
        Assert.DoesNotContain("start", result);
        Assert.DoesNotContain("%BIN%", result);
        Assert.DoesNotContain("%LISTS%", result);
        Assert.DoesNotContain("^", result);
    }

    [Fact]
    public void Parse_UnresolvedVariable_Throws()
    {
        var batText = ReadFixture("unresolved-var.bat");

        var exception = Assert.Throws<InvalidDataException>(
            () => BatPresetParser.ParseArguments(batText, "unresolved-var"));

        Assert.Contains("UNKNOWN", exception.Message);
        Assert.Contains("unresolved-var", exception.Message);
    }

    [Fact]
    public void Parse_NoWinwsLine_Throws()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => BatPresetParser.ParseArguments("@echo off\r\nnotepad\r\n", "broken"));

        Assert.NotNull(exception);
    }

    [Fact]
    public void Parse_EmptyArguments_Throws()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => BatPresetParser.ParseArguments("start \"x\" /min \"%BIN%winws.exe\" ", "empty"));

        Assert.NotNull(exception);
    }

    [Fact]
    public void Parse_LowercaseCommandAndVariables_AreResolved()
    {
        var batText = "sTaRt \"zapret: general\" /min \"%bin%winws.exe\" " +
                      "--hostlist=\"%lists%list-general.txt\" --wf-tcp=%gamefiltertcp%";

        var result = BatPresetParser.ParseArguments(batText, "general");

        Assert.Contains("{{LISTS}}list-general.txt", result);
        Assert.Contains("{{GameFilterTCP}}", result);
        Assert.DoesNotContain("%bin%", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%lists%", result, StringComparison.OrdinalIgnoreCase);
    }
}
