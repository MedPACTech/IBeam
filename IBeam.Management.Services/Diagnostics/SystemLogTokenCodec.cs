using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>The shared AES-GCM token format keyed by the log reader's own protection key.</summary>
public sealed class SystemLogTokenCodec
{
    private readonly DiagnosticTokenCodec _codec;
    public SystemLogTokenCodec(IOptions<SystemLogReaderOptions> options)
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Explicit diagnostic source isolation, bounded options and a 32-byte protection key are required.");
        _codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
    }

    public string Protect<T>(string purpose, string context, T value) => _codec.Protect(purpose, context, value);
    public T Unprotect<T>(string purpose, string context, string token) => _codec.Unprotect<T>(purpose, context, token);
}
