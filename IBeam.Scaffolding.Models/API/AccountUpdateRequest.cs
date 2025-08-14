using System;

namespace IBeam.Scaffolding.Models.API
{
    public class AccountUpdateRequest
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string CountryCode { get; set; }
        public string MobilePhone { get; set; }
        public Guid LicenseAgreementId { get; set; }
        public string Password { get; set; }
        public bool IsArchived { get; set; }
    }
}