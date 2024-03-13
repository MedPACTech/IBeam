using System.Threading.Tasks;

namespace IBeam.Services.Messaging
{
    public interface ISendGridService
    {
        Task SendEmailAsync(string fromEmail, string toEmail, string subject, string plainTextContent, string htmlContent);
    }
}