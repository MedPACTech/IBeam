using System;

namespace IBeam.DataModels
{ 
    public interface IDTOArchive : IDTO
    {
        bool IsArchived { get; set; }
    }
}