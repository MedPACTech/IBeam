using System.Net;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Credentials;

public sealed class ManagementCredentialNetworkOptions
{
    public const string SectionName = "IBeam:Management:Credentials:Network";
    public List<string> AllowedNetworks { get; set; } = [];
}

public sealed class ManagementCredentialNetworkPolicy(IOptions<ManagementCredentialNetworkOptions> options)
{
    public bool Allows(IPAddress? remoteAddress)
    {
        var configured = options.Value.AllowedNetworks;
        if (configured.Count == 0) return true;
        if (remoteAddress is null) return false;
        var address = remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress;
        return configured.Any(network => IPNetwork.Parse(network).Contains(address));
    }
}
