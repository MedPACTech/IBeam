using IBeam.Api.Abstractions;
using IBeam.Api.Controllers;
using IBeam.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Tests.Api;

[TestClass]
public sealed class CrudControllerBaseTests
{
    [TestMethod]
    public async Task GetById_DefaultEnabled_ReturnsOk_WhenEntityExists()
    {
        var id = Guid.NewGuid();
        var service = new TestService
        {
            ById = new TestEntity(id, "Ada")
        };
        var sut = new TestController(service);

        var result = await sut.GetById(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task GetAll_DefaultDisabled_Returns405()
    {
        var sut = new TestController(new TestService());

        var result = await sut.GetAll(CancellationToken.None);

        Assert.IsInstanceOfType<StatusCodeResult>(result);
        var status = (StatusCodeResult)result;
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, status.StatusCode);
    }

    [TestMethod]
    public async Task GetAll_CursorPagingEnabled_ReturnsCursorPagedResponse()
    {
        var service = new PagedService
        {
            CursorPage = new CursorPagedResult<TestEntity>(
                new[] { new TestEntity(Guid.NewGuid(), "Ada") },
                "next-token")
        };
        var sut = new PagedController(service);

        var result = await sut.GetAll(pageSize: 2, continuationToken: "current-token", ct: CancellationToken.None);

        var ok = AssertOk<ApiCursorPagedResponse<TestEntity>>(result);
        Assert.AreEqual(2, ok.PageSize);
        Assert.AreEqual("next-token", ok.ContinuationToken);
        Assert.AreEqual("current-token", service.LastContinuationToken);
        Assert.AreEqual(1, ok.Data!.Count());
    }

    [TestMethod]
    public async Task GetAll_OffsetPagingEnabled_ReturnsOffsetPagedResponse()
    {
        var service = new PagedService
        {
            OffsetPage = new OffsetPagedResult<TestEntity>(
                new[] { new TestEntity(Guid.NewGuid(), "Grace") },
                PageNumber: 2,
                PageSize: 10,
                TotalCount: 31)
        };
        var sut = new PagedController(service);

        var result = await sut.GetAll(pageSize: 10, pageNumber: 2, ct: CancellationToken.None);

        var ok = AssertOk<ApiOffsetPagedResponse<TestEntity>>(result);
        Assert.AreEqual(2, ok.PageNumber);
        Assert.AreEqual(10, ok.PageSize);
        Assert.AreEqual(31, ok.TotalCount);
        Assert.AreEqual(2, service.LastPageNumber);
        Assert.AreEqual(10, service.LastPageSize);
    }

    [TestMethod]
    public async Task GetAll_WithPageNumberAndContinuationToken_Returns400()
    {
        var sut = new PagedController(new PagedService());

        var result = await sut.GetAll(
            pageSize: 10,
            pageNumber: 2,
            continuationToken: "next",
            ct: CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task GetAll_WithPageNumberWithoutPageSize_Returns400()
    {
        var sut = new PagedController(new PagedService());

        var result = await sut.GetAll(pageNumber: 2, ct: CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task GetAll_WithCursorPagingDisabled_Returns405()
    {
        var sut = new TestController(new TestService());

        var result = await sut.GetAll(pageSize: 10, ct: CancellationToken.None);

        Assert.IsInstanceOfType<StatusCodeResult>(result);
        var status = (StatusCodeResult)result;
        Assert.AreEqual(StatusCodes.Status405MethodNotAllowed, status.StatusCode);
    }

    [TestMethod]
    public async Task Post_EnabledWithoutCreateContract_ThrowsInvalidOperationException()
    {
        var sut = new PostEnabledController(new ReadOnlyService());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.Post(new TestEntity(Guid.NewGuid(), "Grace"), CancellationToken.None));
    }

    private static T AssertOk<T>(IActionResult result)
    {
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var ok = (OkObjectResult)result;
        Assert.IsInstanceOfType<T>(ok.Value);
        return (T)ok.Value!;
    }

    private sealed record TestEntity(Guid Id, string Name);

    private sealed class TestService : IGetByIdService<TestEntity, Guid>, IGetAllService<TestEntity>
    {
        public TestEntity? ById { get; set; }

        public Task<IEnumerable<TestEntity>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult(Enumerable.Empty<TestEntity>());

        public Task<TestEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(ById);
    }

    private sealed class ReadOnlyService : IGetByIdService<TestEntity, Guid>
    {
        public Task<TestEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<TestEntity?>(null);
    }

    private sealed class PagedService :
        IGetAllCursorPagedService<TestEntity>,
        IGetAllOffsetPagedService<TestEntity>
    {
        public CursorPagedResult<TestEntity> CursorPage { get; set; }
            = new(Enumerable.Empty<TestEntity>(), null);

        public OffsetPagedResult<TestEntity> OffsetPage { get; set; }
            = new(Enumerable.Empty<TestEntity>(), 1, 10, 0);

        public int? LastPageNumber { get; private set; }
        public int? LastPageSize { get; private set; }
        public string? LastContinuationToken { get; private set; }

        public Task<CursorPagedResult<TestEntity>> GetAllCursorPagedAsync(
            int pageSize,
            string? continuationToken = null,
            CancellationToken ct = default)
        {
            LastPageSize = pageSize;
            LastContinuationToken = continuationToken;
            return Task.FromResult(CursorPage);
        }

        public Task<OffsetPagedResult<TestEntity>> GetAllOffsetPagedAsync(
            int pageNumber,
            int pageSize,
            CancellationToken ct = default)
        {
            LastPageNumber = pageNumber;
            LastPageSize = pageSize;
            return Task.FromResult(OffsetPage);
        }
    }

    private sealed class TestController : CrudControllerBase<TestService, TestEntity, Guid>
    {
        public TestController(TestService service) : base(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }
    }

    private sealed class PagedController : CrudControllerBase<PagedService, TestEntity, Guid>
    {
        protected override bool AllowGetAllCursorPaged => true;
        protected override bool AllowGetAllOffsetPaged => true;

        public PagedController(PagedService service) : base(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }
    }

    private sealed class PostEnabledController : CrudControllerBase<ReadOnlyService, TestEntity, Guid>
    {
        protected override bool AllowPost => true;

        public PostEnabledController(ReadOnlyService service) : base(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }
    }
}
