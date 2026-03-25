using IBeam.Api.Abstractions;

namespace IBeam.Api.Infrastructure;

/// <summary>
/// Captures background and process-level exceptions outside normal HTTP middleware.
/// </summary>
public sealed class GlobalErrorHandler : IHostedService
{
    private readonly ILogger<GlobalErrorHandler> _logger;
    private readonly IApiErrorSink? _errorSink;

    public GlobalErrorHandler(
        ILogger<GlobalErrorHandler> logger,
        IApiErrorSink? errorSink = null)
    {
        _logger = logger;
        _errorSink = errorSink;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        return Task.CompletedTask;
    }

    public Task HandleStartupExceptionAsync(Exception ex, CancellationToken cancellationToken = default)
        => HandleExceptionAsync(ex, "StartupException", cancellationToken);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception ex)
        {
            _ = HandleExceptionAsync(ex, "AppDomain.UnhandledException", CancellationToken.None);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _ = HandleExceptionAsync(args.Exception, "TaskScheduler.UnobservedTaskException", CancellationToken.None);
        args.SetObserved();
    }

    private async Task HandleExceptionAsync(Exception ex, string source, CancellationToken cancellationToken)
    {
        _logger.LogCritical(ex, "Global error from {Source}", source);

        if (_errorSink is null)
        {
            return;
        }

        try
        {
            await _errorSink.SaveAsync(new ApiErrorRecord
            {
                Source = source,
                Path = source,
                Method = "N/A",
                Message = ex.Message,
                Exception = ex.ToString(),
                TraceId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception saveEx)
        {
            _logger.LogError(saveEx, "Failed to persist global error.");
        }
    }
}
