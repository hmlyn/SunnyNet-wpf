namespace SunnyNet.Wpf.Models;

public sealed record SearchRequest(
    string Value,
    string Type,
    string Range,
    string Color,
    bool IgnoreCase,
    bool RemoveSpaces,
    bool ClearPrevious,
    int ProtoSkip)
{
    public string Options
    {
        get
        {
            List<string> options = new();
            if (IgnoreCase)
            {
                options.Add("不区分大小写");
            }

            if (RemoveSpaces)
            {
                options.Add("删除空格后搜索");
            }

            if (ClearPrevious)
            {
                options.Add("取消之前的颜色标记");
            }

            return string.Join("|", options);
        }
    }
}

public sealed record SearchExecutionResult(int MatchCount, CaptureEntry? FirstMatch);
