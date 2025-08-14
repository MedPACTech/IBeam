using System.Security.Claims;
using System.Text;
using AutoMapper;
using IBeam.Scaffolding.DataModels;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Services.Messaging;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using IBeam.Utilities;
using Microsoft.Extensions.Caching.Memory;
using IBeam.Scaffolding.Repositories.Interfaces;
using BCrypt.Net;
using IBeam.Scaffolding.Models.API;
using IBeam.Scaffolding.Models;
using IBeam.Scaffolding.DataModels;

namespace IBeam.Scaffolding.Services.Authorization
{
    public class AccountService : IAccountService
    {
        // TODO: Move these constants into a config file (or don't?)
        private const int REFRESH_TOKEN_LENGTH = 64;
        private static readonly TimeSpan REFRESH_TOKEN_TIMEOUT = TimeSpan.FromDays(30);
        private static readonly TimeSpan AUTH_TOKEN_TIMEOUT = TimeSpan.FromHours(1);
        private static readonly TimeSpan PASSWORD_RESET_TOKEN_TIMEOUT = TimeSpan.FromHours(8);
        
        private readonly IMapper _mapper;
        private readonly BaseAppSettings _appSettings;
        private readonly IAccountRepository _repository;
        private readonly ITwoFactorService _twoFactorService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        //private readonly ISendGridService _sendGridService;
        private readonly NamespacedMemoryCache _passwordResetMemoryCache;
        private readonly IEmailService _emailService;
        private readonly IAccountGroupMemberService _AccountGroupMemberService;
        private readonly IAccountRoleService _AccountRoleService;
        private readonly IAccountGroupRoleService _AccountGroupRoleService;
        private readonly IAccountContextService _AccountContextService;

        public AccountService(
            IMapper mapper,
            IOptions<BaseAppSettings> appSettings, 
            IAccountRepository repository, 
            ITwoFactorService twoFactorService, 
            IRefreshTokenRepository refreshTokenRepository, 
            //ISendGridService sendGridService, 
            IMemoryCache memoryCache,
            IEmailService emailService,
            IAccountGroupMemberService AccountGroupMemberService,
            IAccountRoleService AccountRoleService,
            IAccountGroupRoleService AccountGroupRoleService,
            IAccountContextService AccountContextService
        ) {
            _mapper = mapper;
            _appSettings = appSettings.Value;
            _repository = repository;
            _twoFactorService = twoFactorService;
            _refreshTokenRepository = refreshTokenRepository;
            //_sendGridService = sendGridService;
            _passwordResetMemoryCache = new NamespacedMemoryCache(memoryCache, "pw_reset");
            _emailService = emailService;
            _AccountGroupMemberService = AccountGroupMemberService;
            _AccountRoleService = AccountRoleService;
            _AccountGroupRoleService = AccountGroupRoleService;
            _AccountContextService = AccountContextService;
        }

        public AuthenticateResponse Authenticate(AuthenticateRequest request)
        {
            // NOTE: First, look up the Account associated with the email entered
            // on the login form; if we have no record of it, return a failure.
            var AccountDTO = _repository.GetByEmail(request.Email);
            if (AccountDTO == null || AccountDTO.IsArchived)
            {
                return new AuthenticateResponse()
                {
                    Success = false,
                    Token = null
                };
            }

            // NOTE: Next, let's check if the (hash of the) password entered on the
            // login form matches up with the password hash we have stored for this
            // email address, and return a failure if it does not.
            var Account = _mapper.Map<Account>(AccountDTO);
            if (!BCrypt.Net.BCrypt.EnhancedVerify(request.Password, Account.PasswordHash))
            {
                return new AuthenticateResponse()
                {
                    Success = false,
                    Token = null
                };
            }

            // NOTE: Finally, if the Account email and password are valid, ask the 2FA
            // service to send the Account a code so we can verify them for login, and
            // return a successful response including a code that must be sent
            // along with the 2FA code entered by the Account, in order to associate
            // the code they entered with what we expect it to be, so we can check it.
            // TODO: support the non-2FA path (also maybe other forms of 2FA)
            var token = _twoFactorService.Begin2FA(Account);

            return new AuthenticateResponse
            { 
                Success = true,
                Token = token,
                Id = Account.Id
            };
        }

