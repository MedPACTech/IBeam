using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Management.Identity.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Api;

internal sealed class ManagementIdentityRuntimeValidator(
    IOptions<ManagementIdentityOptions> identity,
    IOptions<AzureTableIdentityOptions> storage,
    IOptions<JwtOptions> jwt,
    IOptions<OtpOptions> otp,
    IOptions<LoginAttemptOptions> attempts,
    IOptions<AuthEventOptions> events) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var token = jwt.Value;
        if (identity.Value.BootstrapEnabled || storage.Value.CreateTablesIfNotExists)
            throw new InvalidOperationException("Bootstrap and schema provisioning must be disabled in the management API host.");
        if (token.AccessTokenMinutes > 15 || token.SessionInactivityMinutes is not (> 0 and <= 60) ||
            token.SessionAbsoluteLifetimeDays is not (> 0 and <= 1) ||
            !string.IsNullOrEmpty(token.SigningKey) && token.SigningKey.Length < 32)
            throw new InvalidOperationException("Management tokens require short access/session lifetimes and a strong signing key.");
        if (otp.Value.AllowAutoProvisionForUnknownUser || otp.Value.CodeLength < 6 ||
            otp.Value.MaxAttempts is < 1 or > 10 || string.IsNullOrWhiteSpace(otp.Value.HashSalt) ||
            otp.Value.HashSalt == "change-me" || otp.Value.HashSalt.Length < 32 || otp.Value.VerificationTokenSecret.Length < 32 ||
            !attempts.Value.Enabled || !events.Value.StrictPublishFailures)
            throw new InvalidOperationException("Management requires closed enrollment, bounded MFA attempts, dedicated OTP secrets and strict lifecycle guards.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
