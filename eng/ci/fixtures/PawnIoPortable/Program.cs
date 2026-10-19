using System;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.PawnIo;

// A separate process is essential: injected Unix flags on a Windows test host
// cannot exercise the real platform branch of PawnIo's type initializer.
Console.WriteLine($"PawnIO smoke on {RuntimeInformation.OSDescription}");
bool isUnix = LibreHardwareMonitor.Software.OperatingSystem.IsUnix;
Check(isUnix == !RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "real platform detection");

if (!isUnix)
{
    Check(PawnIo.IsInstalled == (PawnIo.Version != null), "Windows installation metadata consistency");
    Check(PawnIo.FindInstalledVersion(isUnix: true) == null, "injected Unix probe");
}
else
{
    Check(PawnIo.Version == null && !PawnIo.IsInstalled, "real Unix type initialization skips registry");
}

// Deliberately missing resource: an unsupported platform must return before
// attempting either a device open or a resource lookup.
PawnIo module = isUnix
    ? PawnIo.LoadModuleFromResource(typeof(PawnIo).Assembly, "missing-resource")
    : PawnIo.LoadModuleFromResource(typeof(PawnIo).Assembly, "missing-resource", isUnix: true);
Check(!module.IsLoaded, "unsupported module is not loaded");
Check(module.Execute("fixture", [1], 2).SequenceEqual(new long[] { 0, 0 }), "unloaded Execute returns zero values");
long[] output = [17, 23];
int result = module.ExecuteHr("fixture", [1], 1, output, 2, out uint returned);
Check(result == 0 && returned == 0 && output.SequenceEqual(new long[] { 17, 23 }), "unloaded ExecuteHr reports no output without changing buffers");
module.Close();
module.Close();
Check(!module.IsLoaded, "repeated Close is harmless");

if (isUnix)
{
    new AmdFamily17().Close();
    new IntelMsr().Close();
    new RyzenSmu().Close();
    Console.WriteLine("PASS: real Unix PawnIO-backed module construction");
}
Console.WriteLine(isUnix ? "PASS: real non-Windows acceptance" : "PASS: Windows guard fixture (non-Windows acceptance is separate)");

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
}
