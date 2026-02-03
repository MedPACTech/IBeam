namespace IBeam.DataModels.System
{
    public interface IDeletableEntity
    {
        bool IsDeleted { get; set; }
    }
}