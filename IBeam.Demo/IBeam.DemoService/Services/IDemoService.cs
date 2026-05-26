namespace IBeam.DemoService.Services;

public interface IDemoService
{
    Task<string> PingAsync(CancellationToken ct = default);
}
