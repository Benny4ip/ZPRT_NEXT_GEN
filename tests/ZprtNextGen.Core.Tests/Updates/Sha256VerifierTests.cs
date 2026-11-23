using System.Text;
using ZprtNextGen.Core.Updates;

namespace ZprtNextGen.Core.Tests.Updates;

public class Sha256VerifierTests
{
    [Fact]
    public void ParseSums_ReadsHashAndFileName()
    {
        var text = """
            # comment
            aabbcc  file-a.zip
            DDEEFF *file-b.exe

            """;
        var map = Sha256Verifier.ParseSums(text);
        Assert.Equal("aabbcc", map["file-a.zip"]);
        Assert.Equal("ddeeff", map["file-b.exe"]);
    }

    [Fact]
    public void NormalizeDigest_StripsPrefix()
    {
        Assert.Equal("abc123", Sha256Verifier.NormalizeDigest("sha256:ABC123"));
        Assert.Equal("abc123", Sha256Verifier.NormalizeDigest("ABC123"));
    }

    [Fact]
    public void VerifyFile_Mismatch_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("hello"));
        try
        {
            var ex = Assert.Throws<InvalidDataException>(
                () => Sha256Verifier.VerifyFile(path, "00"));
            Assert.Contains("SHA256 не совпал", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VerifyFile_Match_Succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
        var bytes = Encoding.UTF8.GetBytes("hello");
        File.WriteAllBytes(path, bytes);
        try
        {
            Sha256Verifier.VerifyFile(path, Sha256Verifier.HashBytes(bytes));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FormatSums_ProducesParsableOutput()
    {
        var text = Sha256Verifier.FormatSums([("a.zip", "Aa"), ("b.exe", "Bb")]);
        var map = Sha256Verifier.ParseSums(text);
        Assert.Equal("aa", map["a.zip"]);
        Assert.Equal("bb", map["b.exe"]);
    }
}
