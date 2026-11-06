using System.Globalization;
using MapCorleon.Contracts;
using MapCorleon.Games.Fallout4;
using MapCorleon.Services;

var seconds = 120;
if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out seconds) || seconds is < 1 or > 900)))
{
    Console.WriteLine("Usage: MapCorleon.Fallout4Probe [seconds]");
    Console.WriteLine("Reads a running Fallout4.exe once per second and prints Commonwealth coordinates. Press Ctrl+C to stop.");
    return args is ["--help"] or ["-h"] ? 0 : 2;
}

var provider = new Fallout4PlayerLocationProvider(new WindowsGameProcessDetector(), new WindowsProcessMemoryReader());
var map = new GameMapDescriptor(0, "Fallout 4", "Commonwealth");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
try
{
    for (var sample = 0; sample < seconds; sample++)
    {
        var result = await provider.ReadAsync(map, stop.Token);
        var position = result.Location is { } point
            ? string.Create(CultureInfo.InvariantCulture, $" X={point.GameX:F2} Y={point.GameY:F2} Z={point.GameZ:F2} Lat={point.Latitude:F8} Lon={point.Longitude:F8}")
            : string.Empty;
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} {result.Status}{position} {result.Diagnostic}");
        if (sample + 1 < seconds)
            await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
    }
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
finally { provider.Detach(); }
return 0;
