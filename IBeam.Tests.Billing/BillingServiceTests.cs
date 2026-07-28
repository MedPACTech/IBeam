using IBeam.Billing;
using IBeam.Billing.Services;
using IBeam.Services.Abstractions;
using System.Runtime.CompilerServices;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("54291c6a-c2e8-484f-9464-645e10c34166");
    private static readonly Guid UserId = Guid.Parse("13ef59d1-d11d-427f-8c3b-411dfc0a805e");

    [TestMethod]
    public async Task CreateCustomerAsync_CreatesTenantScopedCustomerAndUsesOperationExecutor()
    {
        var executor = new RecordingServiceOperationExecutor();
        var service = new BillingCustomerService(new InMemoryBillingStore(), executor);

        var customer = await service.CreateCustomerAsync(
            TenantId,
            new CreateBillingCustomerRequest
            {
                UserId = UserId,
                DisplayName = "Hubbsly Owner",
                BillingMode = BillingModes.SelfServiceMonthly,
                ProviderName = "stripe",
                ProviderCustomerId = "cus_123"
            });

        Assert.AreEqual(TenantId, customer.TenantId);
        Assert.AreEqual(UserId, customer.UserId);
        Assert.AreEqual(BillingModes.SelfServiceMonthly, customer.BillingMode);
        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(nameof(BillingCustomerService.CreateCustomerAsync), executor.Calls[0].CallerMemberName);
        Assert.AreEqual(TenantId, executor.Calls[0].Options?.TenantId);
    }

    [TestMethod]
    public async Task SubscriptionService_UpdatesSubscriptionStateAndSeats()
    {
        var store = new InMemoryBillingStore();
        var customers = new BillingCustomerService(store);
        var subscriptions = new BillingSubscriptionService(store);
        var customer = await customers.CreateCustomerAsync(
            TenantId,
            new CreateBillingCustomerRequest { DisplayName = "Enterprise Tenant", BillingMode = BillingModes.AnnualContract });
        var subscription = await subscriptions.CreateSubscriptionAsync(
            TenantId,
            new CreateBillingSubscriptionRequest
            {
                BillingCustomerId = customer.BillingCustomerId,
                ProductKey = "hubbsly",
                PlanKey = "enterprise",
                BillingMode = BillingModes.AnnualContract,
                SeatQuantity = 10,
                ProviderName = "stripe",
                ProviderSubscriptionId = "sub_123"
            });

        var updated = await subscriptions.UpdateSubscriptionAsync(
            TenantId,
            subscription.BillingSubscriptionId,
            new UpdateBillingSubscriptionRequest
            {
                Status = BillingSubscriptionStatuses.PastDue,
                SeatQuantity = 12,
                CancelAtPeriodEnd = true
            });
        var listed = await subscriptions.ListSubscriptionsAsync(TenantId);

        Assert.AreEqual(BillingSubscriptionStatuses.PastDue, updated.Status);
        Assert.AreEqual(12, updated.SeatQuantity);
        Assert.IsTrue(updated.CancelAtPeriodEnd);
        Assert.HasCount(1, listed);
    }

    [TestMethod]
    public async Task InvoiceService_TracksInvoicePaymentState()
    {
        var store = new InMemoryBillingStore();
        var customers = new BillingCustomerService(store);
        var invoices = new BillingInvoiceService(store);
        var customer = await customers.CreateCustomerAsync(
            TenantId,
            new CreateBillingCustomerRequest { DisplayName = "Manual Billing", BillingMode = BillingModes.ManualInvoice });
        var invoice = await invoices.CreateInvoiceAsync(
            TenantId,
            new CreateBillingInvoiceRequest
            {
                BillingCustomerId = customer.BillingCustomerId,
                BillingMode = BillingModes.ManualInvoice,
                Status = BillingInvoiceStatuses.Open,
                Currency = "usd",
                AmountDue = 750m
            });

        var paidUtc = DateTimeOffset.UtcNow;
        var paid = await invoices.UpdateInvoiceAsync(
            TenantId,
            invoice.BillingInvoiceId,
            new UpdateBillingInvoiceRequest
            {
                Status = BillingInvoiceStatuses.Paid,
                AmountPaid = 750m,
                PaidUtc = paidUtc
            });
        var listed = await invoices.ListInvoicesAsync(TenantId);

        Assert.AreEqual(BillingInvoiceStatuses.Paid, paid.Status);
        Assert.AreEqual(750m, paid.AmountPaid);
        Assert.AreEqual(paidUtc, paid.PaidUtc);
        Assert.HasCount(1, listed);
    }

    [TestMethod]
    public async Task ProviderEventService_IsIdempotentByProviderEventId()
    {
        var service = new BillingProviderEventService(new InMemoryBillingStore());
        var request = new RecordBillingProviderEventRequest
        {
            ProviderName = "stripe",
            ProviderEventId = "evt_123",
            EventType = "invoice.paid",
            TenantId = TenantId,
            ProviderInvoiceId = "in_123"
        };

        var first = await service.RecordEventAsync(request);
        var second = await service.RecordEventAsync(request);
        var events = await service.ListEventsAsync(TenantId);

        Assert.AreEqual(first.BillingProviderEventId, second.BillingProviderEventId);
        Assert.AreEqual("stripe:evt_123", first.IdempotencyKey);
        Assert.HasCount(1, events);
    }

    [TestMethod]
    public async Task Store_SeparatesTenantBillingRecords()
    {
        var store = new InMemoryBillingStore();
        var service = new BillingCustomerService(store);

        await service.CreateCustomerAsync(TenantId, new CreateBillingCustomerRequest { DisplayName = "Tenant A" });
        await service.CreateCustomerAsync(Guid.NewGuid(), new CreateBillingCustomerRequest { DisplayName = "Tenant B" });

        var customers = await service.ListCustomersAsync(TenantId);

        Assert.HasCount(1, customers);
        Assert.AreEqual("Tenant A", customers[0].DisplayName);
    }

    private sealed class RecordingServiceOperationExecutor : IServiceOperationExecutor
    {
        private readonly List<ServiceOperationCall> _calls = [];

        public IReadOnlyList<ServiceOperationCall> Calls => _calls;

        public async Task ExecuteAsync(
            object serviceInstance,
            Func<CancellationToken, Task> operation,
            ServiceOperationExecutionOptions? options = null,
            CancellationToken ct = default,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            await operation(ct).ConfigureAwait(false);
        }

        public async Task<TResult> ExecuteAsync<TResult>(
            object serviceInstance,
            Func<CancellationToken, Task<TResult>> operation,
            ServiceOperationExecutionOptions? options = null,
            CancellationToken ct = default,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            return await operation(ct).ConfigureAwait(false);
        }

        public void Execute(
            object serviceInstance,
            Action operation,
            ServiceOperationExecutionOptions? options = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            operation();
        }

        public TResult Execute<TResult>(
            object serviceInstance,
            Func<TResult> operation,
            ServiceOperationExecutionOptions? options = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            _calls.Add(new ServiceOperationCall(callerMemberName, options));
            return operation();
        }
    }

    private sealed record ServiceOperationCall(
        string? CallerMemberName,
        ServiceOperationExecutionOptions? Options);
}
