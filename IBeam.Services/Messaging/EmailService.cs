using System.Net.Http;
using System.Text;
using System.Text.Json;
using IBeam.API.Utilities;
using Microsoft.Extensions.Options;
using IBeam.Services.Interfaces;
using MimeKit.Text;
using System.Net.Mail;
using System.Xml.Linq;
using System.Net;

namespace IBeam.Services.Messaging
{
    public class EmailService : IEmailService
    {
        private static readonly HttpClient HTTP_CLIENT = new HttpClient();
        private readonly ISendGridService _sendGridService;
        private readonly string url;
        
        public EmailService(ISendGridService sendGridService, IOptions<AppSettings> appSettings)
        {
            var conf = appSettings.Value; 
            url = conf.EmailLogicAppURL;
            _sendGridService = sendGridService;
        }

        public void SendEmail(string recipient, string subject, string body, string sender)
        {
            //var req = new Request
            //{
            //    Email = recipient,
            //    Subject = subject,
            //    Body = body
            //};

            _sendGridService.SendEmailAsync(sender, recipient, subject, body, body);

            //HTTP_CLIENT.PostAsync(url, new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")).Wait();
        }

        public void Send(string to, string subject, string html, string from = "")
        {

            MailMessage mailMessage = new MailMessage(from, to, subject, html);
            mailMessage.IsBodyHtml= true;

            var smtp = new SmtpClient("smtp.office365.com", 587);
            smtp.UseDefaultCredentials = false;
            smtp.Credentials = new NetworkCredential("abram.cookson@medpactech.com", "mpxszkpxcvhsytzv");
            smtp.EnableSsl= true;
           // smtp.StartTLS = true;
           // smtp.Port = 587; //todo: from config
           // smtp.Host = "smtp.office365.com"; //todo: from config
            smtp.Send(mailMessage);
          
        }

        //public void sendEmail()
        //{
        //    String userName = "from@domain.com";
        //    String password = "password for from address";
        //    MailMessage msg = new MailMessage("from@domain.com ", " to@domain.com ");
        //    msg.Subject = "Your Subject Name";
        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine("Name: " + txtname.Text);
        //    sb.AppendLine("Mobile Number: " + txtmbno.Text);
        //    sb.AppendLine("Email:" + txtemail.Text);
        //    sb.AppendLine("Drop Downlist Name:" + ddllinksource.SelectedValue.ToString());
        //    msg.Body = sb.ToString();
        //    Attachment attach = new Attachment(Server.MapPath("folder/" + ImgName));
        //    msg.Attachments.Add(attach);
        //    SmtpClient SmtpClient = new SmtpClient();
        //    SmtpClient.Credentials = new System.Net.NetworkCredential(userName, password);
        //    SmtpClient.Host = "smtp.office365.com";
        //    SmtpClient.Port = 587;
        //    SmtpClient.EnableSsl = true;
        //    SmtpClient.Send(objMailMessage);
        //}

        private class Request
        {
            public string Email { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }
        }
    }
}