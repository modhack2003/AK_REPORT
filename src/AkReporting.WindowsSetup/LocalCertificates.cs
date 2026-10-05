using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AkReporting.Deployment;

namespace AkReporting.WindowsSetup;

internal static class LocalCertificates
{
    public static void Create(InstalledHostSettings settings)
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        using var rootKey = RSA.Create(3072);
        var request = new CertificateRequest("CN=AK Reporting Local " + Guid.NewGuid().ToString("N"), rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var root = request.CreateSelfSigned(start, start.AddYears(5));
        using var key = RSA.Create(3072);
        var server = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        server.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        server.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        server.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        server.CertificateExtensions.Add(names.Build());
        using var publicServer = server.Create(root, start, start.AddYears(2), RandomNumberGenerator.GetBytes(16));
        using var privateServer = publicServer.CopyWithPrivateKey(key);
        settings.PfxPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        settings.ServerPfx = privateServer.Export(X509ContentType.Pfx, settings.PfxPassword);
        settings.RootCertificate = root.Export(X509ContentType.Cert);
        settings.CertificateExpires = privateServer.NotAfter.ToUniversalTime();
        // CA private key is deliberately not retained: this trust anchor cannot issue other certificates later.
    }
    public static void Trust(InstalledHostSettings settings)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(settings.RootCertificate);
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0) store.Add(certificate);
    }
    public static void RemoveTrust(InstalledHostSettings settings)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(settings.RootCertificate);
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadWrite);
        foreach (var existing in store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false)) store.Remove(existing);
    }
}
