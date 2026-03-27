using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RemoteCtl.Services;

/// <summary>
/// AES-256-GCM encryption for stored credentials.
/// Key is auto-generated on first use and stored at configDir/key (chmod 600 on Unix).
/// Encrypted values are stored with the "enc:" prefix so plaintext fallback is safe.
/// </summary>
public class CryptoService
{
    private const string Prefix    = "enc:";
    private const int    KeyBytes  = 32;  // AES-256
    private const int    NonceSize = 12;  // GCM standard
    private const int    TagSize   = 16;  // 128-bit auth tag

    private readonly string _keyPath;
    private byte[]? _keyCache;

    public CryptoService(string configDir)
    {
        _keyPath = Path.Combine(configDir, "key");
    }

    public bool IsEncrypted(string? value) => value?.StartsWith(Prefix) == true;

    public string Encrypt(string plaintext)
    {
        var key            = LoadOrCreateKey();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce          = new byte[NonceSize];
        var tag            = new byte[TagSize];
        var ciphertext     = new byte[plaintextBytes.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Layout: nonce(12) | tag(16) | ciphertext(n)
        var blob = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce,       0, blob, 0,                    NonceSize);
        Buffer.BlockCopy(tag,         0, blob, NonceSize,            TagSize);
        Buffer.BlockCopy(ciphertext,  0, blob, NonceSize + TagSize,  ciphertext.Length);

        return Prefix + Convert.ToBase64String(blob);
    }

    public string Decrypt(string value)
    {
        if (!IsEncrypted(value))
            return value; // passthrough for plaintext

        var key  = LoadOrCreateKey();
        var blob = Convert.FromBase64String(value[Prefix.Length..]);

        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted value is too short — may be corrupt.");

        var nonce      = blob[..NonceSize];
        var tag        = blob[NonceSize..(NonceSize + TagSize)];
        var ciphertext = blob[(NonceSize + TagSize)..];
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] LoadOrCreateKey()
    {
        if (_keyCache is not null) return _keyCache;

        if (File.Exists(_keyPath))
        {
            _keyCache = File.ReadAllBytes(_keyPath);
            if (_keyCache.Length != KeyBytes)
                throw new CryptographicException($"Key file at {_keyPath} is invalid (expected {KeyBytes} bytes).");
            return _keyCache;
        }

        // First run: generate and persist key
        _keyCache = new byte[KeyBytes];
        RandomNumberGenerator.Fill(_keyCache);

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
        File.WriteAllBytes(_keyPath, _keyCache);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Restrict read access to owner only
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("chmod", $"600 \"{_keyPath}\"")
                    { UseShellExecute = false })?.WaitForExit();
            }
            catch { /* non-fatal; key is still generated */ }
        }

        return _keyCache;
    }
}
