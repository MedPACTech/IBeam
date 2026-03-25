using IBeam.Api.Abstractions;
using IBeam.Api.Controllers;
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
    public async Task Post_EnabledWithoutCreateContract_ThrowsInvalidOperationException()
    {
        var sut = new PostEnabledController(new ReadOnlyService());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.Post(new TestEntity(Guid.NewGuid(), "Grace"), CancellationToken.None));
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

    private sealed class TestController : CrudControllerBase<TestService, TestEntity, Guid>
    {
        public TestController(TestService service) : base(service)
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
