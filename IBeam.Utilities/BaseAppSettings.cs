namespace IBeam.Utilities
{
    public class BaseAppSettings : IBaseAppSettings
    {
        public string Secret { get; set; }

        public string TwilioSID { get; set; }
        public string TwilioAuthToken { get; set; }
        public string TwilioPhoneNumber { get; set; }

        public string EmailLogicAppURL { get; set; }

        public string SendGridAPIKey { get; set; }
        public string SendGridSenderEmail { get; set; }
        public string SendGridSenderName { get; set; }

        /// <summary>
        /// The BaseURL of the client site, used for generating links or refrenceing client side assets
        /// </summary>
        public string SiteBaseURL { get; set; }

        /// <summary>
        /// Set this to the domain of the API used for cookies and other domain specific settings requiring callbacks
        /// </summary>
        public string APIDomain { get; set; }

        /// <summary>
        /// The type of database being used, this is used to determine the correct database context to use
        /// string must be one of the following: MSSql, Postgres
        /// </summary>
        public string DatabaseType { get; set; }


        /// <summary>
        /// Default repository connection string
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Deletes will remove the record from the database vs setting a flag
        /// The Default Setting false which enables soft delete 
        /// </summary>
        public string DisableSoftDelete { get; set; }

        /// <summary>
        /// Allows for the use of a generic 2FA code for development purposes
        /// </summary>
        public string Dev2FACode { get; set; }

        /// <summary>
        /// Set this to true to enable development mode, this returns more detailed error messages to the client
        /// Allows for the use of a generic 2FA code for development purposes
        /// </summary>
        public bool EnableDevMode { get; set; }

        /// <summary>
        /// Set this to true to enable caching of repositories when GetAll is called. 
        /// This is best used when the data is not expected to change often such as reference data
        /// Default value is true
        /// </summary>
        public bool? EnableCache { get; set; }

        /// <summary>
        /// Set this to true to allow the repository to generate the Id for the entity
        /// Default value is false
        /// </summary>
        public bool? IdGeneratedByRepository { get; set; }
    }
}