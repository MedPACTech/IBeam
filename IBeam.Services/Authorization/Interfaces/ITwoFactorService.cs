using System;
using IBeam.Models.Interfaces;

namespace IBeam.Services.Authorization
{ 
    public interface ITwoFactorService
    {
        string Begin2FA(IAccount Account);
        bool Finish2FA(string token, string code, out Guid AccountId);
        bool ResendCode(string token);
    }
}