using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBeam.Management.Operations;

/// <summary>Uses only the registered typed schema, bounded bytes/depth and strict property names. Never logs payloads.</summary>
public sealed class ManagementOperationPayloadCodec(ManagementOperationRegistry registry)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true
    };
    public TRequest ReadRequest<TRequest>(string operation, ReadOnlyMemory<byte> bytes)
    {
        var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Request.ContractType != typeof(TRequest) || bytes.Length > descriptor.MaximumRequestBytes ||
            descriptor.Method == "GET" && !bytes.IsEmpty) throw new ManagementAccessDeniedException();
        return Read<TRequest>(bytes.IsEmpty && descriptor.Method == "GET" ? "{}"u8.ToArray() : bytes);
    }
    public byte[] WriteRequest<TRequest>(string operation, TRequest request)
    {
        var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Request.ContractType != typeof(TRequest) || request is null) throw new ManagementAccessDeniedException();
        using var stream = new BoundedStream(descriptor.MaximumRequestBytes);
        try
        {
            using (var writer = new Utf8JsonWriter(stream)) JsonSerializer.Serialize(writer, request, Options);
            var bytes = stream.ToArray();
            if (descriptor.Method == "GET")
            {
                if (!bytes.AsSpan().SequenceEqual("{}"u8)) throw new ManagementAccessDeniedException();
                return [];
            }
            _ = Read<TRequest>(bytes); return bytes;
        }
        catch (JsonException) { throw new ManagementAccessDeniedException(); }
    }
    public byte[] WriteResult<TResult>(string operation, TResult result)
    {
        var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Result.ContractType != typeof(TResult) || result is null) throw new ManagementAccessDeniedException();
        try
        {
            using var stream = new BoundedStream(descriptor.MaximumResultBytes);
            using (var writer = new Utf8JsonWriter(stream)) JsonSerializer.Serialize(writer, result, Options);
            return stream.ToArray();
        }
        catch (JsonException) { throw new ManagementAccessDeniedException(); }
    }
    private sealed class BoundedStream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > maximumBytes - Length) throw new ManagementAccessDeniedException();
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximumBytes - Length) throw new ManagementAccessDeniedException();
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
        }
    }
    public TResult ReadResult<TResult>(string operation, ReadOnlyMemory<byte> bytes)
    {
        var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Result.ContractType != typeof(TResult) || bytes.Length > descriptor.MaximumResultBytes)
            throw new ManagementAccessDeniedException();
        return Read<TResult>(bytes);
    }
    private static T Read<T>(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            void Check(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var properties = element.EnumerateObject().ToArray();
                    if (properties.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != properties.Length)
                        throw new ManagementAccessDeniedException();
                    foreach (var property in properties) Check(property.Value);
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Check(item);
            }
            Check(document.RootElement);
            return JsonSerializer.Deserialize<T>(bytes.Span, Options) ?? throw new ManagementAccessDeniedException();
        }
        catch (JsonException) { throw new ManagementAccessDeniedException(); }
    }
}
