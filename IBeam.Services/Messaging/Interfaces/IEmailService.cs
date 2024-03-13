namespace IBeam.Services.Messaging
{
    public interface IEmailService
    {
        void Send(string to, string subject, string html, string from = "");
        void SendEmail(string recipient, string subject, string body, string sender);
    }
}