using System;
using System.Text.Json.Serialization;
using IBeam.Scaffolding.Models.Interfaces;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{
    public class Account : IAccount
    {
        public Guid Id { get; set; }
        [JsonIgnore] public string PasswordHash { get; set; }
        public bool IsEmailConfirmed { get; set; }
        public bool IsPhoneConfirmed { get; set; }
        public bool IsPasswordReset { get; set; }
        public string CountryCode { get; set; }
        public string MobilePhone { get; set; }
        public string Email { get; set; }
        [JsonIgnore] public Guid EmailToken { get; set; }
        [JsonIgnore] public string PhoneToken { get; set; }
        public Guid LicenseAgreementId { get; set; }
        public DateTime DateAgreementSigned { get; set; }
        public Guid AssociatedCompanyId { get; set; }
        public bool IsArchived { get; set; }
    }
}