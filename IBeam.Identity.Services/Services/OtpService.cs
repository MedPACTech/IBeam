using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Core.Entities;
using IBeam.Identity.Services.Otp.Options;
using IBeam.Identity.Core.Otp.Contracts;
using IBeam.Identity.Core.Otp.Interfaces;
using IBeam.Identity.Services.Otp;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services;

public sealed class OtpService : IOtpService
{
    private readonly IOtpChallengeStore _store;
    private readonly IOtpSender _sender;
    private readonly IOptionsMonitor<OtpOptions> _options;

    public OtpService(
        IOtpChallengeStore store,
        IOtpSender sender,
        IOptionsMonitor<OtpOptions> options)
    {
        _store = store;
        _sender = sender;
        _options = options;
    }

    public async Task<CreateOtpChallengeResponse> CreateChallengeAsync(CreateOtpChallengeRequest req, CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        ValidateCreate(req);

        var opts = _options.CurrentValue;

        // Create challenge
        var challengeId = Guid.NewGuid();
        var code = GenerateNumericCode(opts.CodeLength);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(opts.CodeTtl);

        var entity = new OtpChallengeEntity
        {
            ChallengeId = challengeId,
            Purpose = req.Purpose,
            Channel = req.Channel,
            Destination = req.To.Trim(),
            TenantHint = string.IsNullOrWhiteSpace(req.TenantHint) ? null : req.TenantHint.Trim(),
            CodeHash = HashCode(code, opts.HashSalt),
            ExpiresAt = expiresAt,
            CreatedAt = now,
            Attempts = 0,
            MaxAttempts = opts.MaxVerifyAttempts,
            ResendAfter = now.Add(opts.ResendCooldown),
            IsConsumed = false
        };

        await _store.CreateAsync(entity, ct);

        // Send code (provider-agnostic sender)
        await _sender.SendAsync(req.Channel, entity.Destination, code, req.Purpose, ct);

        return new CreateOtpChallengeResponse(
            ChallengeId: challengeId,
            ExpiresAt: expiresAt,
            MaskedDestination: MaskDestination(req.Channel, entity.Destination)
        );
    }

    public async Task<VerifyOtpChallengeResponse> VerifyChallengeAsync(VerifyOtpChallengeRequest req, CancellationToken ct)
    {
        if (req is null) throw new ArgumentNullException(nameof(req));
        if (req.ChallengeId == Guid.Empty) throw new ArgumentException("ChallengeId is required.", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Code)) throw new ArgumentException("Code is required.", nameof(req));

        var opts = _options.CurrentValue;

        var entity = await _store.GetAsync(req.ChallengeId, ct);
        if (entity is null)
            return new VerifyOtpChallengeResponse(false, null, null);

        var now = DateTimeOffset.UtcNow;

        if (entity.IsConsumed)
            return new VerifyOtpChallengeResponse(false, null, null);

        if (entity.ExpiresAt <= now)
            return new VerifyOtpChallengeResponse(false, null, entity.ExpiresAt);

        if (entity.Attempts >= entity.MaxAttempts)
            return new VerifyOtpChallengeResponse(false, null, entity.ExpiresAt);

        var codeHash = HashCode(req.Code.Trim(), opts.HashSalt);
        var ok = FixedTimeEquals(entity.CodeHash, codeHash);

        await _store.IncrementAttemptsAsync(entity.ChallengeId, ct);

        if (!ok)
            return new VerifyOtpChallengeResponse(false, null, entity.ExpiresAt);

        // Create a one-time verification token (to be used by password reset / registration / otp-login)
        var token = CreateVerificationToken(entity, opts.VerificationTokenTtl);

        await _store.MarkConsumedAsync(entity.ChallengeId, token, now, now.Add(opts.VerificationTokenTtl), ct);

        return new VerifyOtpChallengeResponse(true, token, now.Add(opts.VerificationTokenTtl));
    }

    public async Task<ResendOtpChallengeResponse> ResendChallengeAsync(Guid challengeId, CancellationToken ct)
    {
        if (challengeId == Guid.Empty) throw new ArgumentException("challengeId is required.", nameof(challengeId));

        var opts = _options.CurrentValue;

        var entity = await _store.GetAsync(challengeId, ct);
        if (entity is null)
            throw new InvalidOperationException("Challenge not found.");

        var now = DateTimeOffset.UtcNow;

        if (entity.IsConsumed)
            throw new InvalidOperationException("Challenge already consumed.");

        if (entity.ExpiresAt <= now)
            throw new InvalidOperationException("Challenge expired.");

        if (entity.ResendAfter > now)
            throw new InvalidOperationException("Resend cooldown has not elapsed.");

        // Generate and update to a new code
        var newCode = GenerateNumericCode(opts.CodeLength);
        await _store.UpdateCodeAsync(entity.ChallengeId, HashCode(newCode, opts.HashSalt), now.Add(opts.ResendCooldown), ct);

        await _sender.SendAsync(entity.Channel, entity.Destination, newCode, entity.Purpose, ct);

        return new ResendOtpChallengeResponse(
            ChallengeId: entity.ChallengeId,
            ExpiresAt: entity.ExpiresAt,
            MaskedDestination: MaskDestination(entity.Channel, entity.Destination)
        );
    }

    private static void ValidateCreate(CreateOtpChallengeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.To))
            throw new ArgumentException("To is required.", nameof(req));

        // You can tighten these later (email/phone validation)
    }

    private static string GenerateNumericCode(int length)
    {
        // crypto-safe numeric code
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)('0' + (bytes[i] % 10));

        return new string(chars);
    }

    private static string HashCode(string code, string salt)
    {
        // simple hash; you can swap to HMACSHA256 easily later
        using var sha = SHA256.Create();
        var input = Encoding.UTF8.GetBytes($"{salt}:{code}");
        var hash = sha.ComputeHash(input);
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string CreateVerificationToken(OtpChallengeEntity entity, TimeSpan ttl)
    {
        // opaque token: base64 guid + purpose + timestamp
        // NOTE: keep this opaque; store server-side mapping
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    private static string MaskDestination(OtpChannel channel, string destination)
    {
        destination = destination.Trim();

        if (channel == OtpChannel.Email)
        {
            var at = destination.IndexOf('@');
            if (at <= 1) return "***";
            var name = destination[..at];
            var domain = destination[(at + 1)..];
            return $"{name[0]}***@{domain}";
        }

        // Sms: mask all but last 2-4 digits
        var digits = new string(destination.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return "***";
        return $"***{digits[^4..]}";
    }
}
