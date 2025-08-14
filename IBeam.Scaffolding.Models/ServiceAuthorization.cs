using System;

namespace IBeam.Scaffolding.Models
{
    public class ServiceAuthorization : IServiceAuthorization
    {
        public Guid Id { get; set; }
        public Guid ApplicationRoleId { get; set; }
        public string RoleName { get; set; }
        public string ServiceName { get; set; }
        public string ActionName { get; set; }
    }
}