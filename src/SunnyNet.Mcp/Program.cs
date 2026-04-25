using System.Net.Http.Json;
using System.Text.Json;

string endpoint = args.FirstOrDefault(arg => arg.StartsWith("--endpoint=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
    ?? Environment.GetEnvironmentVariable("SUNNYNET_MCP_ENDPOINT")
    ?? "http://127.0.0.1:20256/mcp";

using HttpClient client = new();
Console.Error.WriteLine($"SunnyNet MCP stdio bridge -> {endpoint}");

while (await Console.In.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    try
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        object payload = ConvertJsonRpcToHttpPayload(root);
        using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, payload);
        string responseText = await response.Content.ReadAsStringAsync();
        object? result = JsonSerializer.Deserialize<JsonElement>(responseText);
        Console.WriteLine(BuildJsonRpcResponse(root, result, response.IsSuccessStatusCode));
        Console.Out.Flush();
    }
    catch (Exception exception)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            error = new { code = -32603, message = exception.Message },
            id = (object?)null
        }));
        Console.Out.Flush();
    }
}

static object ConvertJsonRpcToHttpPayload(JsonElement root)
{
    string method = root.TryGetProperty("method", out JsonElement methodElement) ? methodElement.GetString() ?? "tools/list" : "tools/list";
    JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement) ? paramsElement : default;
    if (method == "tools/call" && parameters.ValueKind == JsonValueKind.Object)
    {
        return new
        {
            method,
            arguments = parameters
        };
    }

    return new
    {
        method,
        arguments = parameters
    };
}

static string BuildJsonRpcResponse(JsonElement request, object? httpResult, bool ok)
{
    object? id = request.TryGetProperty("id", out JsonElement idElement) ? JsonElementToObject(idElement) : null;
    if (!ok)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            error = new { code = -32000, message = "SunnyNet MCP HTTP request failed", data = httpResult },
            id
        });
    }

    JsonElement resultElement = httpResult is JsonElement element ? element : default;
    if (resultElement.ValueKind == JsonValueKind.Object
        && resultElement.TryGetProperty("ok", out JsonElement okElement)
        && okElement.ValueKind == JsonValueKind.False)
    {
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            error = new { code = -32000, message = resultElement.GetProperty("error").GetString() },
            id
        });
    }

    object? result = resultElement.ValueKind == JsonValueKind.Object && resultElement.TryGetProperty("result", out JsonElement value)
        ? JsonElementToObject(value)
        : JsonElementToObject(resultElement);

    return JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        result,
        id
    });
}

static object? JsonElementToObject(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out long longValue) => longValue,
        JsonValueKind.Number when element.TryGetDouble(out double doubleValue) => doubleValue,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => JsonSerializer.Deserialize<object>(element.GetRawText())
    };
}
