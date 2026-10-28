namespace IBeam.Management.Operations;

/// <summary>Completion metadata only. No URLs, headers, bodies, profiles, credentials or provider messages.</summary>
public sealed record ManagementTargetCallTelemetry(DateTimeOffset OccurredUtc, long DurationMilliseconds, string Channel,
    string Kind, string Operation, Guid? ApplicationId, Guid? TenantId, Guid? ResourceId, Guid? OperationId,
    string CorrelationId, string Outcome, string FailureClass, int? HttpStatus, bool MutatingRequestSent);
