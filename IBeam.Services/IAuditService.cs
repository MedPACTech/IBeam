namespace IBeam.Services.Abstractions
{
    public interface IAuditService
    {
        void LogAudit(object auditEvent);
    }
}
