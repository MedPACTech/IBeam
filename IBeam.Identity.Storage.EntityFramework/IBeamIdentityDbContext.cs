using IBeam.Identity.Storage.EntityFramework.Types;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Storage.EntityFramework.Data;

public class IBeamIdentityDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IBeamIdentityDbContext(DbContextOptions<IBeamIdentityDbContext> options)
        : base(options) { }
}
