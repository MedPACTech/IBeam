using System;

namespace IBeam.Scaffolding.Models
{
    public interface IServiceAuthorization
    {
        string ActionName { get; set; }
        Guid ApplicationRoleId { get; set; }
        Guid Id { get; set; }
        string RoleName { get; set; }
        string ServiceName { get; set; }
    }
}