namespace IBeam.DemoService.Services;

public sealed class DemoService : IDemoService
{
    public Task<string> PingAsync(CancellationToken ct = default)
        => Task.FromResult("DemoService is alive.");
}
