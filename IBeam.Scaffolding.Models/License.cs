using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{
    public class License : ILicense
    {
        public Guid Id { get; set; }
        public Guid ApplicationId { get; set; }
        public string LicenseData { get; set; }
        public DateTime DateActive { get; set; }

    }
}
