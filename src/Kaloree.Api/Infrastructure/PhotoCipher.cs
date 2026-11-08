using System.Security.Cryptography;

namespace Kaloree.Api.Infrastructure;

public sealed class PhotoCipher
{
    private readonly byte[] key;
    public PhotoCipher(IConfiguration config)
    {
        key = Convert.FromBase64String(config["AI:EncryptionKey"] ?? throw new InvalidOperationException("AI:EncryptionKey is required"));
        if (key.Length != 32) throw new InvalidOperationException("AI:EncryptionKey must encode 32 bytes");
    }
    public byte[] Encrypt(byte[] plain, Guid owner)
    {
        var output = new byte[28 + plain.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(output.AsSpan(0, 12), plain, output.AsSpan(28), output.AsSpan(12, 16), owner.ToByteArray());
        return output;
    }
    public byte[] Decrypt(byte[] encrypted, Guid owner)
    {
        var output = new byte[encrypted.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(encrypted.AsSpan(0, 12), encrypted.AsSpan(28), encrypted.AsSpan(12, 16), output, owner.ToByteArray());
        return output;
    }
}
