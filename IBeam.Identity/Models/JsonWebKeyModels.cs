namespace IBeam.Identity.Models;

public sealed record JsonWebKeySetDto(IReadOnlyList<JsonWebKeyDto> Keys);

public sealed record JsonWebKeyDto(
    string Kty,
    string Use,
    string Alg,
    string Kid,
    string N,
    string E);
