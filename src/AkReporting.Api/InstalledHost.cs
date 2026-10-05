using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using AkReporting.Deployment;

namespace AkReporting.Api;

internal static class InstalledHost
{
    public static string? Configure(WebApplicationBuilder builder, string[] args)
    {
        if (!args.Contains("--installed-host", StringComparer.Ordinal)) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installed host requires Windows.");
        return ConfigureWindows(builder);
    }
    [SupportedOSPlatform("windows")]
    private static string ConfigureWindows(WebApplicationBuilder builder)
    {
        var settings = ProtectedConfiguration.Read<InstalledHostSettings>(InstallationPaths.HostSettings(InstallationPaths.DataRoot));
        using var publicCertificate = X509CertificateLoader.LoadPkcs12(settings.ServerPfx, settings.PfxPassword, X509KeyStorageFlags.EphemeralKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, publicCertificate.Thumbprint, false)
            .OfType<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey)
            ?? throw new InvalidOperationException("Installed HTTPS certificate is missing. Run local setup/repair.");
        // Pin the installation listener. Environment/config URLs cannot enable plaintext or LAN listeners.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(endpoint => endpoint.UseHttps(certificate));
            options.Listen(IPAddress.Loopback, InstallationPaths.HttpsPort, endpoint => endpoint.UseHttps(certificate));
        });
        return settings.RuntimeConnection;
    }
}
