namespace MapCorleon.Models;

public sealed record GameLaunchSettings(
    string? ExecutablePath = null,
    string? Arguments = null,
    string? WorkingDirectory = null);
