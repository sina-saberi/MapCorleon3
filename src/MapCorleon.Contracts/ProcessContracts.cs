namespace MapCorleon.Contracts;

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

public interface IProcessMemorySession : IDisposable
{
    float ReadSingle(nint address);
}

public interface IProcessMemoryReader
{
    IProcessMemorySession Open(int processId);
}
