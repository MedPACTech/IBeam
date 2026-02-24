using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using AbstractionIdentityUser = IBeam.Identity.Abstractions.Models.IdentityUser;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

internal sealed class AzureTableIdentityUserStore : IIdentityUserStore
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AzureTableIdentityUserStore(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<AbstractionIdentityUser?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var normalized = NormalizeEmail(email);
            var user = await _userManager.FindByEmailAsync(normalized);

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

            var user = await _userManager.FindByIdAsync(userId.ToString("D"));
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

            var appUser = new ApplicationUser
            {
                UserName = email,
                Email = email,

                // optional if your provider-internal user keeps it
                DisplayName = request.DisplayName ?? string.Empty
            };

            var result = await _userManager.CreateAsync(appUser, request.Password);

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
            var user = await _userManager.FindByEmailAsync(normalized);
            if (user is null) return false;

            // Consider user lockout checks here if your abstraction expects that.
            return await _userManager.CheckPasswordAsync(user, password);
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
                _userManager.Users.FirstOrDefault(u => u.PhoneNumber == normalized), ct);
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
            var user = await _userManager.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");
            user.Email = NormalizeEmail(newEmail);
            user.UserName = user.Email;
            var result = await _userManager.UpdateAsync(user);
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
            var user = await _userManager.FindByIdAsync(userId.ToString("D"));
            if (user is null) throw new InvalidOperationException("User not found.");
            user.PhoneNumber = NormalizePhone(newPhone);
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to update phone: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private static string NormalizeEmail(string email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizePhone(string phone)
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
            DisplayName: u.DisplayName
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
