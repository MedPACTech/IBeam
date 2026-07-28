using IBeam.Credits;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IBeam.Tests.Credits;

[TestClass]
public sealed class CreditModelTests
{
    private static readonly Guid TenantId = Guid.Parse("36bb2580-7c52-4e44-bc25-f60122a44a5d");

    [TestMethod]
    public void BucketAndAccount_NormalizeGenericCreditMetadata()
    {
        var bucket = CreditBucketInfo.Create(
            " AI_CHAT ",
            "AI Chat",
            unitName: "credits",
            metadata: new Dictionary<string, string> { [" domain "] = " ai " });
        var account = CreditAccountInfo.Create(
            TenantId,
            "Tenant Credits",
            subjectType: "user",
            subjectId: "user-1");

        Assert.AreEqual("ai-chat", bucket.Key);
        Assert.AreEqual("credits", bucket.UnitName);
        Assert.AreEqual("ai", bucket.Metadata["domain"]);
        Assert.AreEqual(TenantId, account.TenantId);
        Assert.AreEqual(CreditAccountStatuses.Active, account.Status);
    }

    [TestMethod]
    public void GrantModels_SupportOneTimeMonthlyExpiringAndRolloverMetadata()
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oneTime = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 100, CreditGrantTypes.OneTime);
        var monthly = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 500, CreditGrantTypes.Monthly, period: "monthly");
        var expiring = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 250, CreditGrantTypes.Expiring, expiresUtc: now.AddDays(30));
        var rollover = CreditGrantInfo.Create(
            TenantId,
            accountId,
            "ai-chat",
            50,
            CreditGrantTypes.Monthly,
            CreditRolloverPolicies.CappedRollover,
            metadata: new Dictionary<string, string> { ["cap"] = "150" });

        Assert.AreEqual(CreditGrantTypes.OneTime, oneTime.GrantType);
        Assert.AreEqual("monthly", monthly.Period);
        Assert.AreEqual(CreditGrantTypes.Expiring, expiring.GrantType);
        Assert.AreEqual(CreditRolloverPolicies.CappedRollover, rollover.RolloverPolicy);
        Assert.AreEqual("150", rollover.Metadata["cap"]);
    }

    [TestMethod]
    public void LedgerBalance_AppliesGrantsAndDebits()
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var grant = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 100, startsUtc: now.AddMinutes(-5));
        var debit = CreditLedgerEntryInfo.CreateDebit(TenantId, accountId, "ai-chat", 35, "ai.chat.complete", effectiveUtc: now);

        var balance = CreditLedgerCalculator.CalculateBalance(
            TenantId,
            accountId,
            "ai-chat",
            [grant.ToLedgerEntry(), debit],
            now.AddMinutes(1));

        Assert.AreEqual(100, balance.Granted);
        Assert.AreEqual(35, balance.Debited);
        Assert.AreEqual(65, balance.Available);
    }

    [TestMethod]
    public void LedgerBalance_ExcludesExpiredCreditsFromAvailability()
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var activeGrant = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 100, startsUtc: now.AddDays(-1), expiresUtc: now.AddDays(1));
        var expiredGrant = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 40, startsUtc: now.AddDays(-5), expiresUtc: now.AddDays(-1));

        var balance = CreditLedgerCalculator.CalculateBalance(
            TenantId,
            accountId,
            "ai-chat",
            [activeGrant.ToLedgerEntry(), expiredGrant.ToLedgerEntry()],
            now);

        Assert.AreEqual(140, balance.Granted);
        Assert.AreEqual(40, balance.Expired);
        Assert.AreEqual(100, balance.Available);
    }

    [TestMethod]
    public void LedgerBalance_SeparatesBuckets()
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var chat = CreditGrantInfo.Create(TenantId, accountId, "ai-chat", 100, startsUtc: now.AddMinutes(-1));
        var sms = CreditGrantInfo.Create(TenantId, accountId, "sms", 25, startsUtc: now.AddMinutes(-1));
        var smsDebit = CreditLedgerEntryInfo.CreateDebit(TenantId, accountId, "sms", 5, effectiveUtc: now);

        var chatBalance = CreditLedgerCalculator.CalculateBalance(
            TenantId,
            accountId,
            "ai-chat",
            [chat.ToLedgerEntry(), sms.ToLedgerEntry(), smsDebit],
            now.AddMinutes(1));
        var smsBalance = CreditLedgerCalculator.CalculateBalance(
            TenantId,
            accountId,
            "sms",
            [chat.ToLedgerEntry(), sms.ToLedgerEntry(), smsDebit],
            now.AddMinutes(1));

        Assert.AreEqual(100, chatBalance.Available);
        Assert.AreEqual(20, smsBalance.Available);
    }

    [TestMethod]
    public void LedgerStoreContract_IsAppendOnlyFromCoreModel()
    {
        var methods = typeof(ICreditLedgerStore).GetMethods().Select(x => x.Name).ToArray();

        CollectionAssert.Contains(methods, nameof(ICreditLedgerStore.AppendLedgerEntryAsync));
        CollectionAssert.Contains(methods, nameof(ICreditLedgerStore.ListLedgerEntriesAsync));
        CollectionAssert.DoesNotContain(methods, "UpdateLedgerEntryAsync");
        CollectionAssert.DoesNotContain(methods, "DeleteLedgerEntryAsync");
    }
}
