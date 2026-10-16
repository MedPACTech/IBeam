using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
namespace IBeam.Tests.Management.Diagnostics;
[TestClass]
public sealed class SanitizationStorageIntegrationTests
{
    private const string Raw = "private-raw-column-canary";
    private const string Forbidden = "business-audit-must-not-be-exposed";
    [TestMethod, TestCategory("StorageIntegration"), DataRow(false), DataRow(true)]
    public async Task RawColumnsAreExplicitOnlyAndSourceCategoryAuditExclusionsStayReadOnly(bool log)
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "Sanitization" + Guid.NewGuid().ToString("N")); await table.CreateIfNotExistsAsync();
        try
        {
            for(var i=0;i<2;i++) await table.AddEntityAsync(Row(log, i, "qurvia", "Testing"));
            await table.AddEntityAsync(Row(log, 2, "bindry", "Testing")); await table.AddEntityAsync(Row(log, 3, "qurvia", "Production"));
            await table.AddEntityAsync(Row(log, 4, null, null)); await table.AddEntityAsync(Row(log, 5, "qurvia", null));
            if(log) foreach(var category in new[] { "EntityChange", "SelectRollup" }) { var audit=Row(true, category == "EntityChange" ? 6:7, "qurvia", "Testing"); audit["Category"]=category; audit["IsSelectRollup"]=category=="SelectRollup"; audit["Message"]=Forbidden; await table.AddEntityAsync(audit); }
            var tenant=Row(log, 8, "qurvia", "Testing"); tenant.PartitionKey="TENANT|"+Guid.NewGuid()+"|DAY|20261016"; await table.AddEntityAsync(tenant);
            var before=await Snapshot(table);
            foreach(var raw in new[] { false, true })
            {
                var capture = new Capture(); var options = new TableClientOptions(); options.Retry.MaxRetries=0; options.AddPolicy(capture,HttpPipelinePosition.PerCall);
                var readOnly = new TableClient(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(15)), options);
                var module=log?"Logs":"Errors"; var operation=log?ManagementOperations.LogsRead:ManagementOperations.ErrorsRead;
                await using var f=await SystemErrorApiFixture.CreateAsync(installLogs: log, repository: log?null:new AzureTableSystemErrorReadRepository(readOnly),
                    logRepository: log?new AzureTableSystemLogReadRepository(readOnly):null, managementAudit:true, configure: values=> {
                        values["IBeam:Management:Diagnostics:Environment"]="Testing";
                        values[$"IBeam:Management:Diagnostics:{module}:SourceMode"]="TaggedShared";
                        values[$"IBeam:Management:Diagnostics:{module}:TrustedTagsConfirmed"]="true";
                        values[$"IBeam:Management:Diagnostics:{module}:Sanitization:Enabled"]=raw?"false":"true";
                    });
                var key=await f.IssueAsync(ManagementOperations.DiagnosticsInfo,operation); var route="/api/management-diagnostics/"+(log?"system-logs":"system-errors");
                using var response=await f.SendAsync(route+"?pageSize=1",key.ApiKey); Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
                var text=await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(Forbidden,text); Assert.AreEqual(raw,text.Contains(Raw,StringComparison.Ordinal));
                using var body=JsonDocument.Parse(text); var row=body.RootElement.GetProperty("data")[0]; var count=body.RootElement.GetProperty("data").GetArrayLength();
                Assert.AreEqual(raw,row.TryGetProperty("unsanitized",out var fields)); if(raw) Assert.AreEqual(Raw,fields.GetProperty("message").GetString());
                using var detail=await f.SendAsync(route+"/"+row.GetProperty("id").GetString(),key.ApiKey); Assert.AreEqual(HttpStatusCode.OK,detail.StatusCode);
                var detailText=await detail.Content.ReadAsStringAsync(); Assert.DoesNotContain(Forbidden,detailText); Assert.AreEqual(raw,detailText.Contains(Raw,StringComparison.Ordinal));
                var cursor=body.RootElement.GetProperty("continuationToken").GetString(); var pages=0;
                while(cursor is not null)
                {
                    Assert.IsLessThan(15,++pages); using var next=await f.SendAsync(route+"?continuationToken="+cursor,key.ApiKey); Assert.AreEqual(HttpStatusCode.OK,next.StatusCode);
                    var nextText=await next.Content.ReadAsStringAsync();Assert.DoesNotContain(Forbidden,nextText); using var json=JsonDocument.Parse(nextText);
                    count+=json.RootElement.GetProperty("data").GetArrayLength();cursor=json.RootElement.GetProperty("continuationToken").GetString();
                }
                Assert.AreEqual(2,count);
                Assert.IsTrue(capture.Requests.All(x=>x.Method=="GET"));
                Assert.IsTrue(capture.Requests.All(x=>x.Query.Contains("$select=",StringComparison.Ordinal)));
                Assert.IsTrue(capture.Requests.All(x=>Selected(x.Query).Contains("Message")==raw));
                foreach(var column in new[] {"BeforeJson","AfterJson","OriginalJson","TransformedJson","ActorId","DeviceId","IpAddress","UserAgent","TenantId","EntityId"})
                    Assert.IsTrue(capture.Requests.All(x=>!Selected(x.Query).Contains(column)));
                if(log) Assert.IsTrue(capture.Requests.Where(x=>x.Query.Contains("$filter=",StringComparison.Ordinal)).All(x=>x.Query.Contains("Category eq 'System'",StringComparison.Ordinal)&&x.Query.Contains("PartitionKey eq 'SYSTEM|DAY|",StringComparison.Ordinal)));
                Assert.DoesNotContain(Raw,JsonSerializer.Serialize(f.ManagementAudits.Rows)); Assert.DoesNotContain(Forbidden,JsonSerializer.Serialize(f.ManagementAudits.Rows));
                Assert.DoesNotContain(Raw,string.Join(" ",f.Logs.Messages));
                var write=await Assert.ThrowsAsync<RequestFailedException>(()=>readOnly.UpsertEntityAsync(Row(log,9,"qurvia","Testing"))); Assert.AreEqual(403,write.Status);
            }
            CollectionAssert.AreEqual(before,await Snapshot(table));
        }
        finally {await table.DeleteAsync();}
    }
    private static TableEntity Row(bool log,int minute,string? app,string? env)
    {
        var at=ErrorReaderFixture.Now.AddHours(-2).AddMinutes(minute);
        var row=new TableEntity((log?"SYSTEM|DAY|":"ERR|")+at.ToString("yyyyMMdd"),at.ToString(log?"HHmmssfffffff":"HHmmssfff")+"|"+Guid.NewGuid().ToString("N")) {
            ["OccurredAtUtc"]=at,["Category"]="System",["IsSelectRollup"]=false,["Level"]="Error",["Source"]=Raw,["TraceId"]=Raw,
            ["Message"]=Raw,["Exception"]=Raw,["Path"]=Raw,["Method"]="POST",["Detail"]=Raw,
            ["BeforeJson"]=Forbidden,["AfterJson"]=Forbidden,["OriginalJson"]=Forbidden,["TransformedJson"]=Forbidden,
            ["ActorId"]=Forbidden,["DeviceId"]=Forbidden,["IpAddress"]=Forbidden,["UserAgent"]=Forbidden,["TenantId"]=Guid.NewGuid(),["EntityId"]=Guid.NewGuid() };
        if(app is not null)row["ManagementApplicationKey"]=app;if(env is not null)row["ManagementEnvironment"]=env;return row;
    }
    private static string[] Selected(string query) {var start=query.IndexOf("$select=",StringComparison.Ordinal);if(start<0)return[];var value=query[(start+8)..];var end=value.IndexOf('&');return(end<0?value:value[..end]).Split(',');}
    private static async Task<string[]> Snapshot(TableClient table) {var rows=new List<string>();await foreach(var row in table.QueryAsync<TableEntity>())rows.Add(row.PartitionKey+"/"+row.RowKey+"/"+row.ETag);return rows.Order(StringComparer.Ordinal).ToArray();}
    private sealed class Capture:HttpPipelineSynchronousPolicy
    {
        public ConcurrentQueue<(string Method,string Query)> Requests {get;}=new();
        public override void OnSendingRequest(HttpMessage message)=>Requests.Enqueue((message.Request.Method.Method,Uri.UnescapeDataString(message.Request.Uri.ToUri().Query)));
    }
}
