using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

internal sealed partial class CustomerDirectoryBackend
{
    public bool EnableOnboarding { get; set; }
    public bool CompleteOnboarding { get; set; }
    public string OnboardingDelivery { get; set; } = "sent";
    public int OnboardingCreates { get; private set; }
    public int OnboardingDeliveries { get; private set; }
    private readonly Guid _job = Guid.NewGuid();
    private readonly Dictionary<(string Host, string Operation, string Key, Guid Scope), (string Fingerprint, ManagementIdentityOperationDto Receipt)> _onboard = [];
    private async Task<HttpResponseMessage> Onboarding(HttpRequestMessage request, string operation, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/enterprise-onboardings/policies", StringComparison.Ordinal)) {
            Assert.AreEqual(ManagementCustomerIdentityOperations.OnboardingRead, operation);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new[] { new { key = "enterprise-owner", requiresInitialization = true, initializerKey = Canary } } }) };
        }
        if (request.Method == HttpMethod.Get) {
            lock (_onboard) {
                var row = _onboard.Where(x => x.Key.Host == uri.Host).Select(x => x.Value.Receipt).FirstOrDefault(x => uri.AbsolutePath.EndsWith(x.OperationId.ToString("D"), StringComparison.Ordinal));
                return row is null ? new(HttpStatusCode.NotFound) : OnboardingResponse(ProjectOnboard(row), HttpStatusCode.OK);
            }
        }
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct); var key = request.Headers.GetValues("Idempotency-Key").Single();
        if (uri.AbsolutePath.EndsWith("/operations/resolve", StringComparison.Ordinal)) {
            var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.AreEqual(operation, lookup.Operation); LookupCalls++;
            lock (_onboard) return _onboard.TryGetValue((uri.Host, operation, key, lookup.ResourceScope), out var found)
                ? OnboardingResponse(ProjectOnboard(found.Receipt), HttpStatusCode.OK) : new(HttpStatusCode.NotFound);
        }
        var retry = uri.AbsolutePath.EndsWith("/retry", StringComparison.Ordinal);
        Assert.AreEqual(retry ? ManagementCustomerIdentityOperations.OnboardingRetry : ManagementCustomerIdentityOperations.OnboardingCreate, operation);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        ManagementIdentityOperationDto result;
        lock (_onboard) {
            var identity = (uri.Host, operation, key, retry ? _job : Guid.Empty);
            if (_onboard.TryGetValue(identity, out var old)) {
                if (old.Fingerprint != fingerprint) return new(HttpStatusCode.Conflict);
                result = ProjectOnboard(old.Receipt);
            } else {
                if (retry) {
                    Assert.AreEqual("{}", System.Text.Encoding.UTF8.GetString(bytes));
                    var id = Guid.Parse(uri.Segments[^2].Trim('/'));
                    if (!_onboard.Any(x => x.Key.Host == uri.Host && x.Value.Receipt.OperationId == id)) return new(HttpStatusCode.NotFound);
                    OnboardingDelivery = "sent";
                } else OnboardingCreates++;
                OnboardingDeliveries++;
                var now = DateTimeOffset.UtcNow;
                result = new(retry ? Guid.NewGuid() : Operation, operation, "succeeded", 1, _job, now, now, now.AddDays(1));
                _onboard.Add(identity, (fingerprint, result)); result = ProjectOnboard(result);
            }
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.Accepted) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        return OnboardingResponse(result, HttpStatusCode.Accepted);
    }
    private ManagementIdentityOperationDto ProjectOnboard(ManagementIdentityOperationDto row)
        => row with { Onboarding = new(Operation, _job, Tenant, Invite, CompleteOnboarding ? "completed" : OnboardingDelivery == "sent" ? "awaiting-acceptance" : "recoverable-failure",
            "invitation-persisted", OnboardingDelivery, CompleteOnboarding, OnboardingDelivery == "sent" ? null : "delivery-unconfirmed", row.CreatedUtc, row.CreatedUtc.AddDays(1)) };
    private HttpResponseMessage OnboardingResponse(ManagementIdentityOperationDto row, HttpStatusCode code)
    {
        var value = JsonSerializer.SerializeToNode(row, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        value["privateOutbox"] = Canary; value["onboarding"]!["inviteToken"] = Canary;
        if (Failure == "onboard-bad-error") value["onboarding"]!["errorCode"] = Canary;
        return new(code) { Content = JsonContent.Create(new { success = true, data = value }) };
    }
}
