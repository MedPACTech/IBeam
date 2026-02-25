using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

internal sealed class AzureTableOtpChallengeStore : IOtpChallengeStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableOtpChallengeStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task SaveAsync(OtpChallengeRecord record, CancellationToken ct = default)
    {
        try
        {
            var table = GetOtpTable();
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false); // <-- Ensure table exists
            var entity = ToEntity(record);

            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OtpChallengeRecord?> GetAsync(string challengeId, CancellationToken ct = default)
    {
        try
        {
            var table = GetOtpTable();
            var pk = PartitionFor(challengeId);
            var rk = challengeId;

            var response = await table.GetEntityIfExistsAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct)
                .ConfigureAwait(false);

            return response.HasValue ? FromEntity(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task IncrementAttemptAsync(string challengeId, CancellationToken ct = default)
    {
        try
        {
            var table = GetOtpTable();
            var pk = PartitionFor(challengeId);
            var rk = challengeId;

            for (var i = 0; i < 5; i++)
            {
                var get = await table.GetEntityAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct).ConfigureAwait(false);
                var entity = get.Value;

                entity.AttemptCount += 1;
                entity.LastAttemptAt = DateTimeOffset.UtcNow;

                try
                {
                    await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return;
                }
                catch (RequestFailedException rfe) when (rfe.Status == 412)
                {
                    // ETag mismatch, retry
                }
            }

            throw new IdentityProviderException("Failed to increment OTP attempts due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task MarkConsumedAsync(
        string challengeId,
        string verificationToken,
        DateTimeOffset verificationTokenExpiresAt,
        CancellationToken ct = default)
    {
        try
        {
            var table = GetOtpTable();
            var pk = PartitionFor(challengeId);
            var rk = challengeId;

            for (var i = 0; i < 5; i++)
            {
                var get = await table.GetEntityAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct).ConfigureAwait(false);
                var entity = get.Value;

                entity.IsConsumed = true;
                entity.ConsumedAt = DateTimeOffset.UtcNow;
                entity.VerificationToken = verificationToken;
                entity.VerificationTokenExpiresAt = verificationTokenExpiresAt;

                try
                {
                    await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return;
                }
                catch (RequestFailedException rfe) when (rfe.Status == 412)
                {
                    // retry
                }
            }

            throw new IdentityProviderException("Failed to mark OTP challenge consumed due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient GetOtpTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.OtpChallengesTableName));

    private static string PartitionFor(string challengeId)
    {
        if (string.IsNullOrEmpty(challengeId))
            return "OTP-00";

        var a = challengeId.Length > 0 ? challengeId[0] : '0';
        var b = challengeId.Length > 1 ? challengeId[1] : '0';
        return $"OTP-{a}{b}";
    }

    private static OtpChallengeEntity ToEntity(OtpChallengeRecord r)
        => new()
        {
            PartitionKey = PartitionFor(r.ChallengeId),
            RowKey = r.ChallengeId,

            Destination = r.Destination,
            Purpose = r.Purpose.ToString(),
            TenantId = r.TenantId?.ToString("D"),

            // IMPORTANT: the Abstractions record appears to store hash (not plaintext)
            CodeHash = r.CodeHash,
            //CodeNonce = r.CodeNonce, // keep only if it exists on record/entity; otherwise remove

            ExpiresAt = r.ExpiresAt,
            AttemptCount = r.AttemptCount,
            IsConsumed = r.IsConsumed,

            VerificationToken = r.VerificationToken,                 // only if record includes these
            VerificationTokenExpiresAt = r.VerificationTokenExpiresAt // otherwise omit

            // CreatedAt is provider-managed if abstraction doesn't include it
        };

    private static OtpChallengeRecord FromEntity(OtpChallengeEntity e)
        => new OtpChallengeRecord(
            ChallengeId: e.RowKey,
            Destination: e.Destination,
            Purpose: Enum.Parse<SenderPurpose>(e.Purpose, ignoreCase: true),
            TenantId: string.IsNullOrWhiteSpace(e.TenantId) ? null : Guid.Parse(e.TenantId),

            CodeHash: e.CodeHash,
            //CodeNonce: e.CodeNonce, // only if exists

            ExpiresAt: e.ExpiresAt,
            AttemptCount: e.AttemptCount,
            IsConsumed: e.IsConsumed,

            VerificationToken: e.VerificationToken,                 // only if exists
            VerificationTokenExpiresAt: e.VerificationTokenExpiresAt // only if exists
        );
}
