namespace IBeam.Services.Abstractions
{
    /// <summary>
    /// Maps between repository entities and transport models.
    /// Implement with AutoMapper, Mapster, manual mapping, etc.
    /// </summary>
    public interface IModelMapper<TEntity, TModel>
        where TEntity : class
        where TModel : class
    {
        TEntity ToEntity(TModel model);
        TModel ToModel(TEntity entity);

        IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models);
        IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities);
    }
}
