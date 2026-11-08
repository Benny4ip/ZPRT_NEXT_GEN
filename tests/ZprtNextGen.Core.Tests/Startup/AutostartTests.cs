using ZprtNextGen.Core.Startup;

namespace ZprtNextGen.Core.Tests.Startup;

public class AutostartTests
{
    [Fact]
    public void BuildCreateArguments_QuotesExeAndUsesHighest()
    {
        var args = AutostartCommand.BuildCreateArguments(@"C:\Program Files\ZPRT\ZprtNextGen.exe");

        var expected =
            $"/Create /TN \"{AutostartCommand.TaskName}\" /TR \"C:\\Program Files\\ZPRT\\ZprtNextGen.exe\" /SC ONLOGON /RL HIGHEST /F";

        Assert.Equal(expected, args);
        Assert.Contains("/Create", args);
        Assert.Contains($"/TN \"{AutostartCommand.TaskName}\"", args);
        Assert.Contains("\"C:\\Program Files\\ZPRT\\ZprtNextGen.exe\"", args);
        Assert.Contains("/SC ONLOGON", args);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains("/F", args);
    }

    [Fact]
    public void BuildDeleteArguments_ContainsTaskName()
    {
        var args = AutostartCommand.BuildDeleteArguments();

        Assert.Equal($"/Delete /TN \"{AutostartCommand.TaskName}\" /F", args);
        Assert.Contains("ZPRT_NEXT_GEN", args);
        Assert.Contains("/Delete", args);
        Assert.Contains("/F", args);
    }
}
