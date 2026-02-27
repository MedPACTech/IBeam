using Azure.Data.Tables;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using ElCaminoIdentityUser = ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser;

namespace TestBedApp.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const int DefaultTimeoutSeconds = 8;
    private static readonly UpperInvariantLookupNormalizer Normalizer = new();

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { ok = true, message = "AuthController reached" });
    }

    [HttpGet("storage-ping")]
    public async Task<IActionResult> StoragePing([FromServices] TableServiceClient tableServiceClient)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

        try
        {
            var table = tableServiceClient.GetTableClient("testbedhealth");
            await table.CreateIfNotExistsAsync(cts.Token);
            return Ok(new { ok = true, table = "testbedhealth" });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "Table operation timed out." });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, message = ex.Message });
        }
    }

    [HttpPost("diag-register")]
    public async Task<IActionResult> DiagRegister([FromBody] RegisterRequest req)
    {
        var steps = new List<object>();

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var serviceResult = await ResolveServiceWithTimeout<IUserStore<ElCaminoIdentityUser>>(HttpContext.RequestServices, "Resolve IUserStore", steps);
        if (!serviceResult.ok || serviceResult.value is null)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "Could not resolve IUserStore.", steps });
        }

        var userStore = serviceResult.value;
        if (userStore is not IUserPasswordStore<ElCaminoIdentityUser> passwordStore ||
            userStore is not IUserEmailStore<ElCaminoIdentityUser> emailStore)
        {
            steps.Add(new { step = "Validate store capabilities", ok = false, elapsedMs = 0L, detail = "Missing password/email interfaces." });
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, message = "Store missing required interfaces.", steps });
        }

        var normalizedEmail = Normalizer.NormalizeEmail(req.Email);
        var findResult = await RunWithTimeout(
            "FindByEmailAsync",
            ct => emailStore.FindByEmailAsync(normalizedEmail, ct),
            steps);

        if (!findResult.ok)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "FindByEmailAsync timed out or failed.", steps });
        }

        if (findResult.value is not null)
        {
            return Conflict(new { ok = false, message = "User already exists.", steps });
        }

        var user = new ElCaminoIdentityUser
        {
            UserName = req.Email,
            Email = req.Email,
            NormalizedUserName = Normalizer.NormalizeName(req.Email),
            NormalizedEmail = normalizedEmail,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var hasher = new PasswordHasher<ElCaminoIdentityUser>();
        var hash = hasher.HashPassword(user, req.Password);

        var setPwdResult = await RunWithTimeout<object?>(
            "SetPasswordHashAsync",
            async ct =>
            {
                await passwordStore.SetPasswordHashAsync(user, hash, ct);
                return null;
            },
            steps);

        if (!setPwdResult.ok)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "SetPasswordHashAsync timed out or failed.", steps });
        }

        var createResult = await RunWithTimeout(
            "CreateAsync",
            ct => userStore.CreateAsync(user, ct),
            steps);

        if (!createResult.ok)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "CreateAsync timed out or failed.", steps });
        }

        if (!createResult.value!.Succeeded)
        {
            return BadRequest(new
            {
                ok = false,
                errors = createResult.value.Errors.Select(e => new { e.Code, e.Description }),
                steps
            });
        }

        return Ok(new { ok = true, userId = user.Id, email = user.Email, steps });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var service = await ResolveServiceWithTimeout<IUserStore<ElCaminoIdentityUser>>(HttpContext.RequestServices, "Resolve IUserStore", null);
        if (!service.ok || service.value is null)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "UserStore resolution timed out." });
        }

        var userStore = service.value;
        if (userStore is not IUserPasswordStore<ElCaminoIdentityUser> passwordStore ||
            userStore is not IUserEmailStore<ElCaminoIdentityUser> emailStore)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, message = "Configured user store does not support passwords and emails." });
        }

        var normalizedEmail = Normalizer.NormalizeEmail(req.Email);
        var existing = await emailStore.FindByEmailAsync(normalizedEmail, HttpContext.RequestAborted);
        if (existing is not null)
        {
            return Conflict(new { ok = false, message = "User already exists." });
        }

        var user = new ElCaminoIdentityUser
        {
            UserName = req.Email,
            Email = req.Email,
            NormalizedUserName = Normalizer.NormalizeName(req.Email),
            NormalizedEmail = normalizedEmail,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var hasher = new PasswordHasher<ElCaminoIdentityUser>();
        var hash = hasher.HashPassword(user, req.Password);
        await passwordStore.SetPasswordHashAsync(user, hash, HttpContext.RequestAborted);

        var create = await userStore.CreateAsync(user, HttpContext.RequestAborted);
        if (!create.Succeeded)
        {
            return BadRequest(new
            {
                ok = false,
                errors = create.Errors.Select(e => new { e.Code, e.Description })
            });
        }

        return Ok(new { ok = true, userId = user.Id, email = user.Email });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var service = await ResolveServiceWithTimeout<IUserStore<ElCaminoIdentityUser>>(HttpContext.RequestServices, "Resolve IUserStore", null);
        if (!service.ok || service.value is null)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { ok = false, message = "UserStore resolution timed out." });
        }

        var userStore = service.value;
        if (userStore is not IUserPasswordStore<ElCaminoIdentityUser> passwordStore)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, message = "Configured user store does not support passwords." });
        }

        var normalizedName = Normalizer.NormalizeName(req.Email);
        var user = await userStore.FindByNameAsync(normalizedName, HttpContext.RequestAborted);
        if (user is null)
        {
            return Unauthorized(new { ok = false, message = "Invalid credentials." });
        }

        var passwordHash = await passwordStore.GetPasswordHashAsync(user, HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Unauthorized(new { ok = false, message = "Invalid credentials." });
        }

        var hasher = new PasswordHasher<ElCaminoIdentityUser>();
        var verified = hasher.VerifyHashedPassword(user, passwordHash, req.Password);
        if (verified == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { ok = false, message = "Invalid credentials." });
        }

        return Ok(new { ok = true, userId = user.Id, email = user.Email });
    }

    private static async Task<(bool ok, T? value)> ResolveServiceWithTimeout<T>(IServiceProvider services, string stepName, List<object>? steps)
        where T : class
    {
        var sw = Stopwatch.StartNew();
        var resolveTask = Task.Run(() => services.GetService<T>());
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
        var completed = await Task.WhenAny(resolveTask, timeoutTask);
        sw.Stop();

        if (completed == timeoutTask)
        {
            steps?.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = "timeout" });
            return (false, null);
        }

        var value = await resolveTask;
        var ok = value is not null;
        steps?.Add(new { step = stepName, ok, elapsedMs = sw.ElapsedMilliseconds, detail = ok ? "ok" : "null" });
        return (ok, value);
    }

    private static async Task<(bool ok, T? value)> RunWithTimeout<T>(
        string stepName,
        Func<CancellationToken, Task<T>> action,
        List<object> steps)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
        var sw = Stopwatch.StartNew();

        try
        {
            var task = action(cts.Token);
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(DefaultTimeoutSeconds), CancellationToken.None));
            sw.Stop();

            if (completed != task)
            {
                steps.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = "timeout" });
                return (false, default);
            }

            var value = await task;
            steps.Add(new { step = stepName, ok = true, elapsedMs = sw.ElapsedMilliseconds, detail = "ok" });
            return (true, value);
        }
        catch (Exception ex)
        {
            sw.Stop();
            steps.Add(new { step = stepName, ok = false, elapsedMs = sw.ElapsedMilliseconds, detail = ex.GetType().Name + ": " + ex.Message });
            return (false, default);
        }
    }
}

public sealed class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
