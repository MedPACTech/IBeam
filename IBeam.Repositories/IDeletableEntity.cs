namespace IBeam.Repositories.Abstractions;

public interface IDeletableEntity
{
    bool IsDeleted { get; set; }
}