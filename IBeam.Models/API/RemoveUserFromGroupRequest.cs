using System;

namespace IBeam.Models.API
{
    public class RemoveAccountFromGroupRequest
    {
        public Guid AccountId { get; set; }
        public Guid AccountGroupId { get; set; }
    }
}