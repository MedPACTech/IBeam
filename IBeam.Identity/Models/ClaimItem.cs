namespace IBeam.Identity.Models;

public sealed record ClaimItem(string Type, string Value, string? ValueType = null);
