using System.Security.Cryptography;
using System.Text;
using IBeam.Licensing;
using Microsoft.Extensions.Options;

namespace IBeam.Billing.Licensing;

public sealed class BillingPurchaseClaimService : IBillingPurchaseClaimService
{
    private readonly IBillingPurchaseService _purchases;
    private readonly IBillingPurchaseClaimStore _claims;
    private readonly ILicenseSeatPolicyService _seatPolicies;
    private readonly BillingPurchaseClaimOptions _options;
    private readonly TimeProvider _timeProvider;

    public BillingPurchaseClaimService(
        IBillingPurchaseService purchases,
        IBillingPurchaseClaimStore claims,
        ILicenseSeatPolicyService seatPolicies,
        IOptions<BillingPurchaseClaimOptions>? options = null,
        TimeProvider? timeProvider = null)
    {
        _purchases = purchases;
        _claims = claims;
        _seatPolicies = seatPolicies;
        _options = options?.Value ?? new BillingPurchaseClaimOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IssuedBillingPurchaseClaimInfo> IssueAsync(Guid purchaseId, CancellationToken ct = default)
    {
        if (purchaseId == Guid.Empty)
            throw new BillingException("purchaseId is required.");
        var purchase = await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Purchase was not found.");
        if (!string.Equals(purchase.Status, BillingPurchaseStatuses.Fulfilled, StringComparison.OrdinalIgnoreCase) ||
            purchase.LicenseKey is null ||
            string.IsNullOrWhiteSpace(purchase.BuyerEmail))
        {
            throw new BillingException("Only a fulfilled purchase with a buyer can issue a claim.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = _timeProvider.GetUtcNow();
        var record = new BillingPurchaseClaimRecord(
            Guid.NewGuid(),
            purchase.PurchaseId,
            purchase.LicenseKey.Value,
            Hash(token),
            Hash(BillingPurchaseInfo.NormalizeEmail(purchase.BuyerEmail)!),
            now,
            now.AddMinutes(_options.TokenLifetimeMinutes),
            null,
            null,
            null);
        var saved = await _claims.SaveIssuedAsync(record, ct).ConfigureAwait(false);
        return new IssuedBillingPurchaseClaimInfo(saved.ClaimId, saved.PurchaseId, token, saved.ExpiresUtc);
    }

    public async Task<ClaimedBillingPurchaseLicenseInfo> ClaimAsync(
        ClaimBillingPurchaseLicenseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty)
            throw new BillingException("tenantId is required.");
        if (request.UserId == Guid.Empty)
            throw new BillingException("userId is required.");
        var token = Required(request.ClaimToken, "claimToken");
        var verifiedEmail = BillingPurchaseInfo.NormalizeEmail(Required(request.VerifiedEmail, "verifiedEmail"))!;
        var tokenHash = Hash(token);
        var claim = await _claims.GetByTokenHashAsync(tokenHash, ct).ConfigureAwait(false)
                    ?? throw new BillingException("Claim token is invalid.");
        var now = _timeProvider.GetUtcNow();
        if (claim.ExpiresUtc <= now && claim.ClaimedUtc is null)
            throw new BillingException("Claim token has expired.");
        if (!string.Equals(claim.BuyerEmailHash, Hash(verifiedEmail), StringComparison.Ordinal))
            throw new BillingException("Claim token does not belong to the verified buyer.");

        var purchase = await _purchases.GetPurchaseAsync(claim.PurchaseId, ct).ConfigureAwait(false)
                       ?? throw new BillingException("Claim purchase was not found.");
        if (purchase.LicenseKey != claim.LicenseKey ||
            purchase.Status is not (BillingPurchaseStatuses.Fulfilled or BillingPurchaseStatuses.Claimed))
        {
            throw new BillingException("Claim purchase is not fulfilled by the expected license.");
        }

        var wasAlreadyClaimed = claim.ClaimedUtc is not null;
        var claimed = await _claims.TryClaimAsync(tokenHash, request.TenantId, request.UserId, now, ct).ConfigureAwait(false)
                      ?? throw new BillingException("Claim token is invalid.");
        if (claimed.ClaimedTenantId != request.TenantId || claimed.ClaimedUserId != request.UserId)
            throw new BillingException("Claim token has already been used by another tenant or buyer.");

        var grant = await _seatPolicies.GrantTenantSeatLicenseAsync(
            request.TenantId,
            new GrantTenantSeatLicenseRequest
            {
                License = new GrantTenantLicenseRequest
                {
                    LicenseKey = claim.LicenseKey,
                    PlanKey = purchase.PlanKey,
                    SeatLimit = purchase.TotalSeats,
                    Status = LicenseStatuses.Active,
                    CommercialStatus = LicenseCommercialStatuses.Paid,
                    StartsUtc = purchase.PaidUtc ?? purchase.FulfilledUtc ?? now,
                    ProviderName = purchase.ProviderName,
                    ProviderCustomerId = purchase.ProviderCustomerId,
                    ProviderSubscriptionId = purchase.ProviderSubscriptionId,
                    ProviderStatus = purchase.Status,
                    Metadata = new Dictionary<string, string>
                    {
                        ["billingPurchaseId"] = purchase.PurchaseId.ToString("D"),
                        ["billingClaimId"] = claim.ClaimId.ToString("D")
                    }
                },
                SeatLimit = purchase.TotalSeats,
                InitialSubjects =
                [
                    new LicenseSubject(
                        LicenseSubjectTypes.User,
                        request.UserId.ToString("D"),
                        verifiedEmail)
                ]
            },
            request.UserId,
            ct).ConfigureAwait(false);
        await _purchases.MarkClaimedAsync(purchase.PurchaseId, request.TenantId, request.UserId, ct).ConfigureAwait(false);

        return new ClaimedBillingPurchaseLicenseInfo(
            claim.ClaimId,
            purchase.PurchaseId,
            request.TenantId,
            request.UserId,
            grant.License,
            grant.Assignments,
            claimed.ClaimedUtc!.Value,
            wasAlreadyClaimed);
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new BillingException($"{name} is required.") : value.Trim();

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
