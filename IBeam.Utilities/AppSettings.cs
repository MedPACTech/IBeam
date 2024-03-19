namespace IBeam.Utilities
{
    public class AppSettings : IAppSettings
    {
        public string Secret { get; set; }
        public string SiteBaseURL { get; set; }
        public string APIDomain { get; set; }

        public string TwilioSID { get; set; }
        public string TwilioAuthToken { get; set; }
        public string TwilioPhoneNumber { get; set; }

        public string EmailLogicAppURL { get; set; }

        public string SendGridAPIKey { get; set; }
        public string SendGridSenderEmail { get; set; }
        public string SendGridSenderName { get; set; }

        public string DatabaseType { get; set; }
        public string ConnectionString { get; set; }

        public string Dev2FACode { get; set; }
        public bool EnableDevMode { get; set; }
    }
}