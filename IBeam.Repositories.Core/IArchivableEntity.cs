namespace IBeam.Repositories.Abstractions;

public interface IArchivableEntity
{
    bool IsArchived { get; set; }
}