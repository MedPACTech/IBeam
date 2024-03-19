using IBeam.Services;
using IBeam.Services.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using IBeam.Utilities;

namespace IBeam.Services.Messaging
{
    public class SendGridService : ISendGridService
    {
        private readonly AppSettings _appSettings;
        private readonly SendGridClient _client;

        public SendGridService(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
            _client = new SendGridClient(_appSettings.SendGridAPIKey);
        }

        public async Task SendEmailAsync(string fromEmail, string toEmail, string subject, string plainTextContent, string htmlContent)
        {
            var from = new EmailAddress(fromEmail);
            var to = new EmailAddress(toEmail);
            var message = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var response = await _client.SendEmailAsync(message);
        }

    }
}