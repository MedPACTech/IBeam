using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;
using System;

namespace IBeam.Scaffolding.DataModels
{
    [Serializable]
	[Alias("Accounts")]
	public class AccountDTO : IEntity, IArchivableEntity
	{
		public Guid Id { get; set; }
		public string PasswordHash { get; set; }
		public bool IsEmailConfirmed { get; set; }
		public bool IsPhoneConfirmed { get; set; }
        public bool IsPasswordReset { get; set; }
        public string CountryCode { get; set; }
		public string MobilePhone { get; set; }
		public string Email { get; set; }
		public Guid EmailToken { get; set; }
		public string PhoneToken { get; set; }
		public Guid LicenseAgreementId { get; set; }
		public DateTime? DateAgreementSigned { get; set; }
		public Guid AssociatedCompanyId { get; set; } //TennantID?
		public bool IsArchived { get; set; }
        public bool IsDeleted { get; set; }
    }
}
