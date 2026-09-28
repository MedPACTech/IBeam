using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Invites;

/// <summary>
/// The built-in invite link policy (IBM-0068): an exact-origin allowlist, an optional fixed accept path, a
/// configurable fallback, and a code delegate for what a list cannot express.
/// </summary>
public sealed class DefaultTenantInviteLinkPolicy : ITenantInviteLinkPolicy
{
    private readonly TenantInviteLinkOptions _options;
    private readonly HashSet<string> _allowedOrigins;
    private readonly ILogger<DefaultTenantInviteLinkPolicy> _logger;

    public DefaultTenantInviteLinkPolicy(
        IOptions<TenantInviteLinkOptions> options,
        ILogger<DefaultTenantInviteLinkPolicy> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Normalized once. An entry that is not a usable origin is dropped rather than silently matching
        // nothing later, and is reported — a typo in an allowlist otherwise looks exactly like a rejection.
        _allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in _options.AllowedOrigins)
        {
            var origin = NormalizeOrigin(configured);
            if (origin is null)
                _logger.LogWarning(
                    "Ignoring invite link allowed origin {ConfiguredOrigin}: not an absolute http(s) origin.",
                    configured);
            else
                _allowedOrigins.Add(origin);
        }
    }

    public string ResolveAcceptUrl(TenantInviteRecord invite)
    {
        ArgumentNullException.ThrowIfNull(invite);

        var requested = invite.RedirectUrl;
        var uri = ParseAllowableUri(requested);

        if (uri is not null && IsAllowed(uri, invite))
        {
            // Keep only the origin when an accept path is configured; the caller's path and query go.
            // An allowed origin is not a licence to choose any path on it.
            return string.IsNullOrWhiteSpace(_options.AcceptPath)
                ? uri.GetLeftPart(UriPartial.Query).TrimEnd('?')
                : CombineOriginAndPath(uri, _options.AcceptPath!);
        }

        // Nothing usable, or not allowed. Say which, because "rejected" and "you sent nonsense" have
        // different fixes and the operator can only see the config file.
        var describedOrigin = uri is null
            ? string.IsNullOrWhiteSpace(requested) ? "(none supplied)" : "(not an absolute http(s) URL without user info)"
            : OriginOf(uri);

        if (!string.IsNullOrWhiteSpace(requested) && _options.OnDisallowed == TenantInviteDisallowedLinkBehavior.Reject)
        {
            throw new IdentityValidationException(
                $"Invite redirect URL '{describedOrigin}' is not an allowed invite link destination.");
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            _logger.LogWarning(
                "Invite {InviteId} in tenant {TenantId} requested link origin {RequestedOrigin}, which is not allowed; using the configured default accept URL instead.",
                invite.InviteId,
                invite.TenantId,
                describedOrigin);
        }

        return RequireDefaultAcceptUrl();
    }

    /// <summary>
    /// Structural validation, applied before any allow decision. Everything rejected here is rejected for
    /// every app, whatever it has configured — including one that called <c>AllowAnyOrigin</c>.
    /// </summary>
    private static Uri? ParseAllowableUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            // "https://good.example@evil.example" has host evil.example but reads as good.example to a
            // person skimming an email. Rejected outright rather than compared.
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri;
    }

    private bool IsAllowed(Uri uri, TenantInviteRecord invite)
    {
        if (_options.AnyOriginAllowed)
        {
            _logger.LogWarning(
                "Invite {InviteId} linked to {Origin} because AllowAnyOrigin() is enabled — anyone who can create an invite can choose where its link points.",
                invite.InviteId,
                OriginOf(uri));
            return true;
        }

        // Exact origin match. Scheme, host and port all have to agree, and "good.example.evil.example"
        // does not match "good.example" because this compares whole origins rather than suffixes.
        if (_allowedOrigins.Contains(OriginOf(uri)))
            return true;

        return _options.IsAllowed?.Invoke(
            uri,
            new TenantInviteLinkContext(invite.InviteId, invite.TenantId, invite.DestinationType, invite.RedirectUrl)) == true;
    }

    private string RequireDefaultAcceptUrl()
    {
        var fallback = ParseAllowableUri(_options.DefaultAcceptUrl)
            ?? throw new InvalidOperationException(
                $"Sending a tenant invite needs a link destination. Configure '{TenantInviteLinkOptions.SectionName}:DefaultAcceptUrl' "
                + "with the absolute URL of your invite accept page, and list your front-end origins under "
                + $"'{TenantInviteLinkOptions.SectionName}:AllowedOrigins' (or add them in code with AddIBeamTenantInviteLinks). "
                + "Earlier versions fell back to https://localhost:3000/invites/accept, which was never a working "
                + "destination outside a developer machine.");

        return string.IsNullOrWhiteSpace(_options.AcceptPath)
            ? fallback.GetLeftPart(UriPartial.Query).TrimEnd('?')
            : CombineOriginAndPath(fallback, _options.AcceptPath!);
    }

    private static string CombineOriginAndPath(Uri origin, string path) =>
        $"{OriginOf(origin)}/{path.TrimStart('/')}";

    /// <summary>"scheme://host[:port]", lowercased. <see cref="Uri.Authority"/> omits a default port, so http on 80 and https on 443 compare as an operator would write them.</summary>
    private static string OriginOf(Uri uri) => $"{uri.Scheme}://{uri.Authority}".ToLowerInvariant();

    private static string? NormalizeOrigin(string? url)
    {
        var uri = ParseAllowableUri(url);
        return uri is null ? null : OriginOf(uri);
    }
}
