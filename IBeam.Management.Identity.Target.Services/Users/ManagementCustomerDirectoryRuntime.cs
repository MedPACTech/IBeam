using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Users;

public sealed class ManagementCustomerUserDirectoryOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:UserDirectory";
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;
    public bool IsValid() { try { return Convert.FromBase64String(ProtectionKey).Length == 32 &&
        TokenLifetimeMinutes is >= 1 and <= 60 && TimeoutSeconds is >= 1 and <= 30; } catch { return false; } }
    public override string ToString() => "Customer directory options (key redacted)";
}
internal sealed class ManagementCustomerDirectoryRuntime(IOptions<ManagementCustomerUserDirectoryOptions> options, TimeProvider clock,
    IServiceOperationExecutor operations)
{
    private sealed record Cursor(string Continuation, DateTimeOffset ExpiresUtc);
    internal sealed record State(string Context, string? Continuation, DateTimeOffset ExpiresUtc, string? Prefix);
    public State Resolve(ManagementDelegatedActor actor, string tenantSource, string source, Guid tenantId, string operation, ManagementCustomerDirectoryQuery query, string[] statuses)
    {
        if (query is null || query.PageSize is < 1 or > 100 || query.NamePrefix?.Length > 100 || query.NamePrefix?.Any(char.IsControl) == true ||
            query.Status is not null && !statuses.Contains(query.Status, StringComparer.Ordinal)) throw new ManagementCustomerIdentityInvalidRequestException();
        var prefix = query.NamePrefix?.Trim().ToUpperInvariant(); if (prefix == "") prefix = null;
        var context = JsonSerializer.Serialize(new { actor.ApplicationKey, actor.Environment, actor.IdentityRealmId, actor.OrganizationId,
            actor.UserId, actor.CredentialId, tenantSource, source, tenantId, operation, query.PageSize, query.Status, prefix });
        var codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
        var cursor = query.ContinuationToken is null ? null : codec.Unprotect<Cursor>("customer-identity-members", context, query.ContinuationToken);
        var expires = cursor?.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes);
        if (expires <= clock.GetUtcNow() || cursor?.Continuation.Length > 12000) throw new ManagementCustomerIdentityInvalidRequestException();
        return new(context, cursor?.Continuation, expires, prefix);
    }
    public string? Continue(State state, string? continuation)
    {
        if (continuation is null) return null;
        if (continuation.Length is < 1 or > 12000) throw new ManagementCustomerIdentityUnavailableException();
        try { return new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey))
            .Protect("customer-identity-members", state.Context, new Cursor(continuation, state.ExpiresUtc)); }
        catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityUnavailableException(); }
    }
    public Task<T> Run<T>(object owner, string operation, Guid tenantId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => operations.ExecuteAsync(owner, async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try { if (!options.Value.IsValid()) throw new ManagementAccessDeniedException(); return await action(timeout.Token); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityInvalidRequestException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException))
            { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityId = tenantId,
            EntityName = "ManagementCustomerDirectory", PermissionEnabled = false, AuditEnabled = true }, ct);
    public static string? Email(string? value) => value is { Length: > 0 and <= 320 } && !value.Any(char.IsControl) &&
        MailAddress.TryCreate(value, out var address) && address.Address == value ? value : null;
    public static string? Name(string? value) => value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl) ? value : null;
    public static IReadOnlyList<Guid> Roles(string? csv)
    {
        if (csv?.Length > 2048) throw new FormatException();
        var values = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > 32 || values.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty)) throw new FormatException();
        return values.Select(Guid.Parse).Distinct().ToArray();
    }
    public static IReadOnlyList<string> RoleNames(string? value, bool json)
    {
        if (value?.Length > 8192) throw new FormatException();
        var names = string.IsNullOrWhiteSpace(value) ? [] : json ? JsonSerializer.Deserialize<string[]>(value, new JsonSerializerOptions { MaxDepth = 4 }) ?? [] :
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length > 32 || names.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x.Any(char.IsControl)) ||
            names.Sum(x => x.Length) > 1024) throw new FormatException();
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Prefix(string? prefix, params string?[] values) => prefix is null ||
        values.Any(x => x?.Trim().ToUpperInvariant().StartsWith(prefix, StringComparison.Ordinal) == true);
}
