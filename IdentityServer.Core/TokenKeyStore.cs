using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IdentityServer;

/// <summary>Stores token keys using Windows DPAPI or password-encrypted PKCS#8.</summary>
internal static class TokenKeyStore
{
    internal static void Validate(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("IdentityServer:PersistKeys") && !OperatingSystem.IsWindows() &&
            string.IsNullOrEmpty(configuration["IdentityServer:KeyProtectionPassword"]))
            throw new InvalidOperationException("Persistent keys on Linux require IdentityServer:KeyProtectionPassword. Supply it through deployment secrets, separately from IdentityServer:DataPath.");
    }

    internal static RsaSecurityKey LoadOrCreate(IConfiguration configuration, string name)
    {
        Validate(configuration);
        string? password = configuration["IdentityServer:KeyProtectionPassword"];
        bool portable = !string.IsNullOrEmpty(password);
        string dataPath = configuration["IdentityServer:DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dataPath);
        else Directory.CreateDirectory(dataPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string keyPath = Path.Combine(dataPath, name + (portable ? ".pem" : ".bin"));
        string otherPath = Path.Combine(dataPath, name + (portable ? ".bin" : ".pem"));
        var rsa = RSA.Create(2048);
        try
        {
            if (!File.Exists(keyPath))
            {
                if (File.Exists(otherPath))
                    throw new InvalidOperationException("A token key exists in a different protection format. IdentityServer:KeyProtectionPassword must match the stored key's protection; keys will not be silently replaced.");
                byte[] encrypted;
                if (portable)
                    encrypted = Encoding.UTF8.GetBytes(rsa.ExportEncryptedPkcs8PrivateKeyPem(password!,
                        new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200_000)));
                else
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
                    byte[] plaintext = Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem());
                    try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.LocalMachine); }
                    finally { CryptographicOperations.ZeroMemory(plaintext); }
                }
                WriteNewKey(keyPath, encrypted);
            }
            // Always read the winning file, including when another process created it concurrently.
            if (portable)
                rsa.ImportFromEncryptedPem(File.ReadAllText(keyPath), password!);
            else
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
                byte[] plaintext = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.LocalMachine);
                try { rsa.ImportFromPem(Encoding.UTF8.GetString(plaintext)); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            return new RsaSecurityKey(rsa);
        }
        catch { rsa.Dispose(); throw; }
    }

    private static void WriteNewKey(string path, byte[] encrypted)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) { file.Write(encrypted); file.Flush(true); }
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path)) { /* Another process won; load its complete key. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
