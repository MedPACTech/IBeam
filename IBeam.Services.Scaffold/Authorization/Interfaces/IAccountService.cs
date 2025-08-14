using System;
using System.Collections.Generic;
using IBeam.Scaffolding.Models.API;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Authorization
{
	public interface IAccountService
	{
        AuthenticateResponse Authenticate(AuthenticateRequest request);
        FinishAuthenticateResponse FinishAuthenticate(FinishAuthenticateRequest request);
        bool ResendTwoFactorCode(string token);
        FinishAuthenticateResponse RefreshToken(string token);
        void RevokeToken(string token);
        RegisterResponse Register(RegisterRequest request);
        bool ChangePassword(PasswordChangeRequest request);
        void RequestPasswordReset(RequestPasswordResetRequest request);
        bool ResetPassword(ResetPasswordRequest request);
        IAccount Fetch(Guid id);
        List<IAccount> FetchAll();
        List<IAccount> FetchArchived();
        List<IAccount> FetchByCustomer(Guid customerId);
        bool Update(AccountUpdateRequest req);
        IAccountContext FetchContextByAccount(Guid AccountId);
        Dictionary<Guid, string> GetRolesForContext(Guid id);
    }
}
