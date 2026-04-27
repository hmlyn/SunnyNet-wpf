using System.Runtime.CompilerServices;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public abstract class TrafficRuleItemBase : ViewModelBase
{
    private string _hash = Guid.NewGuid().ToString("N");
    private bool _enabled = true;
    private string _name = "新规则";
    private string _method = "ANY";
    private string _urlMatchType = "通配";
    private string _urlPattern = "";
    private int _priority = 100;
    private string _note = "";
    private string _state = "未保存";

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetRuleProperty(ref _enabled, value);
    }

    public string Name
    {
        get => _name;
        set => SetRuleProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "新规则" : value);
    }

    public string Method
    {
        get => _method;
        set => SetRuleProperty(ref _method, string.IsNullOrWhiteSpace(value) ? "ANY" : value.Trim().ToUpperInvariant());
    }

    public string UrlMatchType
    {
        get => _urlMatchType;
        set => SetRuleProperty(ref _urlMatchType, string.IsNullOrWhiteSpace(value) ? "通配" : value);
    }

    public string UrlPattern
    {
        get => _urlPattern;
        set => SetRuleProperty(ref _urlPattern, value ?? "");
    }

    public int Priority
    {
        get => _priority;
        set => SetRuleProperty(ref _priority, value);
    }

    public string Note
    {
        get => _note;
        set => SetRuleProperty(ref _note, value ?? "");
    }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value ?? "");
    }

    public string MatchSummary => $"{Method} · {UrlMatchType} · {(string.IsNullOrWhiteSpace(UrlPattern) ? "*" : UrlPattern)}";

    protected bool SetRuleProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        State = "未保存";
        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(Summary));
        return true;
    }

    public abstract string Summary { get; }
}

public sealed class RequestBlockRuleItem : TrafficRuleItemBase
{
    private string _action = "返回空响应";
    private int _statusCode = 403;
    private string _responseValueType = "String(UTF8)";
    private string _responseContent = "";

    public string Action
    {
        get => _action;
        set => SetRuleProperty(ref _action, string.IsNullOrWhiteSpace(value) ? "返回空响应" : value);
    }

    public int StatusCode
    {
        get => _statusCode;
        set => SetRuleProperty(ref _statusCode, value);
    }

    public string ResponseValueType
    {
        get => _responseValueType;
        set => SetRuleProperty(ref _responseValueType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value);
    }

    public string ResponseContent
    {
        get => _responseContent;
        set => SetRuleProperty(ref _responseContent, value ?? "");
    }

    public override string Summary => Action == "自定义状态码"
        ? $"{Action} · {StatusCode}"
        : Action;
}

public sealed class RequestRewriteRuleItem : TrafficRuleItemBase
{
    private string _direction = "请求";
    private string _target = "协议头";
    private string _operation = "设置";
    private string _key = "";
    private string _value = "";
    private string _valueType = "String(UTF8)";
    private string _operationsJson = "[]";

    public string Direction
    {
        get => _direction;
        set => SetRuleProperty(ref _direction, string.IsNullOrWhiteSpace(value) ? "请求" : value);
    }

    public string Target
    {
        get => _target;
        set => SetRuleProperty(ref _target, string.IsNullOrWhiteSpace(value) ? "协议头" : value);
    }

    public string Operation
    {
        get => _operation;
        set => SetRuleProperty(ref _operation, string.IsNullOrWhiteSpace(value) ? "设置" : value);
    }

    public string Key
    {
        get => _key;
        set => SetRuleProperty(ref _key, value ?? "");
    }

    public string Value
    {
        get => _value;
        set => SetRuleProperty(ref _value, value ?? "");
    }

    public string ValueType
    {
        get => _valueType;
        set => SetRuleProperty(ref _valueType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value);
    }

    public string OperationsJson
    {
        get => _operationsJson;
        set => SetRuleProperty(ref _operationsJson, string.IsNullOrWhiteSpace(value) ? "[]" : value);
    }

    public override string Summary => string.IsNullOrWhiteSpace(Key)
        ? $"{Direction} · {Operation}{Target}"
        : $"{Direction} · {Operation}{Target} · {Key}";
}

public sealed class RequestMappingRuleItem : TrafficRuleItemBase
{
    private string _mappingType = "本地文件";
    private string _sourceContent = "";
    private string _targetContent = "";
    private string _valueType = "String(UTF8)";
    private bool _legacyReplaceRule;

    public string MappingType
    {
        get => _mappingType;
        set => SetRuleProperty(ref _mappingType, string.IsNullOrWhiteSpace(value) ? "本地文件" : value);
    }

    public string SourceContent
    {
        get => _sourceContent;
        set => SetRuleProperty(ref _sourceContent, value ?? "");
    }

    public string TargetContent
    {
        get => _targetContent;
        set => SetRuleProperty(ref _targetContent, value ?? "");
    }

    public string ValueType
    {
        get => _valueType;
        set => SetRuleProperty(ref _valueType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value);
    }

    public bool LegacyReplaceRule
    {
        get => _legacyReplaceRule;
        set => SetRuleProperty(ref _legacyReplaceRule, value);
    }

    public override string Summary => LegacyReplaceRule
        ? $"旧替换规则 · {ValueType}"
        : $"{MappingType} · {ValueType}";
}

public sealed class RequestDecodeRuleItem : TrafficRuleItemBase
{
    private string _direction = "响应";
    private string _decoderType = "自动解压";
    private string _scriptCode = "";

    public string Direction
    {
        get => _direction;
        set => SetRuleProperty(ref _direction, string.IsNullOrWhiteSpace(value) ? "响应" : value);
    }

    public string DecoderType
    {
        get => _decoderType;
        set => SetRuleProperty(ref _decoderType, string.IsNullOrWhiteSpace(value) ? "自动解压" : value);
    }

    public string ScriptCode
    {
        get => _scriptCode;
        set => SetRuleProperty(ref _scriptCode, value ?? "");
    }

    public override string Summary => $"{Direction} · {DecoderType}";
}

public sealed class TrafficRuleHitItem
{
    public string Time { get; set; } = "";

    public int Theology { get; set; }

    public string RuleType { get; set; } = "";

    public string RuleHash { get; set; } = "";

    public string RuleName { get; set; } = "";

    public string Action { get; set; } = "";

    public string Direction { get; set; } = "";

    public string URL { get; set; } = "";

    public string Summary => string.IsNullOrWhiteSpace(Direction)
        ? $"{RuleType} · {Action}"
        : $"{RuleType} · {Direction} · {Action}";
}
