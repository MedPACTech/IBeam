using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Schema;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Repositories.AzureTable.Schema;

internal sealed class AzureTableIdentitySchemaManager : IIdentitySchemaManager
{
    private const int TargetVersion = 1;

    private readonly TableServiceClient _serviceClient;
    private readonly IdentityConfiguration _identityConfig;
    private readonly AzureTableIdentityOptions _opts;
    private readonly ILogger<AzureTableIdentitySchemaManager> _logger;

    public AzureTableIdentitySchemaManager(
        TableServiceClient serviceClient,
        IdentityConfiguration identityConfig,
        IOptions<AzureTableIdentityOptions> opts,
        ILogger<AzureTableIdentitySchemaManager> logger)
    {
        _serviceClient = serviceClient;
        _identityConfig = identityConfig;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        // 1) ElCamino Identity tables (explicit, no builder type needed)
        // IdentityConfiguration already contains the table names used by ElCamino.
        await _serviceClient.CreateTableIfNotExistsAsync($"{_identityConfig.TablePrefix}{_identityConfig.UserTableName}", ct)
            .ConfigureAwait(false);

        await _serviceClient.CreateTableIfNotExistsAsync($"{_identityConfig.TablePrefix}{_identityConfig.RoleTableName}", ct)
            .ConfigureAwait(false);

        await _serviceClient.CreateTableIfNotExistsAsync($"{_identityConfig.TablePrefix}{_identityConfig.IndexTableName}", ct)
            .ConfigureAwait(false);

        // 2) Provider custom tables
        foreach (var name in RequiredCustomTables())
            await _serviceClient.CreateTableIfNotExistsAsync(name, ct).ConfigureAwait(false);

        // 3) Schema version
        await WriteSchemaVersionAsync(1, ct).ConfigureAwait(false);

        _logger.LogInformation("AzureTable identity schema ensured.");
    }


    public async Task<IdentitySchemaStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var pending = new List<IdentitySchemaStep>();

        // Read current version (0 if schema row/table missing)
        var currentVersion = await ReadSchemaVersionAsync(ct).ConfigureAwait(false);

        // Snapshot existing tables once
        var existing = await ListTableNamesAsync(ct).ConfigureAwait(false);

        // Custom tables missing?
        foreach (var name in RequiredCustomTables())
        {
            if (!existing.Contains(name))
            {
                pending.Add(new IdentitySchemaStep(
                    Version: 1,
                    Description: $"Create table '{name}'"));
            }
        }

        // Schema table missing?
        var schemaTableName = SchemaTableName();
        if (!existing.Contains(schemaTableName))
        {
            pending.Add(new IdentitySchemaStep(
                Version: 1,
                Description: $"Create table '{schemaTableName}'"));
        }

        // If schema table exists but version row is missing, ReadSchemaVersionAsync returns 0.
        if (existing.Contains(schemaTableName) && currentVersion == 0)
        {
            pending.Add(new IdentitySchemaStep(
                Version: 1,
                Description: $"Write schema version row in '{schemaTableName}'"));
        }

        // You *can* optionally check ElCamino tables too, but EnsureCreatedAsync already guarantees them.
        var isUpToDate = currentVersion >= TargetVersion && pending.Count == 0;

        return new IdentitySchemaStatus(
            CurrentVersion: currentVersion,
            TargetVersion: TargetVersion,
            IsUpToDate: isUpToDate,
            PendingSteps: pending
        );
    }

    // -------- internal helpers --------

    private IEnumerable<string> RequiredCustomTables()
    {
        yield return $"{_opts.TablePrefix}{_opts.TenantsTableName}";
        yield return $"{_opts.TablePrefix}{_opts.TenantUsersTableName}";
        yield return $"{_opts.TablePrefix}{_opts.UserTenantsTableName}";
        yield return $"{_opts.TablePrefix}{_opts.TenantRolesTableName}";
        yield return $"{_opts.TablePrefix}{_opts.OtpChallengesTableName}";
        yield return $"{_opts.TablePrefix}{_opts.ExternalLoginsTableName}";
        yield return $"{_opts.TablePrefix}{_opts.AuthSessionsTableName}";
        yield return $"{_opts.TablePrefix}{_opts.PermissionRoleMapsTableName}";
    }

    private string SchemaTableName()
        => $"{_opts.TablePrefix}Schema";

    private async Task<HashSet<string>> ListTableNamesAsync(CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var t in _serviceClient.QueryAsync(cancellationToken: ct).ConfigureAwait(false))
            set.Add(t.Name);
        return set;
    }

    private async Task<int> ReadSchemaVersionAsync(CancellationToken ct)
    {
        var tableName = SchemaTableName();

        try
        {
            var table = _serviceClient.GetTableClient(tableName);
            var resp = await table.GetEntityAsync<TableEntity>("Schema", "IBeam.Identity", cancellationToken: ct)
                .ConfigureAwait(false);

            if (resp.Value.TryGetValue("Version", out var v))
            {
                // SDK often returns int/long depending on serialization
                if (v is int i) return i;
                if (v is long l) return checked((int)l);
                if (v is string s && int.TryParse(s, out var parsed)) return parsed;
            }

            return 0;
        }
        catch
        {
            return 0; // schema table or version row missing
        }
    }

    private async Task WriteSchemaVersionAsync(int version, CancellationToken ct)
    {
        var tableName = SchemaTableName();
        await _serviceClient.CreateTableIfNotExistsAsync(tableName, ct).ConfigureAwait(false);

        var table = _serviceClient.GetTableClient(tableName);

        var entity = new TableEntity("Schema", "IBeam.Identity")
        {
            ["Version"] = version,
            ["UpdatedAt"] = DateTimeOffset.UtcNow
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }
}
