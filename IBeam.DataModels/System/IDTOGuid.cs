using System;

namespace IBeam.DataModels
{ 
    public interface IDTOGuid : IDTO
    {
        Guid Id { get; set; }
    }
}