using IBeam.Services.Abstractions;

public sealed class AutoMapperModelMapper<TEntity, TModel> : IModelMapper<TEntity, TModel>
    where TEntity : class
    where TModel : class
{
    private readonly AutoMapper.IMapper _mapper;

    public AutoMapperModelMapper(AutoMapper.IMapper mapper)
        => _mapper = mapper;

    public TEntity ToEntity(TModel model) => _mapper.Map<TEntity>(model);
    public TModel ToModel(TEntity entity) => _mapper.Map<TModel>(entity);

    public IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models)
        => models.Select(ToEntity);

    public IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities)
        => entities.Select(ToModel);
}
