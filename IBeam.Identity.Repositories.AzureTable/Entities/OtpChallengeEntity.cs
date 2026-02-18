using System;
using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities
{
    internal sealed class OtpChallengeEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Domain fields
        public string UserId { get; set; } = default!;
        public string Channel { get; set; } = default!; // "sms" / "email" etc.
        public string Destination { get; set; } = default!; // phone/email masked or full (your choice)

        public string CodeHash { get; set; } = default!;
        public DateTimeOffset ExpiresAt { get; set; }

        public int AttemptCount { get; set; }
        public bool IsConsumed { get; set; }

        public string? VerificationToken { get; set; }
        public DateTimeOffset? VerificationTokenExpiresAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
