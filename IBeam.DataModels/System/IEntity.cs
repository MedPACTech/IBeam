namespace IBeam.DataModels.System
{
    public interface IEntity
    {
        Guid Id { get; set; }
        bool IsDeleted { get; set; }
    }
}