        public FinishAuthenticateResponse FinishAuthenticate(FinishAuthenticateRequest request)
        {
            // NOTE: First, check with the two-factor service if the Account submitted the correct
            // 2FA code; if not, return a failure, otherwise, we get their AccountId as a result.
            if (!_twoFactorService.Finish2FA(request.Token, request.Code, out Guid AccountId))
            {
                return new FinishAuthenticateResponse
                {
                    Success = false
                };
            }
            
            // NOTE: If the code the Account entered is correct, we should be able
            // to look up the actual Account for this Account. If that lookup fails,
            // in this case, throw an exception rather than return an error; if
            // this ever happens, it is a bug. If looking up the Account by its ID
            // were going to fail, then it should have failed _before we ever got here_.
            var AccountDTO = _repository.GetById(AccountId);
            if (AccountDTO == null)
            {
                throw new Exception("AccountDTO disappeared!");
            }

            // NOTE: If we got this far, the Account was able to log in and
            // complete 2FA successfully! Generate a new auth token for them,
            // along with a refresh token as a cookie so they can maintain
            // access without having to re-log-in every time the auth token
            // expires. Also save the refresh token in the DB, associated with
            // the Accounts Account ID, for book-keeping purposes.
            var Account = _mapper.Map<Account>(AccountDTO);
            var authToken = GenerateJWT(Account);

            var refreshToken = GenerateRefreshToken(Account.Id);
            _refreshTokenRepository.Save(_mapper.Map<RefreshTokenDTO>(refreshToken));

            return new FinishAuthenticateResponse()
            {
                Success = true,
                AuthToken = authToken,
                RefreshToken = refreshToken,
                Account = Account
            };
        }

        public bool ResendTwoFactorCode(string token)
        {
            return _twoFactorService.ResendCode(token);
        }

        public FinishAuthenticateResponse RefreshToken(string token)
        {
            // NOTE: Look up the refresh token that was used and
            // return a failure if we don't have a record of it.
            var refreshTokenDTO = _refreshTokenRepository.GetByToken(token);
            if (refreshTokenDTO == null)
            {
                return new FinishAuthenticateResponse
                {
                    Success = false
                };
            }

            var refreshToken = _mapper.Map<RefreshToken>(refreshTokenDTO);

            var AccountDTO = _repository.GetById(refreshToken.AccountId);
            if (AccountDTO == null)
            {
                throw new Exception("RefreshToken points to nonexistent Account");
            }

            var Account = _mapper.Map<Account>(AccountDTO);

            // NOTE: If the refresh token is expired, clear it from the DB and return a failure.
            if (DateTime.UtcNow >= refreshToken.Expires)
            {
                _refreshTokenRepository.Delete(refreshTokenDTO);

                return new FinishAuthenticateResponse
                {
                    Success = false
                };
            }
            
            // NOTE: If the refresh token is not expired yet, the request to refresh it is
            // allowed to succeed by generating a brand new refresh token, along with
            // a new auth token. We remove the old refresh token from the DB and add the
            // new one, and return the new tokens in a successful result.
            var authToken = GenerateJWT(Account);
            var newRefreshToken = GenerateRefreshToken(Account.Id);
            var newRefreshTokenDTO = _mapper.Map<RefreshTokenDTO>(newRefreshToken);

            _refreshTokenRepository.Delete(refreshTokenDTO);
            _refreshTokenRepository.Save(newRefreshTokenDTO);

            return new FinishAuthenticateResponse
            {
                Success = true,
                AuthToken = authToken,
                RefreshToken = newRefreshToken,
                Account = Account
            };
        }

