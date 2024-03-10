using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using IBeam.Models;
using IBeam.Models.API;
using IBeam.Models.Interfaces;
using IBeam.Services.Authorization;
using IBeam.API.Utilities;

namespace IBeam.API.Controllers
{

	[ApiController]
	[Route("[controller]")]
	public class AccountController : ControllerBase
	{
        private const string REFRESH_TOKEN_COOKIE_KEY = "refreshToken";

        private readonly string _apiDomain;
        private readonly IAccountService _AccountService;

        public AccountController(IOptions<AppSettings> appSettings, IAccountService AccountService)
        {
            _apiDomain = appSettings.Value.APIDomain;
            _AccountService = AccountService;
        }

        [HttpPost("authenticate")]
        public ActionResult<AuthenticateResponse> Authenticate(AuthenticateRequest request)
        {
            var resp = _AccountService.Authenticate(request);

            if (!resp.Success)
                return BadRequest("Authentication Failed");

            return Ok(resp);
        }

        [HttpPost("finishAuthenticate")]
        public ActionResult<FinishAuthenticateResponse> FinishAuthenticate(FinishAuthenticateRequest request)
        {
            var resp = _AccountService.FinishAuthenticate(request);

            if (!resp.Success)
                return BadRequest("Authentication Failed");

            setRefreshTokenCookie(resp.RefreshToken);

            return Ok(resp);
        }

        [HttpPost("resendTwoFactorCode")]
        public ActionResult ResendTwoFactorCode(ResendTwoFactorCodeRequest req)
        {
            if (!_AccountService.ResendTwoFactorCode(req.Token))
                return BadRequest();

            return Ok();
        }

        [HttpPost("refreshToken")]
        public ActionResult<FinishAuthenticateResponse> RefreshToken()
        {
            var refreshToken = Request.Cookies[REFRESH_TOKEN_COOKIE_KEY];
            var resp = _AccountService.RefreshToken(refreshToken);

            if (!resp.Success)
            {
                deleteRefreshTokenCookie();
                return Unauthorized("Invalid token");
            }

            setRefreshTokenCookie(resp.RefreshToken);

            return Ok(resp);
        }

        [Authorize]
        [HttpPost("revokeToken")]
        public ActionResult RevokeToken()
        {
            var token = Request.Cookies[REFRESH_TOKEN_COOKIE_KEY];

            if (token == null)
                throw new Exception("RevokeToken called without a refresh token to revoke; should never happen");

            _AccountService.RevokeToken(token);

            deleteRefreshTokenCookie();

            return Ok();
        }

        [HttpPost("register")]
        public ActionResult<RegisterResponse> Register(RegisterRequest request)
        {
            var resp = _AccountService.Register(request);

            if (!resp.Success)
                return BadRequest(resp.ErrorMessage);

            return resp;
        }

        //[Authorize]
        [HttpPost("changePassword")]
        public ActionResult ChangePassword(PasswordChangeRequest request)
        {
            if (_AccountService.ChangePassword(request))
            {
                return Ok();
            }

            return BadRequest();
        }

        [HttpPost("requestPasswordReset")]
        public ActionResult RequestPasswordReset(RequestPasswordResetRequest request)
        {
            _AccountService.RequestPasswordReset(request);
            return Ok();
        }

        [HttpPost("resetPassword")]
        public ActionResult ResetPasswordRequest(ResetPasswordRequest request)
        {
            if (!_AccountService.ResetPassword(request))
            {
                return BadRequest();
            }

            return Ok();
        }

        //[Authorize(Roles = Role.ADMIN)]
        [HttpGet("{id}")]
        public ActionResult<Account> Get(Guid id)
        {
            var Account = _AccountService.Fetch(id);

            if (Account == null)
                return NotFound();

            return Ok(Account);
        }

        //[Authorize(Roles = Role.ADMIN)]
        [HttpGet]
        public ActionResult<List<IAccount>> GetAll()
        {
            return Ok(_AccountService.FetchAll());
        }

        [HttpGet("archived")]
        public ActionResult<List<IAccount>> GetArchived()
        {
            return Ok(_AccountService.FetchArchived());
        }

        [HttpGet("customer/{customerId}")]
        public ActionResult GetByCustomer(Guid customerId)
        {
            return Ok(_AccountService.FetchByCustomer(customerId));
        }

        [Authorize]
        [HttpPost]
        public ActionResult Post(AccountUpdateRequest req)
        {
            if (!_AccountService.Update(req))
                return BadRequest();
            return Ok();
        }

        /*
        [Authorize(Roles = Role.ADMIN)]
        [HttpPost("update")]
        public ActionResult Update(AccountUpdateRequest req)
        {
            if (req.AccountId != this.GetRequestAccountID())
            {
                var reqAcc = _AccountsService.Fetch(this.GetRequestAccountID());
                if (reqAcc == null) return Unauthorized(); // NOTE: Shouldn't happen.
                if (reqAcc.Role != Role.ADMIN) return Unauthorized();
            }

            if (!_AccountsService.Update(req))
                return BadRequest();

            return Ok();
        }

        [Authorize]
        [HttpPost("verifyUpdate")]
        public ActionResult VerifyAccountUpdate(AccountUpdateVerificationRequest req)
        {
            if (!_AccountsService.VerifyAccountUpdate(req))
                return BadRequest();

            return Ok();
        }
        */

        private void setRefreshTokenCookie(IRefreshToken refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                Domain = _apiDomain,
                HttpOnly = true, // NOTE: prevent javascript from reading this cookie
                Secure = true, // NOTE: only send this cookie over HTTPS
                Expires = refreshToken.Expires,
                SameSite = SameSiteMode.None
            };
            Response.Cookies.Append(REFRESH_TOKEN_COOKIE_KEY, refreshToken.Token, cookieOptions);
        }

        private void deleteRefreshTokenCookie()
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                Expires = DateTime.UtcNow.AddDays(-1)
            };
            Response.Cookies.Append(REFRESH_TOKEN_COOKIE_KEY, "", cookieOptions);
        }
    }
}
