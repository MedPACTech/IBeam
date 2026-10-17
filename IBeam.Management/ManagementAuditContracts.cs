using IBeam.Management.Entities;
namespace IBeam.Management;
/// <summary>Append-only infrastructure port. No portal read/update/delete capability.</summary>
public interface IManagementAuditRepository
{
    Task AppendAsync(ManagementAuditEntity entry, CancellationToken ct = default);
}
public interface IManagementAuditContext
{
    string CorrelationId { get; }
    /// <summary>Call only for an authenticated machine request, never for a human/browser request.</summary>
    void AcceptMachineCorrelation(string value);
}
public sealed class ManagementAuditUnavailableException() : Exception("Management audit storage is unavailable.");
