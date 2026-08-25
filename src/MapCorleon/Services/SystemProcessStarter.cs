using System.Diagnostics;
using MapCorleon.Contracts;

namespace MapCorleon.Services;

public sealed class SystemProcessStarter : IProcessStarter
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