        public void RevokeToken(string token)
        {
            // NOTE: First of all, look up the token that was
            // requested to be revoked; if we have no record of it,
            // simply ignore the request.
            var refreshTokenDTO = _refreshTokenRepository.GetByToken(token);

            if (refreshTokenDTO == null)
                return;

            // NOTE: Otherwise, revoke the token by deleting it.
            _refreshTokenRepository.Delete(refreshTokenDTO);
        }

        // NOTE: This will eventually be include a referral based registration system
        public RegisterResponse Register(RegisterRequest request)
        {
            // TODO: validate phone number

            //Validate Data
            

            // NOTE: First, look up the email that was submitted; if we
            // already have an Account associated with it, deny the request and send already registered email.
            var AccountDTO = _repository.GetByEmail(request.Email);
            if (AccountDTO != null)
            {
                SendAlreadyRegisteredEmail(request.Email, "");

                return new RegisterResponse
                {
                    Success = true,
                };
            }

            

            var id = request.Id;
            
            var newAccount = new Account
            {
                Id = id,
                Email = request.Email,
                PasswordHash = "",
                AssociatedCompanyId = request.AssociatedCompanyId,
                CountryCode = request.CountryCode,
                MobilePhone = request.MobilePhone
            };

            //var AccountContext = new AccountContext();
            //AccountContext.AccountId = id;
            //_AccountContextService.Save(AccountContext);
 
            if (request.AssociatedCompanyId != Guid.Parse("5A3050CB-9905-4D4D-8768-72A990AE0FFE"))
            {
                var AccountGroupMember = new AccountGroupMember();
                AccountGroupMember.Id = Guid.NewGuid();
                AccountGroupMember.AccountGroupId = Guid.Parse("EE291225-0C83-4246-BAEE-7DDD3B94496C");
                AccountGroupMember.AccountName = request.Email;
                AccountGroupMember.DisplayName = request.Email;
            }

            // NOTE: Next, save the newly-registered Account and return success,
            // along with the Account ID.
            var newAccountDTO = _mapper.Map<AccountDTO>(newAccount);

            //signed automatically for now
            newAccountDTO.DateAgreementSigned = DateTime.Now;

            _repository.Save(newAccountDTO);

            // NOTE: Finally, send the Account an email to reset their password.
            RequestPasswordReset(new RequestPasswordResetRequest
            {
                Email = request.Email
            });
            
            return new RegisterResponse
            {
                Success = true,
                Id = newAccount.Id
            };
        }

