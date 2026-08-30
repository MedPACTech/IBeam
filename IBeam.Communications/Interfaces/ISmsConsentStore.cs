namespace IBeam.Communications.Abstractions;

/// <summary>
/// Host-app persistence hook for SMS consent state, mirroring the extension-store
/// pattern used elsewhere in IBeam: the consent data model belongs to the app,
/// IBeam only tells it when an opt-out or opt-in happened. Consent governs
/// non-transactional SMS only; transactional/security messages (login OTP codes)
/// are exempt and must never be gated on this state.
/// </summary>
public interface ISmsConsentStore
{
    Task RecordOptOutAsync(string phoneNumber, CancellationToken ct = default);

    Task RecordOptInAsync(string phoneNumber, string source, CancellationToken ct = default);
}
