using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.Models;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>AES-GCM hides provider cursors and physical keys and authenticates purpose/source/deployment/query.</summary>
public sealed class DiagnosticTokenCodec
{
    private readonly byte[] _key;
    public DiagnosticTokenCodec(IOptions<SystemErrorReaderOptions> options)
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Explicit diagnostic source isolation, bounded options and a 32-byte protection key are required.");
        _key = Convert.FromBase64String(options.Value.ProtectionKey);
    }

    public string Protect<T>(string purpose, string context, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        if (plain.Length > 12000) throw new DiagnosticTokenException();
        var data = new byte[12 + 16 + plain.Length];
        RandomNumberGenerator.Fill(data.AsSpan(0, 12));
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(data.AsSpan(0, 12), plain, data.AsSpan(28), data.AsSpan(12, 16), Aad(purpose, context));
        return "d1_" + Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public T Unprotect<T>(string purpose, string context, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 16384 || !token.StartsWith("d1_", StringComparison.Ordinal))
            throw new DiagnosticTokenException();
        try
        {
            var encoded = token[3..];
            if (!encoded.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new DiagnosticTokenException();
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            var data = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
            if (data.Length < 29 || Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_') != encoded)
                throw new DiagnosticTokenException();
            var plain = new byte[data.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, Aad(purpose, context));
            return JsonSerializer.Deserialize<T>(plain) ?? throw new DiagnosticTokenException();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        { throw new DiagnosticTokenException(); }
    }

    private static byte[] Aad(string purpose, string context)
        => Encoding.UTF8.GetBytes("IBeam.Management.Diagnostics.v1|" + purpose + "|" + context);
}
