
namespace IBeam.Identity.Models;

public sealed record CreateUserResult(
    bool Succeeded,
    IdentityUser? User,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static CreateUserResult Success(IdentityUser user) =>
        new(true, user, new Dictionary<string, string[]>());

    public static CreateUserResult Failure(IReadOnlyDictionary<string, string[]> errors) =>
        new(false, null, errors);
}


