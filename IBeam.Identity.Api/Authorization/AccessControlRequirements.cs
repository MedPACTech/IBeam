using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed record RequirePermissionRequirement(string PermissionName) : IAuthorizationRequirement;

public sealed record RequireModuleRequirement(
    string ModuleKey,
    string AccessLevel = AccessLevels.View) : IAuthorizationRequirement;

public sealed record RequireResourceRequirement(
    string ResourceType,
    string ResourceId,
    string AccessLevel = AccessLevels.View) : IAuthorizationRequirement;

