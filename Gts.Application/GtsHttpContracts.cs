using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gts.Application;

internal sealed record ValidateInstanceRequest(string InstanceId);
internal sealed record ValidateTypeSchemaRequest(string? TypeId, string? SchemaId);
internal sealed record ValidateEntityRequest(string? EntityId, string? GtsId);
internal sealed record CastRequest(string InstanceId, string? ToTypeId, string? ToSchemaId);
internal sealed record ValidateJsonResponse(bool Ok, string? Id, string? TypeId, bool IsTypeSchema, string? Error);
internal sealed record EntityRegistrationResponse(bool Ok, string? Id, string? TypeId, bool IsTypeSchema, string? Error = null);
internal sealed record CastResponse(JsonNode? CastedEntity, string BackwardCompatibility, string ForwardCompatibility, string FullCompatibility, string? Error = null);

internal static class GtsHttpJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}