using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Core.Entities;
using IBeam.Identity.Repositories.AzureTable.Entities;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Repositories.AzureTable.Stores
{
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

                var entity = ToEntity(record);
                await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw IdentityExceptionTranslator.ToProviderException(ex);
            }
        }

        public async Task<OtpChallengeRecord?> GetAsync(Guid challengeId, CancellationToken ct = default)
        {
            try
            {
                var table = GetOtpTable();
                var pk = PartitionFor(challengeId);
                var rk = challengeId.ToString("D");

                var response = await table.GetEntityIfExistsAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct)
                    .ConfigureAwait(false);

                return response.HasValue ? FromEntity(response.Value) : null;
            }
            catch (Exception ex)
            {
                throw IdentityExceptionTranslator.ToProviderException(ex);
            }
        }

        public async Task IncrementAttemptAsync(Guid challengeId, CancellationToken ct = default)
        {
            try
            {
                var table = GetOtpTable();
                var pk = PartitionFor(challengeId);
                var rk = challengeId.ToString("D");

                // Optimistic concurrency loop
                for (var i = 0; i < 5; i++)
                {
                    var get = await table.GetEntityAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct).ConfigureAwait(false);
                    var entity = get.Value;

                    entity.AttemptCount += 1;

                    try
                    {
                        await table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                        return;
                    }
                    catch (RequestFailedException rfe) when (rfe.Status == 412) // Precondition Failed (ETag mismatch)
                    {
                        // retry
                    }
                }

                throw new IdentityProviderException("Failed to increment OTP attempts due to concurrent updates.");
            }
            catch (Exception ex)
            {
                throw IdentityExceptionTranslator.ToProviderException(ex);
            }
        }

        public async Task MarkConsumedAsync(Guid challengeId, string verificationToken, DateTimeOffset verificationTokenExpiresAt, CancellationToken ct = default)
        {
            try
            {
                var table = GetOtpTable();
                var pk = PartitionFor(challengeId);
                var rk = challengeId.ToString("D");

                for (var i = 0; i < 5; i++)
                {
                    var get = await table.GetEntityAsync<OtpChallengeEntity>(pk, rk, cancellationToken: ct).ConfigureAwait(false);
                    var entity = get.Value;

                    entity.IsConsumed = true;
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
            => _serviceClient.GetTableClient($"{_opts.TablePrefix}{_opts.OtpChallengesTableName}");

        private static string PartitionFor(Guid challengeId)
        {
            // Simple, scalable-enough partition strategy:
            // bucket by first 2 chars of guid so partitions distribute
            var s = challengeId.ToString("N");
            return $"OTP-{s.Substring(0, 2)}";
        }

        private static OtpChallengeEntity ToEntity(OtpChallengeRecord r)
            => new()
            {
                PartitionKey = PartitionFor(r.ChallengeId),
                RowKey = r.ChallengeId.ToString("D"),
                UserId = r.UserId,
                Channel = r.Channel,
                Destination = r.Destination,
                CodeHash = r.CodeHash,
                ExpiresAt = r.ExpiresAt,
                AttemptCount = r.AttemptCount,
                IsConsumed = r.IsConsumed,
                VerificationToken = r.VerificationToken,
                VerificationTokenExpiresAt = r.VerificationTokenExpiresAt,
                CreatedAt = r.CreatedAt,
            };

        private static OtpChallengeRecord FromEntity(OtpChallengeEntity e)
            => new OtpChallengeRecord(
                challengeId: Guid.Parse(e.RowKey),
                userId: e.UserId,
                channel: e.Channel,
                destination: e.Destination,
                codeHash: e.CodeHash,
                expiresAt: e.ExpiresAt,
                attemptCount: e.AttemptCount,
                isConsumed: e.IsConsumed,
                verificationToken: e.VerificationToken,
                verificationTokenExpiresAt: e.VerificationTokenExpiresAt,
                createdAt: e.CreatedAt
            );
    }
}
