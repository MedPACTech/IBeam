using System.Security.Cryptography;
using System.Text;

namespace IBeam.Identity.Repositories.AzureTable.Options;

internal static class DeterministicGuid
{
    public static Guid Create(string scope, string value)
    {
        var input = $"{scope.Trim()}:{value.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bytes = hash[..16];

        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