        private void SendAlreadyRegisteredEmail(string email, string origin)
        {
            string message;
            if (!string.IsNullOrEmpty(origin))
                message = $@"<p>If you don't know your password please visit the <a href=""{origin}/account/forgot-password"">forgot password</a> page.</p>";
            else
                message = "<p>If you don't know your password you can reset it via the <code>/accounts/forgot-password</code> api route.</p>";

            _emailService.Send(email, "Sign-up Verification - Email Already Registered", $@"<h4>Email Already Registered</h4>
                        <p>Your email <strong>{email}</strong> is already registered.</p> {message}", "abram.cookson@medpactech.com");

        }

        public bool ChangePassword(PasswordChangeRequest request)
        {
            // NOTE: First, look up the Account by the ID in the request;
            // if we have no such record, return a failure.
            var AccountDTO = _repository.GetById(request.Id);
            if (AccountDTO == null)
            {
                return false;
            }
            
            var Account = _mapper.Map<Account>(AccountDTO);

            // NOTE: Next, make sure that the (hash of the) password entered
            // matches what we have stored in the DB; if not, return a failure.
            if (!BCrypt.Net.BCrypt.EnhancedVerify(request.CurrentPassword, Account.PasswordHash))
            {
                return false;
            }

            // NOTE: Finally, after verifying the password, update the Account
            // with the new password, hashed (with salt). Save the Account, and
            // return a success.
            Account.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(request.NewPassword);
            AccountDTO = _mapper.Map<AccountDTO>(Account);
            _repository.Save(AccountDTO);
            return true;
        }

        public void RequestPasswordReset(RequestPasswordResetRequest request)
        {
            // NOTE: First, look up the email the Account requested
            // to reset the password for; if we have no such record,
            // simply ignore this request. (In theory, ignoring potentially
            // malicious requests is an ideal strategy, as long as it's not
            // revealing any information to the potential attacker that
            // they wouldn't otherwise have.)
            var email = request.Email;
            var AccountDTO = _repository.GetByEmail(email);
            if (AccountDTO == null)
            {
                return;
            }

            // NOTE: If we have an Account associated with the email,
            // generate a token to be used in a 'magic URL' sent
            // to that email address, so the Account can verify they
            // own the email address, and reset their password.
            // Save this token, associated with the Account ID
            // associated with this email address, for use later.
            var token = TokenGenerator.GenerateRandomToken(16);
            _passwordResetMemoryCache.Set(token, AccountDTO.Id, PASSWORD_RESET_TOKEN_TIMEOUT);

            // NOTE: Finally, send the actual email with a link taking
            // the Account to our password reset page, passing the token
            // in the URL so we can verify the request.
            // TODO: pretty the email up; this is just a placeholder
            var sender = "no-reply@medpactech.com";
            var subject = "Set Pass";
            var content = $"Please click here to set your pass: <a href=\"{_appSettings.SiteBaseURL}/#/password-reset/{token}\">set password</a> . This link will expire 8 hours after being received.";
            //_sendGridService.SendEmail(email, subject, content);
            _emailService.SendEmail(email, subject, content, sender);
        }

        public bool ResetPassword(ResetPasswordRequest request)
        {
            // NOTE: First, check if we have any record of the token passed in this request.
            // If we don't, return a failure. Also check if the Account ID associated with
            // that token is empty, and if it is, return a failure as well. (I can't remember
            // why we're doing this part off the top of my head; must've been a reason.)
            if (!_passwordResetMemoryCache.TryGetValue(request.Token, out Guid AccountId)) return false;
            if (AccountId == Guid.Empty) return false;

            // NOTE: If we got a valid password reset token, we can now remove it from the DB.

            var AccountDTO = _repository.GetById(AccountId);
            var Account = _mapper.Map<Account>(AccountDTO);
            
            // NOTE: Finally, update the Account associated with this password reset request token
            // with the new password, hashed (with salt), store it back to the DB, and return success.
            Account.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(request.NewPassword);
            AccountDTO = _mapper.Map<AccountDTO>(Account);
            _repository.Save(AccountDTO);
            _passwordResetMemoryCache.Remove(request.Token);

            return true;
        }

        public IAccount Fetch(Guid id)
        {
            if (id == Guid.Empty)
                return new Account();

            var AccountDTO = _repository.GetById(id);
            return _mapper.Map<Account>(AccountDTO);
        }

        public List<IAccount> FetchAll()
        {
            return _repository.GetAll().Select(dto => (IAccount)_mapper.Map<Account>(dto)).ToList();
        }

        public List<IAccount> FetchArchived()
        {
            return _repository.GetArchived().Select(dto => (IAccount)_mapper.Map<Account>(dto)).ToList();
        }

        public List<IAccount> FetchByCustomer(Guid customerId)
        {
            return FetchAll().Where(Account => 
            {         
                return Account.AssociatedCompanyId == customerId;
            }).ToList();
        }

        public bool Update(AccountUpdateRequest req)
        {
            var Account = Fetch(req.Id);
            if (Account == null)
                return false;

            Account.Email = req.Email;
            Account.CountryCode = req.CountryCode;
            Account.MobilePhone = req.MobilePhone;
            Account.IsArchived = req.IsArchived;

            if (Account.LicenseAgreementId != req.LicenseAgreementId && req.LicenseAgreementId != Guid.Empty)
            {
                Account.LicenseAgreementId = req.LicenseAgreementId;
                Account.DateAgreementSigned = DateTime.Now;
            }

            if (!string.IsNullOrEmpty(req.Password)) 
                Account.PasswordHash = BCrypt.Net.BCrypt.EnhancedHashPassword(req.Password);


            var AccountDTO = _mapper.Map<AccountDTO>(Account);
            _repository.Save(AccountDTO);
            
            return true;
        }

        public Dictionary<Guid, string> GetRolesForContext(Guid id)
        {
            var Account = Fetch(id);
            var AccountRoles = GetAccountRoles(Account);
            var AccountGroupRoles = GetGroupRoles(Account);
            var roles = AccountRoles.Concat(AccountGroupRoles).GroupBy(d => d.Key).ToDictionary(d => d.Key, d => d.First().Value);
            return roles;
        }

        private static RefreshToken GenerateRefreshToken(Guid AccountId)
        {
            return new RefreshToken
            {
                Id = Guid.NewGuid(),
                AccountId = AccountId,
                Token = TokenGenerator.GenerateRandomToken(REFRESH_TOKEN_LENGTH),
                Expires = DateTime.UtcNow.Add(REFRESH_TOKEN_TIMEOUT)
            };
        }

        private string GenerateJWT(IAccount Account)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(GenerateClaims(Account)),
                Expires = DateTime.UtcNow.Add(AUTH_TOKEN_TIMEOUT),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private IEnumerable<Claim> GenerateClaims(IAccount Account)
        {
            var claims = new List<Claim>();
            var AccountRoles = GetAccountRoles(Account);
            var AccountGroupRoles = GetGroupRoles(Account);
            var roles = AccountRoles.Concat(AccountGroupRoles).GroupBy(d => d.Key).ToDictionary(d => d.Key, d => d.First().Value);

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Value));
                claims.Add(new Claim("RoleId", role.Key.ToString()));
            }

