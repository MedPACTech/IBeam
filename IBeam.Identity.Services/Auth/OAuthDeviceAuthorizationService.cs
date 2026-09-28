using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Auth;

public sealed class OAuthDeviceAuthorizationService : IOAuthDeviceAuthorizationService
{
    // RFC 8628 recommends a charset that avoids vowels and visually-ambiguous characters so a
    // user typing the code by hand from one device to another doesn't stumble over 0/O, 1/I, etc.
    private const string UserCodeAlphabet = "BCDFGHJKLMNPQRSTVWXYZ23456789";

    private readonly IOAuthClientStore _clients;
    private readonly IOAuthDeviceAuthorizationStore _devices;
    private readonly IOAuthConsentStore _consents;
    private readonly IOAuthEffectivePermissionResolver _permissions;
    private readonly OAuthAuthorizationServerOptions _options;

    public OAuthDeviceAuthorizationService(
        IOAuthClientStore clients,
        IOAuthDeviceAuthorizationStore devices,
        IOAuthConsentStore consents,
        IOAuthEffectivePermissionResolver permissions,
        IOptions<OAuthAuthorizationServerOptions> options)
    {
        _clients = clients;
        _devices = devices;
        _consents = consents;
        _permissions = permissions;
        _options = options.Value;
        _options.Validate();
    }

    public async Task<OAuthDeviceAuthorizationStartResult> RequestAsync(OAuthDeviceAuthorizationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await _clients.GetAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client is null || !client.IsActive)
            throw Error("invalid_client", "The OAuth client is invalid.");
        if (!client.AllowsGrantType(OAuthGrantTypes.DeviceCode))
            throw Error("unauthorized_client", "The client cannot use this grant type.");
        if (string.IsNullOrWhiteSpace(client.DeviceVerificationUri))
            throw Error("server_error", "The client is not configured for device authorization.");
        if (!client.AllowsResource(request.Resource))
            throw Error("invalid_target", "The requested resource is invalid.");
        if (request.Scopes.Count == 0 || request.Scopes.Any(scope => !client.AllowsScope(scope)))
            throw Error("invalid_scope", "One or more requested scopes are invalid.");

        var now = DateTimeOffset.UtcNow;
        var deviceCode = Base64Url(RandomNumberGenerator.GetBytes(32));
        // Stored in its normalized (no-dash) form - the canonical value NormalizeUserCode()
        // reduces every later lookup to, whatever punctuation/case the user typed it back with.
        // The dash is added only for display, in FormatUserCodeForDisplay below.
        var userCode = GenerateUserCode();
        var record = new OAuthDeviceAuthorizationRecord(
            Hash(deviceCode),
            userCode,
            client.ClientId,
            request.Scopes,
            request.Resource,
            now,
            now.AddMinutes(_options.DeviceCodeLifetimeMinutes),
            _options.DeviceCodePollIntervalSeconds);
        await _devices.CreateAsync(record, ct).ConfigureAwait(false);

        var displayCode = FormatUserCodeForDisplay(userCode);
        var separator = client.DeviceVerificationUri!.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var complete = $"{client.DeviceVerificationUri}{separator}user_code={Uri.EscapeDataString(displayCode)}";
        return new(
            deviceCode,
            displayCode,
            client.DeviceVerificationUri,
            complete,
            checked((int)(record.ExpiresUtc - now).TotalSeconds),
            record.IntervalSeconds);
    }

    public async Task<OAuthDeviceApprovalContext> PrepareApprovalAsync(ClaimsPrincipal subject, string userCode, CancellationToken ct = default)
    {
        var record = await _devices.GetByUserCodeAsync(NormalizeUserCode(userCode), ct).ConfigureAwait(false);
        if (record is null)
            return new(Found: false, Usable: false, null, [], string.Empty);
        if (!record.IsUsable(DateTimeOffset.UtcNow) || !record.IsPending)
            return new(Found: true, Usable: false, null, [], string.Empty);

        var client = await _clients.GetAsync(record.ClientId, ct).ConfigureAwait(false);
        return new(Found: true, Usable: true, client?.DisplayName ?? record.ClientId, record.Scopes, record.Resource);
    }

    public async Task<OAuthDeviceApprovalResult> ApproveAsync(ClaimsPrincipal subject, OAuthDeviceApprovalDecision decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(decision);
        var userId = ResolveGuid(subject, "uid", ClaimTypes.NameIdentifier, "sub");
        var tenantId = ResolveGuid(subject, "tid", "tenant_id");
        if (userId == Guid.Empty || tenantId == Guid.Empty)
            return new(false, "access_denied", "An authenticated tenant user is required.");

        var userCode = NormalizeUserCode(decision.UserCode);
        var record = await _devices.GetByUserCodeAsync(userCode, ct).ConfigureAwait(false);
        if (record is null || !record.IsUsable(DateTimeOffset.UtcNow) || !record.IsPending)
            return new(false, "invalid_grant", "This code is invalid or has expired.");

        if (!decision.Approved)
        {
            await _devices.TryDenyAsync(userCode, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            return new(true);
        }

        var client = await _clients.GetAsync(record.ClientId, ct).ConfigureAwait(false);
        if (client is null || !client.IsActive)
            return new(false, "invalid_client", "The OAuth client is invalid.");
        if (client.TenantId is { } clientTenant && clientTenant != tenantId)
            return new(false, "access_denied", "This sign-in is not available for your workspace.");

        var now = DateTimeOffset.UtcNow;
        var existingConsent = await _consents.GetAsync(userId, tenantId, client.ClientId, record.Resource, ct).ConfigureAwait(false);
        var consent = await _consents.UpsertAsync(new(
            existingConsent?.ConsentId ?? Guid.NewGuid(),
            userId,
            tenantId,
            client.ClientId,
            record.Resource,
            record.Scopes,
            existingConsent?.CreatedUtc ?? now,
            now), ct).ConfigureAwait(false);
        var effective = await _permissions.ResolveAsync(new(
            tenantId, client, consent, subject, record.Scopes, record.Resource), ct).ConfigureAwait(false);
        if (effective.GrantedScopes.Count == 0 || effective.DeniedScopes.Count > 0)
        {
            await _devices.TryDenyAsync(userCode, now, ct).ConfigureAwait(false);
            return new(false, "invalid_scope", "One or more requested permissions are not available.");
        }

        var approved = await _devices.TryApproveAsync(userCode, userId, tenantId, effective.GrantedScopes, now, ct).ConfigureAwait(false);
        return approved is null
            ? new(false, "invalid_grant", "This code is invalid or has expired.")
            : new(true);
    }

    private static OAuthProtocolException Error(string error, string description) => new(error, description);

    private static Guid ResolveGuid(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
            if (Guid.TryParse(principal.FindFirst(type)?.Value, out var value) && value != Guid.Empty)
                return value;
        return Guid.Empty;
    }

    private static string GenerateUserCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        return new string(chars);
    }

    private static string FormatUserCodeForDisplay(string userCode) => $"{userCode[..4]}-{userCode[4..]}";

    // The device only ever sees its own code back through the same channel it typed it into, so
    // this only needs to tolerate how a human types it (case, the dash) - not resist tampering.
    private static string NormalizeUserCode(string userCode) =>
        new string((userCode ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
