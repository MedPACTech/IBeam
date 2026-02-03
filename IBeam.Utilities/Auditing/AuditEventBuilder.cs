using IBeam.DataModels.System;

namespace IBeam.Utilities.Auditing
{
    public static class AuditEventBuilder
    {
        public static AuditEvent Build<TDTO>(AuditAction action, TDTO dto, object? data = null)
            where TDTO : IEntity
        {
            var entityName = typeof(TDTO).Name;
            if (entityName.EndsWith("DTO", StringComparison.OrdinalIgnoreCase))
                entityName = entityName[..^3];

            return new AuditEvent
            {
                Action = action,
                EntityName = entityName,
                EntityId = dto.Id,
                Data = data ?? dto
            };
        }
    }
}
