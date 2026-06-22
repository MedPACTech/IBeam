namespace IBeam.Identity.Interfaces;

public interface IApiCredentialSecretHasher
{
    string Hash(string secret);
    bool Verify(string secret, string storedHash);
}
