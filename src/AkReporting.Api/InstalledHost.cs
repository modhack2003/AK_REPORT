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
        var certificate = X509CertificateLoader.LoadPkcs12(settings.ServerPfx, settings.PfxPassword, X509KeyStorageFlags.EphemeralKeySet);
        // Pin the installation listener. Environment/config URLs cannot enable plaintext or LAN listeners.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(endpoint => endpoint.UseHttps(certificate));
            options.Listen(IPAddress.Loopback, InstallationPaths.HttpsPort, endpoint => endpoint.UseHttps(certificate));
        });
        return settings.RuntimeConnection;
    }
}
