namespace IBeam.Repositories.Abstractions;

public interface IEntity
{
    Guid Id { get; set; }
    bool IsDeleted { get; set; }
}
