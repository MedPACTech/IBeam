using System;
using IBeam.Models.Interfaces;

namespace IBeam.Models
{
    public class License : ILicense
    {
        public Guid Id { get; set; }
        public Guid ApplicationId { get; set; }
        public string LicenseData { get; set; }
        public DateTime DateActive { get; set; }

    }
}
