using System.Diagnostics;

namespace MapCorleon.Services;

public sealed record GameProcessInfo(
    int Id,
    nint ModuleBaseAddress,
    string ExecutablePath,
    string FileVersion);

public interface IGameProcessDetector
{
    GameProcessInfo? Find(string processName);
    bool IsRunning(string processName);
}

public sealed class WindowsGameProcessDetector : IGameProcessDetector
{
    public GameProcessInfo? Find(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var module = process.MainModule;
                    if (module is null || string.IsNullOrWhiteSpace(module.FileName)) continue;
                    return new GameProcessInfo(
                        process.Id,
                        module.BaseAddress,
                        module.FileName,
                        module.FileVersionInfo.FileVersion ?? string.Empty);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        return null;
    }

    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Length > 0; }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
