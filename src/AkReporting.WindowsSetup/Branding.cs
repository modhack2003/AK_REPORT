using System.Security.AccessControl;
using System.Security.Principal;
using AkReporting.Deployment;

namespace AkReporting.WindowsSetup;

internal static class Branding
{
    public static void Validate(byte[] png)
    {
        if (png.Length < 33 || png.Length > 2 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("Choose a PNG logo smaller than 2 MiB.");
        using var stream = new MemoryStream(png);
        using var image = System.Drawing.Image.FromStream(stream, false, true);
        if (image.Width > 4096 || image.Height > 4096) throw new InvalidOperationException("Logo dimensions must not exceed 4096 × 4096 pixels.");
    }
    public static void Install(byte[] png)
    {
        Validate(png);
        var folder = Path.Combine(InstallationPaths.DataRoot, "branding");
        WindowsSecurity.DirectoryAcl(folder);
        var directory = new DirectoryInfo(folder); var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        var temporary = Path.Combine(folder, "logo-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, png); File.Move(temporary, Path.Combine(folder, "logo.png"), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
