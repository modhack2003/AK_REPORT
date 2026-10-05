using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AkReporting.Deployment;

public sealed class InstalledHostSettings
{
    public string RuntimeConnection { get; set; } = "";
    public byte[] ServerPfx { get; set; } = [];
    public string PfxPassword { get; set; } = "";
    public byte[] RootCertificate { get; set; } = [];
    public DateTimeOffset CertificateExpires { get; set; }
}
public sealed class InstalledOwnerSettings
{
    public string OwnerConnection { get; set; } = "";
    public string RuntimePassword { get; set; } = "";
    public string InstallRoot { get; set; } = "";
}

/// <summary>Machine-bound encryption plus service/administrator ACLs supplied by setup.</summary>
[SupportedOSPlatform("windows")]
public static class ProtectedConfiguration
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AKReporting.Installation.v1");
    public static T Read<T>(string path)
    {
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine);
        try { return JsonSerializer.Deserialize<T>(clear) ?? throw new InvalidDataException("Invalid installed configuration."); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public static void Write<T>(string path, T value)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.LocalMachine); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(encrypted); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
