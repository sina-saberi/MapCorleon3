using System.Diagnostics;
using System.IO;
using MapCorleon.Models;

namespace MapCorleon.Services;

public enum GameLaunchStatus
{
    Started,
    AlreadyRunning,
    NotConfigured,
    ExecutableNotFound,
    Failed
}

public sealed record GameLaunchResult(GameLaunchStatus Status, string Message);

public interface IProcessStarter
{
    Process? Start(ProcessStartInfo startInfo);
}

public sealed class SystemProcessStarter : IProcessStarter
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}

public interface IGameLauncher
{
    GameLaunchResult Launch(GameLaunchSettings? settings);
}

public sealed class SkyrimGameLauncher(IGameProcessDetector processDetector, IProcessStarter processStarter) : IGameLauncher
{
    public GameLaunchResult Launch(GameLaunchSettings? settings)
    {
        if (processDetector.IsRunning(SkyrimPlayerLocationProvider.TargetProcessName))
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
