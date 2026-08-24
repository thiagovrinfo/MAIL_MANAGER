using System.Diagnostics;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public static class WindowsAutostart
{
    public static bool IsEnabled()
    {
        var result = Run("schtasks", $"/Query /TN \"{MailConstants.AutostartTaskName}\" /FO LIST");
        return result.ExitCode == 0;
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        if (!enabled)
        {
            Run("schtasks", $"/Delete /TN \"{MailConstants.AutostartTaskName}\" /F");
            return;
        }

        var quoted = executablePath.Replace("\"", "\\\"");
        Run("schtasks",
            $"/Create /TN \"{MailConstants.AutostartTaskName}\" /TR \"\\\"{quoted}\\\" --tray\" /SC ONLOGON /RL LIMITED /F");
    }

    private static (int ExitCode, string Output) Run(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(info);
        if (process is null)
            return (-1, string.Empty);
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
