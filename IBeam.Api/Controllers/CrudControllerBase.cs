using IBeam.Api.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class CrudControllerBase<TService, TEntity, TKey> : ApiControllerBase
    where TService : class
{
    protected readonly TService Service;

    protected virtual bool AllowGetAll => false;
    protected virtual bool AllowGetAllWithArchived => false;
    protected virtual bool AllowGetById => true;
    protected virtual bool AllowGetByIds => false;
    protected virtual bool AllowPost => false;
    protected virtual bool AllowPut => false;
    protected virtual bool AllowDelete => false;

    protected virtual bool ReturnCreatedOnPost => false;
    protected virtual string GetByIdActionName => nameof(GetById);

    protected CrudControllerBase(TService service)
    {
        Service = service;
    }

    [HttpGet]
    public virtual async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        if (!AllowGetAll)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var reader = RequireContract<IGetAllService<TEntity>>(nameof(GetAll));
        var results = await reader.GetAllAsync(ct);
        return OkResponse(results);
    }

    [HttpGet("withArchived")]
    public virtual async Task<IActionResult> GetAllWithArchived(bool withArchived = true, CancellationToken ct = default)
    {
        if (!AllowGetAllWithArchived)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var reader = RequireContract<IGetAllWithArchivedService<TEntity>>(nameof(GetAllWithArchived));
        var results = await reader.GetAllWithArchivedAsync(withArchived, ct);
        return OkResponse(results);
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct = default)
    {
        if (!AllowGetById)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var reader = RequireContract<IGetByIdService<TEntity, TKey>>(nameof(GetById));
        var result = await reader.GetByIdAsync(id, ct);

        if (result is null)
        {
            return NotFound();
        }

        return OkResponse(result);
    }

    [HttpGet("ids")]
    public virtual async Task<IActionResult> GetByIds([FromQuery] IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        if (!AllowGetByIds)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var reader = RequireContract<IGetByIdsService<TEntity, TKey>>(nameof(GetByIds));
        var result = await reader.GetByIdsAsync(ids, ct);
        return OkResponse(result);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Post([FromBody] TEntity model, CancellationToken ct = default)
    {
        if (!AllowPost)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var writer = RequireContract<ICreateService<TEntity>>(nameof(Post));
        var savedModel = await writer.CreateAsync(model, ct);

        if (!ReturnCreatedOnPost)
        {
            return OkResponse(savedModel);
        }

        var routeValues = BuildCreatedRouteValues(savedModel);
        if (routeValues is null)
        {
            return OkResponse(savedModel);
        }

        return CreatedResponse(GetByIdActionName, routeValues, savedModel);
    }

    [HttpPut]
    public virtual async Task<IActionResult> Put([FromBody] TEntity model, CancellationToken ct = default)
    {
        if (!AllowPut)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var writer = RequireContract<IUpdateService<TEntity>>(nameof(Put));
        var savedModel = await writer.UpdateAsync(model, ct);
        return OkResponse(savedModel);
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct = default)
    {
        if (!AllowDelete)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var deleter = RequireContract<IDeleteService<TKey>>(nameof(Delete));
        await deleter.DeleteAsync(id, ct);
        return Accepted();
    }

    protected virtual object? BuildCreatedRouteValues(TEntity createdEntity) => null;

    private TContract RequireContract<TContract>(string actionName)
        where TContract : class
    {
        if (Service is TContract contract)
        {
            return contract;
        }

        throw new InvalidOperationException(
            $"{GetType().Name} action '{actionName}' requires service '{typeof(TContract).Name}'.");
    }
}
