using System;

namespace IBeam.Scaffolding.Models
{
    public interface ISystemAudit
    {
        Guid Id { get; }
        DateTime DateChanged { get; }
        string ChangeType { get; }
        string EntityName { get; }
        string Data { get;  }
        Guid EntityID { get; }
    }
}