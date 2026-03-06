namespace IBeam.Services.Abstractions
{
    public enum ServiceOperation
    {
        GetById,
        GetByIds,
        GetAll,
        GetAllWithArchived,
        Save,
        SaveAll,
        Archive,
        Unarchive,
        Delete
    }
}