            claims.Add(new Claim("AccountId", Account.Id.ToString()));

            CheckAccountContext(Account, roles);

            return claims;
        }

        private Dictionary<Guid, string> GetAccountRoles(IAccount Account)
        {
            var AccountRoles = _AccountRoleService.FetchByAccount(Account.Id)
                .ToDictionary(t => t.ApplicationRoleId, t => t.ApplicationRoleName);
            return AccountRoles;
        }

        private Dictionary<Guid, string> GetGroupRoles(IAccount Account)
        {
            var AccountGroups = _AccountGroupMemberService.FetchByAccount(Account.Id);
            var AccountGroupIds = AccountGroups.Select(x => x.AccountGroupId);
            var groupRoles = _AccountGroupRoleService.FetchByAccountGroups(AccountGroupIds)
                .ToDictionary(t => t.ApplicationRoleId, t => t.ApplicationRoleName); 

            return groupRoles;
        }

        private void CheckAccountContext(IAccount Account, Dictionary<Guid, string> roles)
        {
            var existingAccountContext = FetchContextByAccount(Account.Id);

            if (existingAccountContext == null)
            {
                var AccountContext = _AccountContextService.FetchByAccount(Guid.Empty);
                AccountContext.Id = Guid.NewGuid();
                AccountContext.AccountId = Account.Id;
                AccountContext.RoleIds = roles.Keys;
                AccountContext.Roles = roles.Values;
                _AccountContextService.Save(AccountContext);
            }
        }

        public IAccountContext FetchContextByAccount(Guid AccountId)
        {
            if (AccountId == Guid.Empty)
                return new AccountContext();
            else
            {
                var AccountContext = _AccountContextService.FetchByAccount(AccountId);
                var roles = GetRolesForContext(AccountId);
                AccountContext.RoleIds = roles.Keys;
                AccountContext.Roles = roles.Values;
                return AccountContext;
            }
        }
    }
}