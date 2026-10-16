using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemErrorProjectionTests
{
    [TestMethod]
    public void KnownSourcesUseFixedSummariesAndUnknownFreeTextIsSuppressed()
    {
        var f = new ErrorReaderFixture();
        var projection = new SystemErrorProjection(Options.Create(f.Options), Options.Create(f.Deployment));
        var row = ErrorReaderFixture.Row(ErrorReaderFixture.Now) with { Source = "RequestPipeline", TraceId = SystemErrorApiFixture.Sensitive };
        var known = projection.Project("opaque-id", row);
        Assert.AreEqual("RequestPipeline", known.Source);
        Assert.AreEqual("An unexpected request processing error was recorded.", known.Summary);
        var unknown = projection.Project("opaque-id", row with { Source = "RequestPipeline?password=" + SystemErrorApiFixture.Sensitive });
        Assert.AreEqual("System", unknown.Source);
        Assert.AreEqual("An unexpected system error was recorded.", unknown.Summary);
        var json = JsonSerializer.Serialize(new[] { known, unknown });
        Assert.IsFalse(json.Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(row.PartitionKey, StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(row.RowKey, StringComparison.Ordinal));
    }

    [TestMethod]
    public void CorrelationIsAnOpaqueStableDeploymentScopedPseudonym()
    {
        var f = new ErrorReaderFixture();
        var row = ErrorReaderFixture.Row(ErrorReaderFixture.Now) with { TraceId = "0HND11FN7L0DA:00000001" };
        var projection = new SystemErrorProjection(Options.Create(f.Options), Options.Create(f.Deployment));
        var first = projection.Project("a", row);
        Assert.AreEqual(first.CorrelationId, projection.Project("b", row).CorrelationId);
        Assert.IsFalse(first.CorrelationId!.Contains(row.TraceId, StringComparison.Ordinal));
        var other = new SystemErrorProjection(Options.Create(f.Options),
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "medinsights", Environment = "Production" }));
        Assert.AreNotEqual(first.CorrelationId, other.Project("a", row).CorrelationId);
        Assert.IsNull(projection.Project("a", row with { TraceId = "" }).CorrelationId);
        Assert.IsNull(projection.Project("a", row with { TraceId = new string('x', 129) }).CorrelationId);
    }
}
