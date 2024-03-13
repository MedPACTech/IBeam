using System;
using IBeam.Models.Interfaces;

namespace IBeam.Models
{
    public class RefreshToken : IRefreshToken
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string Token { get; set; }
        public DateTime Expires { get; set; }
    }
}
