using System;

namespace IBeam.Scaffolding.Models.Interfaces
{
    public interface IRefreshToken : IUniqueId
    {
        public Guid AccountId { get; set; }
        public string Token { get; set; }
        public DateTime Expires { get; set; }
    }
}
