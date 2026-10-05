using System.Security.AccessControl;
using System.Security.Principal;

namespace AkReporting.WindowsSetup;

internal static class WindowsSecurity
{
    public static void RequireAdministrator()
    {
        if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Major < 10)
            throw new InvalidOperationException("The local package requires 64-bit Windows 10 or 11.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Run Windows setup as an administrator.");
    }
    public static void DirectoryAcl(string path, params (string Account, FileSystemRights Rights, bool Inherit)[] grants)
    {
        RejectReparsePath(path);
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var id in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(id, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        foreach (var (account, rights, inherit) in grants)
            security.AddAccessRule(new FileSystemAccessRule(new NTAccount(account), rights,
                inherit ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None,
                PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
    public static void RejectReparsePath(string path)
    {
        var item = new DirectoryInfo(Path.GetFullPath(path));
        while (item != null)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Installation/data directories cannot contain junctions or symbolic links.");
            item = item.Parent;
        }
    }
}
