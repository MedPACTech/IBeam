using System;
using System.Collections.Generic;
using System.Text;

namespace IBeam.Identity.Repositories.AzureTable.Options
{
    internal static class OtpChallengeKeys
    {
        public static (string pk, string rk) For(string? tenantId, string destinationHash, string challengeId)
        {
            var t = string.IsNullOrWhiteSpace(tenantId) ? "global" : tenantId;
            var pk = $"otp:{t}:{destinationHash}";
            var rk = challengeId;
            return (pk, rk);
        }
    }

    internal static class UserTenantKeys
    {
        public static (string pk, string rk) For(string userId, string tenantId)
            => ($"USR#{userId}", $"TEN#{tenantId}");

        public static string PkForUser(string userId)
            => $"USR#{userId}";
    }

    internal static class TenantUserKeys
    {
        public static (string pk, string rk) For(string tenantId, string userId)
            => ($"TEN#{tenantId}", $"USR#{userId}");

        public static string PkForTenant(string tenantId)
            => $"TEN#{tenantId}";
    }

    internal static class TenantKeys
    {
        public const string Pk = "TEN";
        public static (string pk, string rk) For(string tenantId) => (Pk, tenantId);
    }

}
