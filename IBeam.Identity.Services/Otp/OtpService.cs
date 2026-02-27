using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Otp;

public sealed class OtpService : IOtpService
{
    private readonly IOtpChallengeStore _store;
    private readonly IOptionsMonitor<OtpOptions> _options;
    private readonly IIdentityCommunicationSender _sender;

    public OtpService(
        IOtpChallengeStore store,
        IIdentityCommunicationSender sender,
        IOptionsMonitor<OtpOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OtpChallengeResult> CreateChallengeAsync(OtpChallengeRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Destination))
            throw new IdentityValidationException("Destination is required.");

        var opts = _options.CurrentValue;

        var challengeId = Guid.NewGuid().ToString("D");
        var code = GenerateNumericCode(opts.CodeLength);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(opts.ExpirationMinutes);

        var normalizedDestination = request.Destination.Trim();
        var record = new OtpChallengeRecord(
            ChallengeId: challengeId,
            Destination: normalizedDestination,
            Purpose: request.Purpose,
            CodeHash: HashCode(code, opts.HashSalt),
            ExpiresAt: expiresAt,
            AttemptCount: 0,
            TenantId: request.TenantId,
            IsConsumed: false,
            VerificationToken: null,
            VerificationTokenExpiresAt: null);

        await _store.SaveAsync(record, ct);

        // send plaintext code via configured delivery mechanism
        var message = new IdentitySenderMessage
        {
            Channel = request.Channel,
            Destination = normalizedDestination,
            Code = code,
            Purpose = request.Purpose,
            TenantId = request.TenantId,
            ExpiresAt = expiresAt
            // Add more properties as needed
        };
        await _sender.SendAsync(message, ct);

        return new OtpChallengeResult(record.ChallengeId, record.ExpiresAt);
    }

    public async Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ChallengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new IdentityValidationException("Code is required.");

        var opts = _options.CurrentValue;

        var record = await _store.GetAsync(request.ChallengeId, ct);
        if (record is null)
            return new OtpVerifyResult(false);

        var now = DateTimeOffset.UtcNow;

        if (record.IsConsumed)
            return new OtpVerifyResult(false);

        if (record.ExpiresAt <= now)
            return new OtpVerifyResult(false);

        if (record.AttemptCount >= opts.MaxAttempts)
            return new OtpVerifyResult(false);

        var providedHash = HashCode(request.Code.Trim(), opts.HashSalt);
        var ok = FixedTimeEquals(record.CodeHash, providedHash);

        if (!ok)
        {
            await _store.IncrementAttemptAsync(record.ChallengeId, ct);
            return new OtpVerifyResult(false);
        }

        var verificationToken = CreateVerificationToken(record, opts.VerificationTokenSecret);
        var tokenExpiresAt = now.AddMinutes(opts.VerificationTokenMinutes);

        await _store.MarkConsumedAsync(record.ChallengeId, verificationToken, tokenExpiresAt, ct);

        return new OtpVerifyResult(
            Success: true,
            VerificationToken: verificationToken,
            ExpiresAt: tokenExpiresAt);
    }

    private static string GenerateNumericCode(int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)('0' + (bytes[i] % 10));

        return new string(chars);
    }

    private static string HashCode(string code, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes($"{salt}:{code}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string CreateVerificationToken(OtpChallengeRecord record, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new IdentityProviderException("OTP verification token secret is not configured.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{record.ChallengeId}|{record.Destination}|{record.Purpose}|{record.TenantId}|{now}";
        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(bytes);
    }
}
