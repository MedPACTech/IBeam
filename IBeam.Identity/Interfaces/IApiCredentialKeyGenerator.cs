using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialKeyGenerator
{
    (string RawKey, ParsedApiCredentialKey ParsedKey, string KeyPrefix) CreateKey(Guid tenantId, Guid credentialId);
    bool TryParse(string rawKey, out ParsedApiCredentialKey parsedKey);
}
