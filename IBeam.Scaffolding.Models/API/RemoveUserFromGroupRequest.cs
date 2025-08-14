using System;

namespace IBeam.Scaffolding.Models.API
{
    public class RemoveAccountFromGroupRequest
    {
        public Guid AccountId { get; set; }
        public Guid AccountGroupId { get; set; }
    }
}