using System;
using System.Text.Json.Serialization;

namespace IBeam.Models.Interfaces
{
	public interface IAccount
	{
		 Guid Id { get; set; }
		 [JsonIgnore] string PasswordHash { get; set; }
		 bool IsEmailConfirmed { get; set; }
		 bool IsPhoneConfirmed { get; set; }
         bool IsPasswordReset { get; set; }
        string CountryCode { get; set; }
		 string MobilePhone { get; set; }
		 string Email { get; set; }
		 [JsonIgnore] Guid EmailToken { get; set; }
		 [JsonIgnore] string PhoneToken { get; set; }
		 Guid LicenseAgreementId { get; set; }
		 DateTime DateAgreementSigned { get; set; }
		public Guid AssociatedCompanyId { get; set; }
		bool IsArchived { get; set; }
	}
}
