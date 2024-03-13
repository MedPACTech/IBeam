using System;
using ServiceStack.DataAnnotations;

namespace IBeam.DataModels
{
    [Alias("RefreshToken")]
    public class RefreshTokenDTO : IDTO
    {
        [System.ComponentModel.DataAnnotations.Required]
        public Guid Id { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public Guid AccountId { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public string Token { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public DateTime Expires { get; set; }
    }
}