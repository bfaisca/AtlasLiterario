using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AtlasLiterario.API.Configuration;

/// <summary>
/// The custom DateTimeOffset converter hides the type from the schema generator; restore "string"/"date-time".
/// </summary>
internal sealed class UtcDateTimeOffsetSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "date-time";
        }
        else if (type == typeof(DateTimeOffset?))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = "date-time";
        }

        return Task.CompletedTask;
    }
}
