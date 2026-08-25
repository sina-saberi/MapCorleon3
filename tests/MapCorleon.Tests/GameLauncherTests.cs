using System.Diagnostics;
using MapCorleon.Contracts;
using MapCorleon.Games.Skyrim;

namespace MapCorleon.Tests;

public sealed class GameLauncherTests
{
    [Fact]
    public void DoesNotLaunchDuplicateWhenSkyrimProcessExists()
    {
        var detector = new SkyrimPlayerLocationProviderTests.FakeDetector
        {
            Process = new GameProcessInfo(1, 0, @"C:\SkyrimSE.exe", "1.6.1170.0")
        };
        var starter = new FakeStarter();
        var launcher = new SkyrimGameLauncher(detector, starter);

        var result = launcher.Launch(new GameLaunchSettings(@"C:\missing.exe"));

        Assert.Equal(GameLaunchStatus.AlreadyRunning, result.Status);
        Assert.Null(starter.StartInfo);
    }

    [Fact]
    public void RejectsMissingConfiguredExecutable()
    {
        var launcher = new SkyrimGameLauncher(new SkyrimPlayerLocationProviderTests.FakeDetector(), new FakeStarter());
        var result = launcher.Launch(new GameLaunchSettings(@"C:\definitely-missing\skse64_loader.exe"));
        Assert.Equal(GameLaunchStatus.ExecutableNotFound, result.Status);
    }

    [Fact]
    public void UsesConfiguredExecutableArgumentsAndWorkingDirectory()
    {
        var executable = Path.GetTempFileName();
        try
        {
            var starter = new FakeStarter();
            var launcher = new SkyrimGameLauncher(new SkyrimPlayerLocationProviderTests.FakeDetector(), starter);

            var result = launcher.Launch(new GameLaunchSettings(executable, "-forcesteamloader", Path.GetTempPath()));

            Assert.Equal(GameLaunchStatus.Started, result.Status);
            Assert.Equal(Path.GetFullPath(executable), starter.StartInfo?.FileName);
            Assert.Equal("-forcesteamloader", starter.StartInfo?.Arguments);
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()), starter.StartInfo?.WorkingDirectory);
            Assert.True(starter.StartInfo?.UseShellExecute == true);
        }
        finally { File.Delete(executable); }
    }

    private sealed class FakeStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }
        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return null;
        }
    }
}
