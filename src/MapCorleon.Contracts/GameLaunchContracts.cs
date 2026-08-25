using System.Diagnostics;

namespace MapCorleon.Contracts;

public sealed record GameLaunchSettings(
    string? ExecutablePath = null,
    string? Arguments = null,
    string? WorkingDirectory = null);

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

public interface IGameLauncher
{
    string Key { get; }
    bool Supports(GameMapDescriptor map);
    GameLaunchResult Launch(GameLaunchSettings? settings);
}
