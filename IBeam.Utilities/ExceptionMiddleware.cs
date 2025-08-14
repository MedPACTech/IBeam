using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


// Avoid ambiguity with System.Net.ProblemDetails
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace IBeam.Utilities
{
    /// <summary>
    /// Global exception middleware that converts exceptions to RFC7807 ProblemDetails JSON.
    /// - First-class support for IBaseException (Code, UserMessage, StatusCode, client-safe extensions).
    /// - Dev vs Prod behavior: include stack traces & request info in dev only (configurable).
    /// </summary>
    public sealed class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly bool _defaultIncludeDetails;
        private readonly ExceptionMiddlewareOptions _options;
        private readonly JsonSerializerOptions _json;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IHostEnvironment env,
            IOptions<ExceptionMiddlewareOptions>? options = null)
        {
            _next = next;
            _logger = logger;
            _options = options?.Value ?? new ExceptionMiddlewareOptions();
            _defaultIncludeDetails = _options.IncludeExceptionDetails ?? env.IsDevelopment();

            _json = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = _defaultIncludeDetails
            };
            _options.ConfigureJsonSerializerOptions?.Invoke(_json);
        }

        public async Task Invoke(HttpContext ctx)
        {
            try
            {
                await _next(ctx);
            }
            catch (Exception ex)
            {
                if (ctx.Response.HasStarted)
                {
                    _logger.LogError(ex, "Unhandled exception after response started. Path={Path}", ctx.Request.Path);
                    throw;
                }

                var (status, problem) = MapToProblemDetails(ctx, ex);

                ctx.Response.Clear();
                ctx.Response.StatusCode = (int)status;
                ctx.Response.ContentType = "application/problem+json";

                await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, _json));
            }
        }

        private (HttpStatusCode, MvcProblemDetails) MapToProblemDetails(HttpContext ctx, Exception ex)
        {
            var traceId = ctx.TraceIdentifier;

            // 1) First-class: any app exception implementing IBaseException
            if (ex is IBaseException bex)
            {
                var pd = new MvcProblemDetails
                {
                    Title = bex.Code,              // e.g., "REPOSITORY.SAVE"
                    Detail = bex.UserMessage,       // curated, user-safe
                    Status = (int)bex.StatusCode,
                    Type = "about:blank",
                    Instance = ctx.Request.Path
                };

                foreach (var kv in bex.GetClientExtensions())
                    pd.Extensions[kv.Key] = kv.Value;

                pd.Extensions["traceId"] = traceId;

                if (ShouldIncludeDetails(ctx, ex))
                    EnrichForDev(pd, ex, ctx);

                _logger.LogError(ex, "[IBaseException] code={Code} status={Status} traceId={TraceId}",
                    bex.Code, (int)bex.StatusCode, traceId);

                return (bex.StatusCode, pd);
            }

            // 2) Fallback: generic 500 with minimal detail in prod, richer in dev
            var e = ex.InnerException ?? ex;
            var fallback = new MvcProblemDetails
            {
                Title = "Unexpected Exception",
                Detail = ShouldIncludeDetails(ctx, ex) ? e.Message : "An unexpected error occurred. Please try again later.",
                Status = (int)HttpStatusCode.InternalServerError,
                Type = "about:blank",
                Instance = ctx.Request.Path
            };
            fallback.Extensions["traceId"] = traceId;

            if (ShouldIncludeDetails(ctx, ex))
                EnrichForDev(fallback, ex, ctx);

            _logger.LogError(ex, "[Unhandled] status=500 traceId={TraceId}", traceId);

            return (HttpStatusCode.InternalServerError, fallback);
        }

        private bool ShouldIncludeDetails(HttpContext ctx, Exception ex) =>
            _options.IncludeDetailsPredicate?.Invoke(ctx, ex) ?? _defaultIncludeDetails;

        // ---------- Dev enrichment & helpers ----------

        private void EnrichForDev(MvcProblemDetails pd, Exception ex, HttpContext ctx)
        {
            var e = ex.InnerException ?? ex;

            pd.Extensions["exception"] = new
            {
                type = e.GetType().FullName,
                message = e.Message,
                stackTrace = e.StackTrace,
                inner = FlattenInner(e)
            };

            pd.Extensions["request"] = new
            {
                path = ctx.Request.Path.Value,
                method = ctx.Request.Method,
                query = ctx.Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString())
            };
        }

        private static object? FlattenInner(Exception e)
        {
            if (e.InnerException == null) return null;
            var list = new List<object>();
            var cur = e.InnerException;
            while (cur != null)
            {
                list.Add(new
                {
                    type = cur.GetType().FullName,
                    message = cur.Message,
                    stackTrace = cur.StackTrace
                });
                cur = cur.InnerException;
            }
            return list;
        }
    }

    /// <summary>
    /// Optional configuration for ExceptionMiddleware.
    /// </summary>
    public sealed class ExceptionMiddlewareOptions
    {
        /// <summary>
        /// If set, overrides environment-based detail behavior (default: env.IsDevelopment()).
        /// </summary>
        public bool? IncludeExceptionDetails { get; set; }

        /// <summary>
        /// Optional predicate for fine-grained control (e.g., only include for local requests).
        /// </summary>
        public Func<HttpContext, Exception, bool>? IncludeDetailsPredicate { get; set; }

        /// <summary>
        /// Hook to customize JSON serialization (camelCase + dev pretty-print are preset).
        /// </summary>
        public Action<JsonSerializerOptions>? ConfigureJsonSerializerOptions { get; set; }
    }

    public static class ExceptionMiddlewareExtensions
    {
        /// <summary>
        /// Registers the IBeam exception middleware.
        /// </summary>
        public static IApplicationBuilder UseIBeamExceptionHandling(this IApplicationBuilder app)
            => app.UseMiddleware<ExceptionMiddleware>();
    }
}
