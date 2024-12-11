using System;
using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;

namespace IBeam.DataModels
{
    [Alias("RefreshToken")]
    public class RefreshTokenDTO : IDTO
    {

        public Guid Id { get; set; }
  
        public Guid AccountId { get; set; }
     
        public string Token { get; set; }
      
        public DateTime Expires { get; set; }
        public bool IsDeleted { get; set; }

    }
}