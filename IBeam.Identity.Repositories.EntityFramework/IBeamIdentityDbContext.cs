using IBeam.Identity.Repositories.EntityFramework.Types;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.Data;

public class IBeamIdentityDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IBeamIdentityDbContext(DbContextOptions<IBeamIdentityDbContext> options)
        : base(options) { }
}
