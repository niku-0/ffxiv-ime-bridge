using FfxivImeBridge.Fcitx.Diagnostics;

// Usage: TransportProbe [bus-address] [uid]
// Climbs the ladder and prints each rung as it happens. Exit code 0 when
// fcitx5 composed a preedit, 1 otherwise. Run it under Wine with
// Dalamud's runtime to test the AF_UNIX rung without the game:
//   WINEPREFIX=... wine ~/.xlcore/runtime/dotnet.exe FfxivImeBridge.TransportProbe.dll

var settings = new ProbeSettings
{
    Address = args.Length > 0 ? args[0] : null,
    UnixUserId = args.Length > 1 ? uint.Parse(args[1]) : null,
};

Console.OutputEncoding = System.Text.Encoding.UTF8;
var report = await TransportProbe.RunAsync(settings, step => Console.WriteLine(step));
Console.WriteLine(report.Summary);
return report.Succeeded ? 0 : 1;
