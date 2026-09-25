namespace IBeam.Identity.Models;

public sealed record OAuthDeviceAuthorizationRequest(
    string ClientId,
    IReadOnlyList<string> Scopes,
    string Resource);

public sealed record OAuthDeviceAuthorizationStartResult(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresIn,
    int Interval);

public sealed record OAuthDeviceApprovalContext(
    bool Found,
    bool Usable,
    string? ClientDisplayName,
    IReadOnlyList<string> Scopes,
    string Resource);

public sealed record OAuthDeviceApprovalDecision(
    string UserCode,
    bool Approved);

public sealed record OAuthDeviceApprovalResult(
    bool Success,
    string? Error = null,
    string? ErrorDescription = null);
