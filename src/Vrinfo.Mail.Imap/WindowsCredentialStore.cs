using System.Runtime.InteropServices;
using System.Text;
using Vrinfo.Mail.Core;

namespace Vrinfo.Mail.Imap;

public static class WindowsCredentialStore
{
    public static void Save(string email, string password)
    {
        var target = MailConstants.CredentialPrefix + email.Trim();
        var credential = new Native.CREDENTIAL
        {
            Type = Native.CRED_TYPE_GENERIC,
            TargetName = target,
            UserName = email.Trim(),
            Persist = Native.CRED_PERSIST_LOCAL_MACHINE,
            AttributeCount = 0,
            Comment = MailConstants.ProductName
        };

        var bytes = Encoding.Unicode.GetBytes(password);
        credential.CredentialBlobSize = (uint)bytes.Length;
        credential.CredentialBlob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
            if (!Native.CredWrite(ref credential, 0))
                throw new InvalidOperationException("Não foi possível gravar a senha no Credential Manager.");
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public static string? Read(string email)
    {
        var target = MailConstants.CredentialPrefix + email.Trim();
        if (!Native.CredRead(target, Native.CRED_TYPE_GENERIC, 0, out var ptr) || ptr == nint.Zero)
            return null;

        try
        {
            var cred = Marshal.PtrToStructure<Native.CREDENTIAL>(ptr);
            if (cred.CredentialBlob == nint.Zero || cred.CredentialBlobSize == 0)
                return null;

            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            Native.CredFree(ptr);
        }
    }

    public static bool Delete(string email)
    {
        var target = MailConstants.CredentialPrefix + email.Trim();
        return Native.CredDelete(target, Native.CRED_TYPE_GENERIC, 0);
    }

    private static class Native
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public nint CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public nint Attributes;
            public string? TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, int type, int flags, out nint credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree(nint buffer);
    }
}
