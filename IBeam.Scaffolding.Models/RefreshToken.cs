using System;
using IBeam.Scaffolding.Models.Interfaces;

namespace IBeam.Scaffolding.Models
{
    public class RefreshToken : IRefreshToken
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Token { get; set; }
        public DateTime Expires { get; set; }
    }
}
