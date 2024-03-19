namespace IBeam.Utilities
{
    public interface IAppSettings
    {
        string APIDomain { get; set; }
        string ConnectionString { get; set; }
        string DatabaseType { get; set; }
        string Dev2FACode { get; set; }
        string EmailLogicAppURL { get; set; }
        bool EnableDevMode { get; set; }
        string Secret { get; set; }
        string SendGridAPIKey { get; set; }
        string SendGridSenderEmail { get; set; }
        string SendGridSenderName { get; set; }
        string SiteBaseURL { get; set; }
        string TwilioAuthToken { get; set; }
        string TwilioPhoneNumber { get; set; }
        string TwilioSID { get; set; }
    }
}