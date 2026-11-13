using System.Xml.Linq;
using ZprtNextGen.Core.Startup;

namespace ZprtNextGen.Core.Tests.Startup;

public class AutostartXmlTests
{
    private const string Exe = @"C:\Program Files\ZPRT & Co\ZprtNextGen.exe";

    [Fact]
    public void BuildTaskXml_ContainsTrayAutoconnectAndHighest()
    {
        var xml = AutostartCommand.BuildTaskXml(Exe);

        Assert.Contains("--tray --autoconnect", xml, StringComparison.Ordinal);
        Assert.Contains("HighestAvailable", xml, StringComparison.Ordinal);
        Assert.Contains("InteractiveToken", xml, StringComparison.Ordinal);
        Assert.Contains("<LogonTrigger>", xml, StringComparison.Ordinal);
        Assert.Contains("IgnoreNew", xml, StringComparison.Ordinal);
        Assert.Contains("PT0S", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("& Co", xml, StringComparison.Ordinal);
        Assert.Contains("&amp; Co", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTaskXml_IsValidXmlWithExpectedCommand()
    {
        var document = XDocument.Parse(AutostartCommand.BuildTaskXml(Exe));

        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var command = document.Descendants(ns + "Command").Single();
        var arguments = document.Descendants(ns + "Arguments").Single();

        Assert.Equal(Exe, command.Value);
        Assert.Equal("--tray --autoconnect", arguments.Value);
    }

    [Fact]
    public void MatchesTaskXml_AcceptsOwnTask()
    {
        var xml = AutostartCommand.BuildTaskXml(Exe);

        Assert.True(AutostartCommand.MatchesTaskXml(xml, Exe));
    }

    [Fact]
    public void MatchesTaskXml_RejectsOtherPathOrMissingArguments()
    {
        var xml = AutostartCommand.BuildTaskXml(Exe);

        Assert.False(AutostartCommand.MatchesTaskXml(xml, @"C:\Other\ZprtNextGen.exe"));
        Assert.False(AutostartCommand.MatchesTaskXml(
            xml.Replace("--tray --autoconnect", string.Empty, StringComparison.Ordinal), Exe));
    }

    [Fact]
    public void MatchesTaskXml_HandlesInvalidXml()
    {
        Assert.False(AutostartCommand.MatchesTaskXml("not xml", Exe));
    }

    [Fact]
    public void BuildXmlCreateArguments_ReferencesTempFile()
    {
        var arguments = AutostartCommand.BuildXmlCreateArguments(@"C:\Temp\task.xml");

        Assert.Contains("/Create", arguments, StringComparison.Ordinal);
        Assert.Contains($"/TN \"{AutostartCommand.TaskName}\"", arguments, StringComparison.Ordinal);
        Assert.Contains("/XML \"C:\\Temp\\task.xml\"", arguments, StringComparison.Ordinal);
        Assert.Contains("/F", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildQueryXmlArguments_RequestsXml()
    {
        var arguments = AutostartCommand.BuildQueryXmlArguments();

        Assert.Contains("/Query", arguments, StringComparison.Ordinal);
        Assert.Contains("/XML", arguments, StringComparison.Ordinal);
    }
}
