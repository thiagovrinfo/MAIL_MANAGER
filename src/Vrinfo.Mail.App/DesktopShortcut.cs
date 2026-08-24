using System.IO;
using System.Runtime.InteropServices;

namespace Vrinfo.Mail.App;

internal static class DesktopShortcut
{
    public static void Ensure()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "VRINFO Mail.lnk");
            var icon = File.Exists(AppIcon.IcoPath) ? AppIcon.IcoPath : exe;

            var wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic shortcut = ((dynamic)wsh!).CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.WindowStyle = 1;
            shortcut.Description = "VRINFO Mail";
            shortcut.IconLocation = icon + ",0";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(wsh);

            NativeMethods.NotifyFileUpdated(link);
            NativeMethods.NotifyAssociationChanged();
        }
        catch
        {
            // atalho é best-effort
        }
    }

    private static class NativeMethods
    {
        private const uint ShcneAssocchanged = 0x08000000;
        private const uint ShcneUpdateItem = 0x00002000;
        private const uint ShcnfFlush = 0x1000;
        private const uint ShcnfPathW = 0x0005;
        private const uint ShcnfIdList = 0x0000;

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

        public static void NotifyAssociationChanged()
            => SHChangeNotify(ShcneAssocchanged, ShcnfIdList | ShcnfFlush, IntPtr.Zero, IntPtr.Zero);

        public static void NotifyFileUpdated(string path)
            => SHChangeNotify(ShcneUpdateItem, ShcnfPathW | ShcnfFlush, path, IntPtr.Zero);
    }
}
