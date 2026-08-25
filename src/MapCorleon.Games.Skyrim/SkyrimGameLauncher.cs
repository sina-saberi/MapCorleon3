using System.Diagnostics;
using System.IO;
using MapCorleon.Contracts;

namespace MapCorleon.Games.Skyrim;

public sealed class SkyrimGameLauncher(
    IGameProcessDetector processDetector,
    IProcessStarter processStarter) : IGameLauncher
{
    public string Key => "skyrim-se";

    public bool Supports(GameMapDescriptor map) => SkyrimGameIdentity.IsSkyrim(map);

    public GameLaunchResult Launch(GameLaunchSettings? settings)
    {
        if (processDetector.IsRunning(SkyrimWorldMapPlayerLocationProvider.TargetProcessName))
            return new GameLaunchResult(GameLaunchStatus.AlreadyRunning, "Skyrim is already running.");

        var executable = settings?.ExecutablePath?.Trim();
        if (string.IsNullOrWhiteSpace(executable))
            return new GameLaunchResult(GameLaunchStatus.NotConfigured, "Configure the launch executable in Maps & timelines first.");
        if (!File.Exists(executable))
            return new GameLaunchResult(GameLaunchStatus.ExecutableNotFound, "The configured Skyrim launch executable does not exist.");

        try
        {
            var configuredWorkingDirectory = settings?.WorkingDirectory?.Trim();
            var workingDirectory = string.IsNullOrWhiteSpace(configuredWorkingDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(executable))!
                : Path.GetFullPath(configuredWorkingDirectory);
            if (!Directory.Exists(workingDirectory))
                return new GameLaunchResult(GameLaunchStatus.Failed, "The configured Skyrim working directory does not exist.");

            var process = processStarter.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executable),
                Arguments = settings?.Arguments?.Trim() ?? string.Empty,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            process?.Dispose();
            return new GameLaunchResult(GameLaunchStatus.Started, "Starting Skyrim…");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new GameLaunchResult(GameLaunchStatus.Failed, $"Skyrim could not be started: {ex.Message}");
        }
    }
}
