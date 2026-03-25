using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed record RequireRoleIdsRequirement(IReadOnlyList<Guid> RoleIds) : IAuthorizationRequirement;
