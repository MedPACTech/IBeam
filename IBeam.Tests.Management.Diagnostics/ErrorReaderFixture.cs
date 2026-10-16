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

internal sealed class ErrorReaderFixture
{
    public static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    public readonly Clock Time = new();
    public readonly Access Authorization = new();
    public readonly MemoryRepository Repository = new();
    public SystemErrorReaderOptions Options { get; } = new()
    {
        SourceMode = DiagnosticSourceMode.DedicatedDeployment, DedicatedSourceConfirmed = true,
        ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };
    public ManagementDiagnosticsOptions Deployment { get; } = new() { ApplicationKey = "qurvia", Environment = "Production" };
    public IManagementSystemErrorReader Reader(ISystemErrorReadRepository? repository = null,
        ManagementDiagnosticsOptions? deployment = null, IServiceOperationExecutor? operations = null, IHostEnvironment? host = null)
        => new ManagementSystemErrorReader(repository ?? Repository, Authorization, new DiagnosticTokenCodec(Microsoft.Extensions.Options.Options.Create(Options)),
            Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(deployment ?? Deployment), Time,
            operations ?? new ServiceOperationExecutor(), host);
    public static SystemErrorRecord Row(DateTimeOffset at, string? application = null, string? environment = null)
        => new()
        {
            PartitionKey = "ERR|" + at.UtcDateTime.ToString("yyyyMMdd"),
            RowKey = at.ToString("HHmmssfff") + "|" + Guid.NewGuid().ToString("N"),
            OccurredAtUtc = at, Source = "untrusted source", TraceId = "untrusted correlation",
            ManagementApplicationKey = application, ManagementEnvironment = environment
        };
    public sealed class Clock : TimeProvider
    {
        public DateTimeOffset NowUtc { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => NowUtc;
    }
    public sealed class Access : IManagementDiagnosticsAccessService
    {
        public int Calls { get; private set; }
        public bool Deny { get; set; }
        public Task<ManagementCredentialMetadata> RequireAsync(string operation, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            if (Deny || operation != ManagementOperations.ErrorsRead) throw new ManagementAccessDeniedException();
            return Task.FromResult(new ManagementCredentialMetadata(Guid.NewGuid(), "qurvia", "Production", [operation], "Active", Now.AddDays(1)));
        }
    }
    public sealed class MemoryRepository : ISystemErrorReadRepository
    {
        public string SourceBinding { get; set; } = "fixture-source";
        public List<SystemErrorRecord> Rows { get; } = [];
        public List<SystemErrorPartitionQuery> Queries { get; } = [];
        public int Gets { get; private set; }
        public bool EmptyProviderPage { get; set; }
        public bool DelayUntilCancellation { get; set; }
        public Action? OnRead { get; set; }
        public async Task<SystemErrorPartitionPage> ReadPartitionAsync(SystemErrorPartitionQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (DelayUntilCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            Queries.Add(query);
            OnRead?.Invoke();
            if (EmptyProviderPage && query.ContinuationToken is null) return new SystemErrorPartitionPage([], 0, "after-empty");
            var skip = query.ContinuationToken is null or "after-empty" ? 0 : int.Parse(query.ContinuationToken);
            var matching = Rows.Where(x => x.PartitionKey == "ERR|" + query.DayUtc.ToString("yyyyMMdd") &&
                x.OccurredAtUtc >= query.FromUtc && x.OccurredAtUtc < query.ToUtc).OrderBy(x => x.RowKey, StringComparer.Ordinal).ToArray();
            var rows = matching.Skip(skip).Take(query.Take).ToArray();
            return new SystemErrorPartitionPage(rows, rows.Length, skip + rows.Length < matching.Length ? (skip + rows.Length).ToString() : null);
        }
        public Task<SystemErrorRecord?> GetUnsanitizedAsync(string partitionKey, string rowKey, CancellationToken ct = default) => GetAsync(partitionKey, rowKey, ct);
        public Task<SystemErrorRecord?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Gets++;
            return Task.FromResult(Rows.SingleOrDefault(x => x.PartitionKey == partitionKey && x.RowKey == rowKey));
        }
    }
}
