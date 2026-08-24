using System.Diagnostics;

namespace Vrinfo.Mail.App;

internal static class SingleInstance
{
    public static void ReplaceRunning()
    {
        var current = Process.GetCurrentProcess();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            current.ProcessName,
            "VRINFO.Mail"
        };

        foreach (var name in names)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in found)
            {
                using (process)
                {
                    if (process.Id == current.Id)
                        continue;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(8000);
                    }
                    catch
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(4000);
                        }
                        catch
                        {
                            // a nova instância segue mesmo assim
                        }
                    }
                }
            }
        }
    }
}
