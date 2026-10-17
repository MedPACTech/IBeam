using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Diagnostics;

internal sealed class LogReaderFixture
{
    public static readonly DateTimeOffset Now = ErrorReaderFixture.Now;
    public readonly ErrorReaderFixture.Clock Time = new();
    public readonly Access Authorization = new();
    public readonly MemoryRepository Repository = new();
    public SystemLogReaderOptions Options { get; } = new()
    {
        SourceMode = DiagnosticSourceMode.DedicatedDeployment, DedicatedSourceConfirmed = true,
        ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };
    public ManagementDiagnosticsOptions Deployment { get; } = new() { ApplicationKey = "qurvia", Environment = "Production" };
    public IManagementSystemLogReader Reader(ISystemLogReadRepository? repository = null,
        ManagementDiagnosticsOptions? deployment = null, IServiceOperationExecutor? operations = null, IHostEnvironment? host = null)
        => new ManagementSystemLogReader(repository ?? Repository, Authorization,
            new SystemLogTokenCodec(Microsoft.Extensions.Options.Options.Create(Options)),
            Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(deployment ?? Deployment), Time,
            operations ?? new ServiceOperationExecutor(), host);
    public SystemLogProjection Projection(ManagementDiagnosticsOptions? deployment = null)
        => new(Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(deployment ?? Deployment));
    public static SystemLogMetadata Row(DateTimeOffset at, string? application = null, string? environment = null,
        string source = "RequestPipeline", string level = "Error", string trace = "trace-1")
        => new()
        {
            PartitionKey = "SYSTEM|DAY|" + at.UtcDateTime.ToString("yyyyMMdd"),
            RowKey = at.UtcDateTime.ToString("HHmmssfffffff") + "|" + Guid.NewGuid().ToString("N"),
            OccurredAtUtc = at, Source = source, Level = level, TraceId = trace,
            ManagementApplicationKey = application, ManagementEnvironment = environment
        };
    public sealed class Access : IManagementDiagnosticsAccessService
    {
        public int Calls { get; private set; }
        public bool Deny { get; set; }
        public Task<ManagementCredentialMetadata> RequireAsync(string operation, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            if (Deny || operation != ManagementOperations.LogsRead) throw new ManagementAccessDeniedException();
            return Task.FromResult(new ManagementCredentialMetadata(Guid.NewGuid(), "qurvia", "Production", [operation], "Active", Now.AddDays(1)));
        }
    }
    /// <summary>Applies the same exact filters a real provider would, so reader-side defence can be tested separately.</summary>
    public sealed class MemoryRepository : ISystemLogReadRepository
    {
        public string SourceBinding { get; set; } = "fixture-log-source";
        public List<SystemLogMetadata> Rows { get; } = [];
        public List<SystemLogPartitionQuery> Queries { get; } = [];
        public int Gets { get; private set; }
        public bool IgnoreFilters { get; set; }
        public bool DelayUntilCancellation { get; set; }
        public Action? OnRead { get; set; }
        public async Task<SystemLogPartitionPage> ReadPartitionAsync(SystemLogPartitionQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (DelayUntilCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            Queries.Add(query);
            OnRead?.Invoke();
            var skip = query.ContinuationToken is null ? 0 : int.Parse(query.ContinuationToken);
            var matching = Rows.Where(x => x.PartitionKey == "SYSTEM|DAY|" + query.DayUtc.ToString("yyyyMMdd") &&
                x.OccurredAtUtc >= query.FromUtc && x.OccurredAtUtc < query.ToUtc &&
                (IgnoreFilters || (query.Source is null || x.Source == query.Source) &&
                    (query.Level is null || x.Level == query.Level) && (query.TraceId is null || x.TraceId == query.TraceId)))
                .OrderBy(x => x.RowKey, StringComparer.Ordinal).ToArray();
            var rows = matching.Skip(skip).Take(query.Take).ToArray();
            return new SystemLogPartitionPage(rows, rows.Length, skip + rows.Length < matching.Length ? (skip + rows.Length).ToString() : null);
        }
        public Task<SystemLogMetadata?> GetUnsanitizedAsync(string partitionKey, string rowKey, CancellationToken ct = default) => GetAsync(partitionKey, rowKey, ct);
        public Task<SystemLogMetadata?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Gets++;
            return Task.FromResult(Rows.SingleOrDefault(x => x.PartitionKey == partitionKey && x.RowKey == rowKey));
        }
    }
}
