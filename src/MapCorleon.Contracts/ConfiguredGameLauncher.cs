using System.ComponentModel;
using System.Diagnostics;

namespace MapCorleon.Contracts;

public abstract class ConfiguredGameLauncher(
    IGameProcessDetector processDetector, IProcessStarter processStarter, string processName, string gameName) : IGameLauncher
{
    public abstract string Key { get; }
    public abstract bool Supports(GameMapDescriptor map);

    public GameLaunchResult Launch(GameLaunchSettings? settings)
    {
        if (processDetector.IsRunning(processName))
            return new GameLaunchResult(GameLaunchStatus.AlreadyRunning, $"{gameName} is already running.");

        var executable = settings?.ExecutablePath?.Trim();
        if (string.IsNullOrWhiteSpace(executable))
            return new GameLaunchResult(GameLaunchStatus.NotConfigured, "Configure the launch executable in Settings.");
        if (!File.Exists(executable))
            return new GameLaunchResult(GameLaunchStatus.ExecutableNotFound, $"The configured {gameName} launch executable does not exist.");

        try
        {
            var configuredWorkingDirectory = settings?.WorkingDirectory?.Trim();
            var workingDirectory = string.IsNullOrWhiteSpace(configuredWorkingDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(executable))!
                : Path.GetFullPath(configuredWorkingDirectory);
            if (!Directory.Exists(workingDirectory))
                return new GameLaunchResult(GameLaunchStatus.Failed, $"The configured {gameName} working directory does not exist.");

            using var process = processStarter.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executable),
                Arguments = settings?.Arguments?.Trim() ?? string.Empty,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true
            });
            return new GameLaunchResult(GameLaunchStatus.Started, $"Starting {gameName}…");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException or Win32Exception)
        {
            return new GameLaunchResult(GameLaunchStatus.Failed, $"{gameName} could not be started: {ex.Message}");
        }
    }
}
