using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.Models.Interfaces;
using IBeam.Services.Messaging;
using IBeam.Utilities;

namespace IBeam.Services.Authorization
{
    public class TwoFactorService : ITwoFactorService
    {
        public static readonly TimeSpan TWO_FACTOR_CODE_TIMEOUT = TimeSpan.FromMinutes(10);

        private readonly ITwilioService _twilioService;
        private readonly IEmailService _emailService;
        private readonly NamespacedMemoryCache _cache;

        private readonly string _dev2FACode;
        private readonly bool _enableDevMode;

        public TwoFactorService(
            ITwilioService twilioService,
            IEmailService emailService,
            IOptions<AppSettings> appSettings,
            IMemoryCache memoryCache
        )
        {
            _twilioService = twilioService;
            _emailService = emailService;
            _dev2FACode = appSettings.Value.Dev2FACode;
            _enableDevMode = appSettings.Value.EnableDevMode;
            _cache = new NamespacedMemoryCache(memoryCache, "2FA");
        }

        public string Begin2FA(IAccount Account)
        {
            var token = TokenGenerator.GenerateRandomToken(20);
            var code = TokenGenerator.GenerateTwoFactorCode();
            var number = Account.CountryCode + Account.MobilePhone;

            _cache.Set(
                token,
                new PendingAuth { Code = code, AccountId = Account.Id, MobilePhone = number },
                TWO_FACTOR_CODE_TIMEOUT
            );

            SendSMS(number, code);

            var sender = "no-reply@atp-cal.com";
            var subject = "Login Code";
            var content = $"Your login code is {code}";
            //_sendGridService.SendEmail(email, subject, content);
            _emailService.SendEmail(Account.Email, subject, content, sender);

            return token;
        }

        public bool Finish2FA(string token, string code, out Guid AccountId)
        {
            AccountId = Guid.Empty;

            var pendingAuth = GetPendingAuthentication(token);
            if (pendingAuth == null)
                return false;

            if (!(_enableDevMode && code == _dev2FACode))
            {
                if (code != pendingAuth.Code)
                    return false;
            }

            _cache.Remove(token);
            AccountId = pendingAuth.AccountId;

            return true;
        }

        public bool ResendCode(string token)
        {
            var pendingAuth = GetPendingAuthentication(token);
            if (pendingAuth == null)
                return false;

            // NOTE: Re-set the entry in the cache to update the expiration time.
            _cache.Set(token, pendingAuth, TWO_FACTOR_CODE_TIMEOUT);

            SendSMS(pendingAuth.MobilePhone, pendingAuth.Code);
            return true;
        }

        private PendingAuth GetPendingAuthentication(string token)
        {
            return (PendingAuth)_cache.Get(token);
        }

        private void SendSMS(string phoneNumber, string code)
        {
            _twilioService.SendSMS(phoneNumber, $"Your login code is {code}");
        }

        private class PendingAuth
        {
            public string Code { get; set; }
            public Guid AccountId { get; set; }
            public string MobilePhone { get; set; }
        }
    }
}