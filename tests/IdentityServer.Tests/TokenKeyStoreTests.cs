using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static IdentityServer.Tests.IdentityProviderTests;

namespace IdentityServer.Tests;

public sealed class TokenKeyStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "identityserver-keys-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EncryptedKeysSurviveRestart_AndWrongPasswordsDoNotReplaceThem()
    {
        var configuration = Configuration(("IdentityServer:PersistKeys", "true"), ("IdentityServer:DataPath", _path),
            ("IdentityServer:KeyProtectionPassword", "a-separate-test-deployment-secret"));
        using var first = TokenKeyStore.LoadOrCreate(configuration, "signing-key").Rsa;
        using var encryption = TokenKeyStore.LoadOrCreate(configuration, "encryption-key").Rsa;
        byte[] data = Encoding.UTF8.GetBytes("restart continuity");
        byte[] signature = first.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        byte[] ciphertext = encryption.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
        using var second = TokenKeyStore.LoadOrCreate(configuration, "signing-key").Rsa;
        using var secondEncryption = TokenKeyStore.LoadOrCreate(configuration, "encryption-key").Rsa;
        Assert.True(second.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal(data, secondEncryption.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256));
        string keyFile = Path.Combine(_path, "signing-key.pem");
        string original = File.ReadAllText(keyFile);
        Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", original);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", original);
        var wrong = Configuration(("IdentityServer:PersistKeys", "true"), ("IdentityServer:DataPath", _path),
            ("IdentityServer:KeyProtectionPassword", "wrong-password"));
        Assert.ThrowsAny<CryptographicException>(() => TokenKeyStore.LoadOrCreate(wrong, "signing-key"));
        Assert.Equal(original, File.ReadAllText(keyFile));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyFile));
    }

    [Fact]
    public void ExistingWindowsDpapiFileRemainsUsableWithoutNewConfiguration()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("DPAPI requires Windows.");
        Directory.CreateDirectory(_path);
        using var original = RSA.Create(2048);
        File.WriteAllBytes(Path.Combine(_path, "signing-key.bin"),
            ProtectedData.Protect(Encoding.UTF8.GetBytes(original.ExportPkcs8PrivateKeyPem()), null, DataProtectionScope.LocalMachine));
        var configuration = Configuration(("IdentityServer:PersistKeys", "true"), ("IdentityServer:DataPath", _path));
        using var loaded = TokenKeyStore.LoadOrCreate(configuration, "signing-key").Rsa;
        Assert.Equal(original.ExportSubjectPublicKeyInfo(), loaded.ExportSubjectPublicKeyInfo());
        Assert.False(File.Exists(Path.Combine(_path, "signing-key.pem")));
    }

    [Fact]
    public void ChangingProtectionFormatRequiresDeliberateMigration()
    {
        Directory.CreateDirectory(_path);
        File.WriteAllText(Path.Combine(_path, "signing-key.bin"), "existing-machine-protected-key");
        var configuration = Configuration(("IdentityServer:PersistKeys", "true"), ("IdentityServer:DataPath", _path),
            ("IdentityServer:KeyProtectionPassword", "separate-secret"));
        Assert.Throws<InvalidOperationException>(() => TokenKeyStore.LoadOrCreate(configuration, "signing-key"));
        Assert.False(File.Exists(Path.Combine(_path, "signing-key.pem")));
    }

    [Fact]
    public void WrongKeyPasswordFailsHostStartup_BeforeServingRequests()
    {
        var configuration = Configuration(("IdentityServer:PersistKeys", "true"), ("IdentityServer:DataPath", _path),
            ("IdentityServer:KeyProtectionPassword", "original-test-secret"));
        using var key = TokenKeyStore.LoadOrCreate(configuration, "signing-key").Rsa;
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, settings) => settings
                .AddConfiguration(LdapConfiguration())
                .AddConfiguration(configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["IdentityServer:KeyProtectionPassword"] = "wrong-test-secret" })));
        Assert.ThrowsAny<CryptographicException>(() => host.CreateClient());
    }

    public void Dispose()
    {
        // This path is a unique test-owned directory under the system temporary directory.
        if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
    }
}
