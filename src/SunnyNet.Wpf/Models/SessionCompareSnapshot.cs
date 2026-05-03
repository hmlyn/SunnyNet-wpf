namespace SunnyNet.Wpf.Models;

public sealed class SessionCompareSnapshot
{
    public int Index { get; init; }

    public int Theology { get; init; }

    public string Method { get; init; } = "";

    public string Url { get; init; } = "";

    public string State { get; init; } = "";

    public string ResponseLength { get; init; } = "";

    public string ResponseType { get; init; } = "";

    public string Process { get; init; } = "";

    public string Notes { get; init; } = "";

    public string RequestHeaders { get; init; } = "";

    public string RequestBody { get; init; } = "";

    public string RequestRaw { get; init; } = "";

    public string ResponseHeaders { get; init; } = "";

    public string ResponseBody { get; init; } = "";

    public string ResponseRaw { get; init; } = "";

    public string RequestRawMd5 { get; init; } = "";

    public string RequestRawSha256 { get; init; } = "";

    public string RequestBodyMd5 { get; init; } = "";

    public string RequestBodySha256 { get; init; } = "";

    public string ResponseRawMd5 { get; init; } = "";

    public string ResponseRawSha256 { get; init; } = "";

    public string ResponseBodyMd5 { get; init; } = "";

    public string ResponseBodySha256 { get; init; } = "";

    public string Summary => $"#{Index} {Method} {State} {Url}";

    public string MetaSummary
    {
        get
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(ResponseLength)) parts.Add($"长度 {ResponseLength}");
            if (!string.IsNullOrWhiteSpace(ResponseType)) parts.Add(ResponseType);
            if (!string.IsNullOrWhiteSpace(Process)) parts.Add(Process);
            if (!string.IsNullOrWhiteSpace(Notes)) parts.Add($"备注：{Notes}");
            return string.Join(" · ", parts);
        }
    }

    public string GetCompareText(string range)
    {
        return range switch
        {
            "RequestHeaders" => RequestHeaders,
            "RequestBody" => RequestBody,
            "ResponseRaw" => ResponseRaw,
            "ResponseHeaders" => ResponseHeaders,
            "ResponseBody" => ResponseBody,
            _ => RequestRaw
        };
    }
}
