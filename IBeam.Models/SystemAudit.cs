using System;

namespace IBeam.Models
{
    public class SystemAudit : ISystemAudit
    {
        public Guid Id { get; set; }
        public DateTime DateChanged { get; set; }
        public string ChangeType { get; set; }
        public string EntityName { get; set; }
        public string Data { get; set; }
        public Guid EntityID { get; set; }
    }
}
