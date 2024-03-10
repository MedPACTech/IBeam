using System;

namespace IBeam.Models.API
{
    public class RegisterRequest
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string CountryCode { get; set; }
        public string MobilePhone { get; set; }
        public Guid AssociatedCompanyId { get; set; }
    }
}