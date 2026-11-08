using System.IO;
using System.Linq;
using Xunit;
using ZprtNextGen.Core.Presets;

namespace ZprtNextGen.Core.Tests.Presets;

public class PresetImporterTests
{
    private static string FixturesDir()
    {
        // Файлы из Fixtures\**\*.bat копируются в вывод при сборке (csproj:26).
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "bats");
    }

    [Theory]
    [InlineData("general (ALT5).bat", "general-alt5")]
    [InlineData("general (ALT).bat", "general-alt")]
    [InlineData("general (EXP).bat", "general-exp")]
    [InlineData("general (FAKE TLS AUTO ALT).bat", "general-fake-tls-auto-alt")]
    [InlineData("general (FAKE TLS AUTO).bat", "general-fake-tls-auto")]
    [InlineData("general.bat", "general")]
    public void ToId_ConvertsFileNames(string fileName, string expectedId)
    {
        Assert.Equal(expectedId, PresetNaming.ToId(fileName));
    }

    [Fact]
    public void Describe_AltWithoutNumber_IsFirst()
    {
        var (name, desc) = PresetNaming.Describe("general-alt");
        Assert.Equal("Альтернатива 1", name);
        Assert.Equal("Вариант 1: другая комбинация стратегий обхода.", desc);
    }

    [Fact]
    public void Import_RealPresetNames_ClassifiesGroups()
    {
        var engineRoot = CreateTempEngineWith(FixturesDir());
        try
        {
            var catalog = PresetImporter.Import(engineRoot, "1.10.2");

            Assert.Equal(4, catalog.Presets.Count);
            Assert.Equal("Flowseal/zapret-discord-youtube", catalog.Engine);
            Assert.Equal("1.10.2", catalog.EngineVersion);
            Assert.Equal(3, catalog.Groups.Count); // general, faketls, experimental (simplefake пустой)

            var byGroup = catalog.Presets.GroupBy(p => p.GroupId).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(2, byGroup["general"]);
            Assert.Equal(1, byGroup["experimental"]);
            Assert.Equal(1, byGroup["faketls"]);

            var alt = catalog.Presets.First(p => p.Id == "general-alt5");
            Assert.Equal("Альтернатива 5", alt.Name);
            Assert.Equal("general", alt.GroupId);
            Assert.NotEmpty(alt.Arguments);

            var exp = catalog.Presets.First(p => p.Id == "general-exp");
            Assert.Equal("Экспериментальный", exp.Name);
            Assert.Equal("Экспериментальные стратегии. Может работать нестабильно.", exp.Description);

            var fakeTls = catalog.Presets.First(p => p.Id == "general-fake-tls-auto");
            Assert.Equal("Fake TLS (авто)", fakeTls.Name);
            Assert.Equal("faketls", fakeTls.GroupId);

            // service.bat ignored
            Assert.DoesNotContain(catalog.Presets, p => p.Id.Contains("service"));

            foreach (var p in catalog.Presets)
            {
                Assert.NotEmpty(p.Arguments);
                Assert.Contains("{{LISTS}}", p.Arguments);
            }
        }
        finally
        {
            Directory.Delete(engineRoot, true);
        }
    }

    [Fact]
    public void Import_MissingBin_ThrowsInvalidDataException()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"zprt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => PresetImporter.Import(emptyDir));
            Assert.Contains("winws.exe", ex.Message);
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }

    private static string CreateTempEngineWith(string batsDir)
    {
        var engineRoot = Path.Combine(Path.GetTempPath(), $"zprt-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(engineRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(engineRoot, "lists"));
        File.WriteAllText(Path.Combine(engineRoot, "bin", "winws.exe"), string.Empty);
        File.WriteAllText(Path.Combine(engineRoot, "lists", "list-general.txt"), string.Empty);

        foreach (var bat in Directory.GetFiles(batsDir, "*.bat"))
        {
            File.Copy(bat, Path.Combine(engineRoot, Path.GetFileName(bat)), overwrite: true);
        }
        return engineRoot;
    }
}
