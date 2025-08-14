using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Services.Authorization
{ 
    public interface ITwoFactorService
    {
        string Begin2FA(IAccount Account);
        bool Finish2FA(string token, string code, out Guid AccountId);
        bool ResendCode(string token);
    }
}