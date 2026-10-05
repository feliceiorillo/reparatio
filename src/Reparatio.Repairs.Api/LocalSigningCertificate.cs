using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Reparatio.Repairs.Api;

// Local development credentials: persistent private keys protected by Windows DPAPI.
public static class LocalSigningCertificate
{
    public static X509Certificate2 Load(string purpose)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Configure certificates explicitly outside Windows.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Codex", ".secrets", "reparatio");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, purpose + ".pfx.dpapi");
        if (!File.Exists(path))
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=Reparatio local " + purpose, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(purpose == "signing" ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.KeyEncipherment, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
            var bytes = certificate.Export(X509ContentType.Pfx);
            try { File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return X509CertificateLoader.LoadPkcs12(plaintext, null, X509KeyStorageFlags.EphemeralKeySet); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
