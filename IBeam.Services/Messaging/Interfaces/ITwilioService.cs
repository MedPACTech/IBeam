namespace IBeam.Services.Messaging
{
    public interface ITwilioService
    {
        public void SendSMS(string to, string message);
    }
}