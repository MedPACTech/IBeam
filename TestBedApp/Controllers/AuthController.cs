using ElCamino.AspNetCore.Identity.AzureTable.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace TestBedApp.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser> _userManager;

    public AuthController(UserManager<ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var user = new ElCamino.AspNetCore.Identity.AzureTable.Model.IdentityUser
        {
            UserName = req.Email,
            Email = req.Email
        };
        var result = await _userManager.CreateAsync(user, req.Password);
        return Ok(result);
    }

    [HttpGet("by-email")]
    public async Task<IActionResult> GetByEmail([FromQuery] string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        return Ok(user);
    }
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

//**How to use:**
//- `POST /api/auth/register` with `{ "email": "user@example.com", "password": "yourpassword" }` to create a user.
//- `GET /api/auth/by-email?email=user@example.com` to fetch a user.

//This will confirm ElCamino and AzureTable Identity are working in your testbed. Let me know if you want login or password reset endpoints!