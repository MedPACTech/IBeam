using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.DataModels
{
    [Serializable]
    [Alias("SystemAudit")]
    public class SystemAuditDTO : IDTO
    {
        public Guid Id { get; set; }
        public DateTime DateChanged { get; set; }
        public string ChangeType { get; set; }
        public string EntityName { get; set; }
        public string Data { get; set; }
        public Guid EntityID { get; set; }
        public bool IsDeleted { get; set; }
    }
}