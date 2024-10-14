using IBeam.Services;
using Microsoft.Extensions.Options;
using IBeam.Services.Interfaces;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using IBeam.Utilities;

namespace IBeam.Services.Messaging
{
    public class TwilioService : ITwilioService
    {
        private readonly BaseAppSettings _appSettings;

        public TwilioService(IOptions<BaseAppSettings> appSettings)
        {
            _appSettings = appSettings.Value;

            TwilioClient.Init(_appSettings.TwilioSID, _appSettings.TwilioAuthToken);
        }

        public void SendSMS(string to, string message)
        {
            to = string.Format("+{0}", to);
            MessageResource.Create(
                new PhoneNumber(to),
                from: new PhoneNumber(_appSettings.TwilioPhoneNumber),
                body: message
            );
        }
    }
}