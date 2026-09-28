namespace IBeam.Identity.Options;

/// <summary>What happens when an invite asks to link somewhere that is not allowed.</summary>
public enum TenantInviteDisallowedLinkBehavior
{
    /// <summary>Build the link on <see cref="TenantInviteLinkOptions.DefaultAcceptUrl"/> and log a warning. The invite is still sent.</summary>
    Fallback = 0,

    /// <summary>Refuse the invite outright, so the caller learns their redirect URL was rejected instead of a recipient getting a different link than the caller intended.</summary>
    Reject = 1
}

/// <summary>
/// Where an invite's "Accept invitation" link is allowed to point (IBM-0068).
/// <para>
/// Before this existed, <c>DefaultTenantInviteUrlBuilder</c> built the link from whatever
/// <c>RedirectUrl</c> the caller supplied, with no validation and a hardcoded
/// <c>https://localhost:3000</c> fallback. Any caller who could create an invite could therefore send a
/// genuine, app-branded invitation whose button opened a site of their choosing — the app's own invite
/// email as a phishing channel.
/// </para>
/// <para>
/// Bound from <c>IBeam:Identity:Invites:Links</c>, and further configurable in code through
/// <c>AddIBeamTenantInviteLinks</c>. Code runs after configuration, so an app can build
/// <see cref="AllowedOrigins"/> from its own sources — its CORS origins, say — rather than repeating them
/// in a config file.
/// </para>
/// </summary>
public sealed class TenantInviteLinkOptions
{
    public const string SectionName = "IBeam:Identity:Invites:Links";

    /// <summary>
    /// Exact origins an invite link may be built on, as <c>scheme://host[:port]</c>. Compared
    /// case-insensitively; no wildcards, and no suffix matching.
    /// <para>
    /// Deliberately empty by default. An allowlist that defaults to something permissive is not an
    /// allowlist, and IBeam cannot know an app's front-end origins. Empty means every requested redirect
    /// falls back to <see cref="DefaultAcceptUrl"/>.
    /// </para>
    /// <para>
    /// Do not populate this from a CORS wildcard. An app that trusts <c>*.example-hosting.net</c> for CORS
    /// is trusting every tenant of that host, and anyone can publish a site there.
    /// </para>
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// The absolute URL an invite links to when the requested redirect is missing, malformed, or not
    /// allowed. Replaces the old hardcoded <c>https://localhost:3000/invites/accept</c>.
    /// <para>
    /// Required for any app that sends invites. Validated when an invite is first delivered rather than at
    /// startup, so an app that never sends one is not forced to configure it.
    /// </para>
    /// </summary>
    public string DefaultAcceptUrl { get; set; } = string.Empty;

    /// <summary>
    /// When set, only the <em>origin</em> of an allowed redirect is kept and this path is appended; the
    /// caller's own path and query are discarded.
    /// <para>
    /// Worth setting. Without it an allowed origin still lets a caller choose any path on that origin,
    /// which matters for a host that serves user-controlled content under some prefix.
    /// </para>
    /// </summary>
    public string? AcceptPath { get; set; }

    /// <summary>What to do with a disallowed redirect. Defaults to <see cref="TenantInviteDisallowedLinkBehavior.Fallback"/>.</summary>
    public TenantInviteDisallowedLinkBehavior OnDisallowed { get; set; } = TenantInviteDisallowedLinkBehavior.Fallback;

    /// <summary>
    /// An extra allow decision for rules a list cannot express — per-tenant custom domains, or preview
    /// environments whose hostnames are not known in advance.
    /// <para>
    /// Consulted only after <see cref="AllowedOrigins"/> has already said no, and only for a URL that has
    /// passed the structural checks (absolute http(s), no user info). It can widen the allowlist; it cannot
    /// smuggle a malformed URL past validation. Code-only — configuration cannot express a delegate.
    /// </para>
    /// </summary>
    public Func<Uri, TenantInviteLinkContext, bool>? IsAllowed { get; set; }

    /// <summary>
    /// Whether <see cref="AllowAnyOrigin"/> has been called. Not settable from configuration on purpose:
    /// turning the protection off is a deliberate code change, not a config flag someone can flip while
    /// looking at something else.
    /// </summary>
    public bool AnyOriginAllowed { get; private set; }

    /// <summary>
    /// Restores the pre-IBM-0068 behaviour: any absolute http(s) redirect is accepted. Logs a warning
    /// whenever it is used.
    /// <para>
    /// Here so that an app with an unusual deployment is not forced to fork the policy, but it does mean
    /// anyone who can create an invite can choose where its link points. Prefer <see cref="IsAllowed"/>.
    /// </para>
    /// </summary>
    public void AllowAnyOrigin() => AnyOriginAllowed = true;
}

/// <summary>
/// What the <see cref="TenantInviteLinkOptions.IsAllowed"/> delegate is told about the invite asking for a
/// link. A projection rather than the record itself, so the delegate cannot be broken by a change to
/// <c>TenantInviteRecord</c>, and so it is not handed the token hash.
/// </summary>
/// <param name="InviteId">The invite being delivered.</param>
/// <param name="TenantId">The tenant it belongs to — the hook for per-tenant custom domains.</param>
/// <param name="DestinationType">Email or SMS.</param>
/// <param name="RequestedRedirectUrl">The raw redirect the caller asked for, before normalization.</param>
public readonly record struct TenantInviteLinkContext(
    Guid InviteId,
    Guid TenantId,
    string DestinationType,
    string? RequestedRedirectUrl);
