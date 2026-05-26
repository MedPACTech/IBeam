using System;

namespace IBeam.Identity.Models;

public class IdentityCommunicationMessage
{
    public SenderChannel Channel { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public SenderPurpose Purpose { get; set; }
    public Guid? TenantId { get; set; }
    public string? TemplateKey { get; set; }
    public object? TemplateModel { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
}
