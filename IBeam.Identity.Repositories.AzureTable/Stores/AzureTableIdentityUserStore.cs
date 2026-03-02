using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using AbstractionIdentityUser = IBeam.Identity.Abstractions.Models.IdentityUser;
using ElCamino.AspNetCore.Identity.AzureTable;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableIdentityUserStore : IIdentityUserStore
{
    //private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> _store;
    private readonly PasswordHasher<ApplicationUser> _passwordHasher = new();

    public AzureTableIdentityUserStore(UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> store)
        => _store = store;

    public async Task<AbstractionIdentityUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = NormalizeEmail(email);
            var user = await _store.FindByEmailAsync(normalized, ct);

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

            var appUser = new ApplicationUser
            {
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
                return CreateUserResult.Success(MapOrThrow(appUser));

            return CreateUserResult.Failure(MapErrors(result));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    // NOTE: Prefer moving this to Core/Auth orchestration later.
    // Keep for now to support email+password flow without leaking UserManager.
    public async Task<bool> ValidatePasswordAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = NormalizeEmail(email);
            var user = await _store.FindByEmailAsync(normalized);
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
            var user = await Task.Run(() =>
                _store.Users.FirstOrDefault(u => u.PhoneNumber == normalized), ct);
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
            user.Email = NormalizeEmail(newEmail);
            user.UserName = user.Email;
            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update email: " + string.Join(", ", result.Errors.Select(e => e.Description)));
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
            user.PhoneNumber = NormalizePhone(newPhone);
            var result = await _store.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update phone: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizePhone(string? phone)
        => (phone ?? string.Empty).Trim();

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
