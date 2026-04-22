using System.Text.Json;
using System.Text.Json.Serialization;

namespace SunnyNet.Wpf.Models;

public sealed class BackendEventEnvelope
{
    [JsonPropertyName("Command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("Args")]
    public JsonElement Args { get; set; }
}

public sealed class BackendResponseEnvelope
{
    [JsonPropertyName("ok")]
    public bool OK { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("err")]
    public JsonElement Error { get; set; }
}
