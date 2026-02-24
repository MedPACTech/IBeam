using IBeam.Identity.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Abstractions.Interfaces;

public interface IIdentityCommunicationSender
{
    Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default);
}

public class IdentitySenderMessage
{
    public SenderChannel Channel { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public SenderPurpose? Purpose { get; set; }
    public Guid? TenantId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    // Add more properties as needed for templating, etc.
}
