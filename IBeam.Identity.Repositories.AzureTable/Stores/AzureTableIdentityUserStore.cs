using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using IBeam.Identity.Services.Utils;
using Microsoft.AspNetCore.Identity;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using AbstractionIdentityUser = IBeam.Identity.Models.IdentityUser;
using ElCamino.AspNetCore.Identity.AzureTable;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableIdentityUserStore : IIdentityUserStore
{
    //private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> _store;
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;
    private readonly PasswordHasher<ApplicationUser> _passwordHasher = new();

    public AzureTableIdentityUserStore(
        UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> store,
        TableServiceClient serviceClient,
        IOptions<AzureTableIdentityOptions> opts)
    {
        _store = store;
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<AbstractionIdentityUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = NormalizeEmail(email);
            var user = await FindByIdentifierAsync("email", normalized, ct);
            if (user is null)
            {
                user = await _store.FindByEmailAsync(normalized, ct);
                if (user is not null)
                    await UpsertIdentifierAsync("email", normalized, user.Id, ct);
            }

            return user is null ? null : MapOrThrow(user);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AbstractionIdentityUser?> FindByIdAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = await _store.FindByIdAsync(userId.ToString("D"));
            return user is null ? null : MapOrThrow(user);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<CreateUserResult> CreateAsync(RegisterUserRequest request, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var email = NormalizeEmail(request.Email);
            var phone = NormalizePhone(request.PhoneNumber);
            var userName = !string.IsNullOrWhiteSpace(email) ? email : phone;
            if (string.IsNullOrWhiteSpace(userName))
                throw new InvalidOperationException("Either email or phone number is required.");

            await EnsureIdentifierAvailableAsync("email", email, null, ct);
            await EnsureIdentifierAvailableAsync("sms", phone, null, ct);
            if (!string.IsNullOrWhiteSpace(email) && await _store.FindByEmailAsync(email, ct) is not null)
                throw new InvalidOperationException("email identifier is already bound to another user.");

            var appUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("D"),
                UserName = userName,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,

                // optional if your provider-internal user keeps it
                DisplayName = request.DisplayName ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(request.Password))
                appUser.PasswordHash = _passwordHasher.HashPassword(appUser, request.Password);

            var result = await _store.CreateAsync(appUser);

            if (result.Succeeded)
            {
                await BindIdentifierAsync("email", email, appUser.Id, ct);
                await BindIdentifierAsync("sms", phone, appUser.Id, ct);
                return CreateUserResult.Success(MapOrThrow(appUser));
            }

            return CreateUserResult.Failure(MapErrors(result));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    // NOTE: Prefer moving this to Core/Auth orchestration later.
    // Keep for now to support email+password flow without leaking UserManager.
    public async Task<bool> ValidatePasswordAsync(string emailOrPhone, string password, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = emailOrPhone.Contains('@', StringComparison.Ordinal)
                ? await FindByIdentifierAsync("email", NormalizeEmail(emailOrPhone), ct)
                : await FindByIdentifierAsync("sms", NormalizePhone(emailOrPhone), ct);

            if (user is null && emailOrPhone.Contains('@', StringComparison.Ordinal))
                user = await _store.FindByEmailAsync(NormalizeEmail(emailOrPhone), ct);

            if (user is null) return false;

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                return false;

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            user.SecurityStamp = Guid.NewGuid().ToString("N");

            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to set password: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task SetEmailConfirmedAsync(Guid userId, bool confirmed, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");

            user.EmailConfirmed = confirmed;

            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update email confirmation: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task SetTwoFactorAsync(Guid userId, bool enabled, string? preferredMethod = null, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");

            user.TwoFactorEnabled = enabled;
            user.PreferredTwoFactorMethod = string.IsNullOrWhiteSpace(preferredMethod)
                ? null
                : preferredMethod.Trim().ToLowerInvariant();

            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update two-factor settings: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AbstractionIdentityUser?> FindByPhoneAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var normalized = NormalizePhone(phoneNumber);
            var user = await FindByIdentifierAsync("sms", normalized, ct);
            return user is null ? null : MapOrThrow(user);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task UpdateEmailAsync(Guid userId, string newEmail, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");
            var oldEmail = NormalizeEmail(user.Email);
            var normalized = NormalizeEmail(newEmail);
            await EnsureIdentifierAvailableAsync("email", normalized, user.Id, ct);
            user.Email = normalized;
            user.UserName = user.Email;
            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update email: " + string.Join(", ", result.Errors.Select(e => e.Description)));

            await BindIdentifierAsync("email", normalized, user.Id, ct);
            if (!string.Equals(oldEmail, normalized, StringComparison.OrdinalIgnoreCase))
                await DeleteIdentifierAsync("email", oldEmail, ct);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task UpdatePhoneAsync(Guid userId, string newPhone, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");
            var oldPhone = NormalizePhone(user.PhoneNumber);
            var normalized = NormalizePhone(newPhone);
            await EnsureIdentifierAvailableAsync("sms", normalized, user.Id, ct);
            user.PhoneNumber = normalized;
            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update phone: " + string.Join(", ", result.Errors.Select(e => e.Description)));

            await BindIdentifierAsync("sms", normalized, user.Id, ct);
            if (!string.Equals(oldPhone, normalized, StringComparison.OrdinalIgnoreCase))
                await DeleteIdentifierAsync("sms", oldPhone, ct);
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizePhone(string? phone)
        => IdentityUtils.NormalizePhoneNumber(phone);

    private TableClient GetAuthIdentifiersTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AuthIdentifiersTableName));

    private static string IdentifierPartition(string type, string identifier)
        => $"AUTH|{type.Trim().ToUpperInvariant()}|{identifier.Trim().ToUpperInvariant()}";

    private static string IdentifierRowKey() => "USER";

    private async Task<ApplicationUser?> FindByIdentifierAsync(string type, string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var table = GetAuthIdentifiersTable();
        var response = await table.GetEntityIfExistsAsync<AuthIdentifierEntity>(
            IdentifierPartition(type, identifier),
            IdentifierRowKey(),
            cancellationToken: ct).ConfigureAwait(false);

        if (!response.HasValue || string.IsNullOrWhiteSpace(response.Value.UserId))
            return null;

        return await _store.FindByIdAsync(response.Value.UserId);
    }

    private async Task EnsureIdentifierAvailableAsync(string type, string identifier, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        var table = GetAuthIdentifiersTable();
        var response = await table.GetEntityIfExistsAsync<AuthIdentifierEntity>(
            IdentifierPartition(type, identifier),
            IdentifierRowKey(),
            cancellationToken: ct).ConfigureAwait(false);

        if (response.HasValue &&
            !string.Equals(response.Value.UserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{type} identifier is already bound to another user.");
        }
    }

    public async Task SetPhoneConfirmedAsync(Guid userId, bool confirmed, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var user = await _store.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");

            user.PhoneNumberConfirmed = confirmed;

            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update phone confirmation: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task UpsertIdentifierAsync(string type, string identifier, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        var table = GetAuthIdentifiersTable();
        await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);

        var entity = new AuthIdentifierEntity
        {
            PartitionKey = IdentifierPartition(type, identifier),
            RowKey = IdentifierRowKey(),
            UserId = userId,
            IdentifierType = type.Trim().ToLowerInvariant(),
            Identifier = identifier.Trim(),
            BoundAtUtc = DateTimeOffset.UtcNow
        };

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }

    private async Task BindIdentifierAsync(string type, string identifier, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        var table = GetAuthIdentifiersTable();
        await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);

        var entity = new AuthIdentifierEntity
        {
            PartitionKey = IdentifierPartition(type, identifier),
            RowKey = IdentifierRowKey(),
            UserId = userId,
            IdentifierType = type.Trim().ToLowerInvariant(),
            Identifier = identifier.Trim(),
            BoundAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            await EnsureIdentifierAvailableAsync(type, identifier, userId, ct).ConfigureAwait(false);
            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
    }

    private async Task DeleteIdentifierAsync(string type, string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        try
        {
            await GetAuthIdentifiersTable()
                .DeleteEntityAsync(IdentifierPartition(type, identifier), IdentifierRowKey(), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private static bool IsCancellation(Exception ex)
        => ex is OperationCanceledException;

    private static AbstractionIdentityUser MapOrThrow(ApplicationUser u)
    {
        if (!Guid.TryParse(u.Id, out var id))
            throw IdentityExceptionTranslator.ToProviderException(
                new InvalidOperationException($"Provider user id '{u.Id}' is not a GUID."));

        return new AbstractionIdentityUser(
            UserId: id,
            Email: u.Email ?? string.Empty,
            EmailConfirmed: u.EmailConfirmed,
            PhoneNumber: u.PhoneNumber,
            PhoneConfirmed: u.PhoneNumberConfirmed,
            DisplayName: u.DisplayName,
            TwoFactorEnabled: u.TwoFactorEnabled,
            PreferredTwoFactorMethod: u.PreferredTwoFactorMethod
        );
    }

    private static IReadOnlyDictionary<string, string[]> MapErrors(IdentityResult result)
        => result.Errors
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Code) ? "Identity" : e.Code)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Description).ToArray(),
                StringComparer.OrdinalIgnoreCase);
}
