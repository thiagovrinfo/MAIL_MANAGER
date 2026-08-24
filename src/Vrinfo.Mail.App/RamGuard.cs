using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Vrinfo.Mail.App;

internal static class RamGuard
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void EnableProcessLimits()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch
        {
            // prioridade reduzida é opcional
        }

        try
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch
        {
            // modo de GC indisponível
        }
    }

    public static void Trim()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
        }
        catch
        {
            // trim best-effort
        }
    }
}
