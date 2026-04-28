using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private string _action = "断开请求";

    public string Action
    {
        get => _action;
        set => SetRuleProperty(ref _action, string.IsNullOrWhiteSpace(value) ? "断开请求" : value);
    }

    public override string Summary => Action;
}

public sealed class WebSocketBlockRuleItem : TrafficRuleItemBase
{
    private string _action = "断开连接";

    public string Action
    {
        get => _action;
        set => SetRuleProperty(ref _action, string.IsNullOrWhiteSpace(value) ? "断开连接" : value);
    }

    public override string Summary => Action;
}

public sealed class TcpBlockRuleItem : TrafficRuleItemBase
{
    private string _action = "断开连接";

    public string Action
    {
        get => _action;
        set => SetRuleProperty(ref _action, string.IsNullOrWhiteSpace(value) ? "断开连接" : value);
    }

    public override string Summary => Action;
}

public sealed class UdpBlockRuleItem : TrafficRuleItemBase
{
    private string _action = "丢弃上行包";

    public string Action
    {
        get => _action;
        set => SetRuleProperty(ref _action, string.IsNullOrWhiteSpace(value) ? "丢弃上行包" : value);
    }

    public override string Summary => Action;
}

public sealed class RequestRewriteRuleItem : TrafficRuleItemBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private string _direction = "请求";
    private string _target = "协议头";
    private string _operation = "设置";
    private string _key = "";
    private string _value = "";
    private string _valueType = "String(UTF8)";
    private string _operationsJson = "[]";
    private bool _syncingOperations;
    private bool _refreshingOperationState;

    public RequestRewriteRuleItem()
    {
        Operations.CollectionChanged += RewriteOperationsChanged;
    }

    public string Direction
    {
        get => _direction;
        set
        {
            if (SetRuleProperty(ref _direction, string.IsNullOrWhiteSpace(value) ? "请求" : value))
            {
                RefreshRewriteEditorState();
            }
        }
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
        set
        {
            if (SetRuleProperty(ref _operationsJson, string.IsNullOrWhiteSpace(value) ? "[]" : value))
            {
                LoadOperationsFromJson();
            }
        }
    }

    public ObservableCollection<RequestRewriteOperationItem> Operations { get; } = new();

    public bool CanAddRequestMethod => IsRequestDirection && !HasSingletonTarget("请求方法", "Method");

    public bool CanAddUrl => IsRequestDirection && !HasSingletonTarget("URL", "完整URL");

    public bool CanAddPath => IsRequestDirection && !HasSingletonTarget("Path", "路径");

    public bool CanAddParameter => IsRequestDirection;

    public bool CanAddHeader => IsRequestDirection || IsResponseDirection;

    public bool CanAddBody => (IsRequestDirection || IsResponseDirection) && !HasSingletonTarget("Body", "请求体", "响应体");

    public bool CanAddStatusCode => IsResponseDirection && !HasSingletonTarget("状态码", "StatusCode");

    private bool IsRequestDirection => string.Equals(Direction, "请求", StringComparison.Ordinal);

    private bool IsResponseDirection => string.Equals(Direction, "响应", StringComparison.Ordinal);

    public override string Summary
    {
        get
        {
            if (Operations.Count > 1)
            {
                return $"{Direction} · {Operations.Count} 个动作";
            }

            RequestRewriteOperationItem? operation = Operations.Count == 1 ? Operations[0] : null;
            string target = operation?.Target ?? Target;
            string action = operation?.Operation ?? Operation;
            string key = operation?.Key ?? Key;
            return string.IsNullOrWhiteSpace(key)
                ? $"{Direction} · {action}{target}"
                : $"{Direction} · {action}{target} · {key}";
        }
    }

    public void EnsureOperations()
    {
        if (Operations.Count == 0)
        {
            LoadOperationsFromJson();
        }

        if (Operations.Count > 0)
        {
            RefreshRewriteEditorState();
            SyncLegacyFieldsFromFirstOperation();
            return;
        }

        Operations.Add(new RequestRewriteOperationItem
        {
            Target = Target,
            Operation = Operation,
            Key = Key,
            Value = Value,
            ValueType = ValueType
        });
        RefreshRewriteEditorState();
        SyncOperationsJson();
    }

    public void SyncOperationsJson()
    {
        foreach (RequestRewriteOperationItem operation in Operations)
        {
            operation.Normalize();
        }

        _operationsJson = JsonSerializer.Serialize(
            Operations.Select(static item => new
            {
                item.Target,
                item.Operation,
                item.Key,
                item.Value,
                item.ValueType
            }),
            JsonOptions);
        OnPropertyChanged(nameof(OperationsJson));
        SyncLegacyFieldsFromFirstOperation();
        OnPropertyChanged(nameof(Summary));
    }

    public void LoadOperationsFromJson()
    {
        _syncingOperations = true;
        try
        {
            Operations.Clear();
            if (!string.IsNullOrWhiteSpace(OperationsJson))
            {
                List<RequestRewriteOperationItem>? operations = JsonSerializer.Deserialize<List<RequestRewriteOperationItem>>(OperationsJson);
                if (operations is not null)
                {
                    foreach (RequestRewriteOperationItem operation in operations.Where(static item => item is not null))
                    {
                        Operations.Add(operation);
                    }
                }
            }
        }
        catch
        {
            Operations.Clear();
        }
        finally
        {
            _syncingOperations = false;
        }

        UpdateOperationIndexes();
        RefreshRewriteEditorState();
        OnPropertyChanged(nameof(Summary));
    }

    private void SyncLegacyFieldsFromFirstOperation()
    {
        RequestRewriteOperationItem? operation = Operations.FirstOrDefault();
        if (operation is null)
        {
            return;
        }

        SetLegacyField(ref _target, operation.Target, nameof(Target));
        SetLegacyField(ref _operation, operation.Operation, nameof(Operation));
        SetLegacyField(ref _key, operation.Key, nameof(Key));
        SetLegacyField(ref _value, operation.Value, nameof(Value));
        SetLegacyField(ref _valueType, operation.ValueType, nameof(ValueType));
    }

    private void SetLegacyField(ref string field, string value, string propertyName)
    {
        value ??= "";
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void RewriteOperationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RequestRewriteOperationItem item in e.OldItems.OfType<RequestRewriteOperationItem>())
            {
                item.PropertyChanged -= RewriteOperationPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RequestRewriteOperationItem item in e.NewItems.OfType<RequestRewriteOperationItem>())
            {
                item.PropertyChanged += RewriteOperationPropertyChanged;
            }
        }

        if (!_syncingOperations)
        {
            State = "未保存";
        }

        UpdateOperationIndexes();
        RefreshRewriteEditorState();
        OnPropertyChanged(nameof(Summary));
    }

    private void RewriteOperationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestRewriteOperationItem.DisplayIndex)
            or nameof(RequestRewriteOperationItem.DisplayIndexText)
            or nameof(RequestRewriteOperationItem.EditorDirection)
            or nameof(RequestRewriteOperationItem.ValidationMessage)
            or nameof(RequestRewriteOperationItem.HasValidationWarning)
            or nameof(RequestRewriteOperationItem.DisplayHint)
            or nameof(RequestRewriteOperationItem.IsTargetAllowedForDirection))
        {
            return;
        }

        if (_refreshingOperationState)
        {
            return;
        }

        if (!_syncingOperations)
        {
            State = "未保存";
        }

        RefreshRewriteEditorState();
        OnPropertyChanged(nameof(Summary));
    }

    private void UpdateOperationIndexes()
    {
        for (int index = 0; index < Operations.Count; index++)
        {
            Operations[index].DisplayIndex = index + 1;
        }
    }

    public void RefreshRewriteEditorState()
    {
        _refreshingOperationState = true;
        try
        {
            Dictionary<string, int> singletonCounts = Operations
                .Select(static operation => GetSingletonKey(operation.Target))
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .GroupBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (RequestRewriteOperationItem operation in Operations)
            {
                operation.EditorDirection = Direction;
                string singletonKey = GetSingletonKey(operation.Target);
                if (!operation.IsTargetAllowedForDirection)
                {
                    operation.ValidationMessage = $"{Direction}方向不支持 {operation.Target}。";
                }
                else if (!string.IsNullOrWhiteSpace(singletonKey) && singletonCounts.TryGetValue(singletonKey, out int count) && count > 1)
                {
                    operation.ValidationMessage = $"{operation.Target} 在同一规则中只能配置一个。";
                }
                else
                {
                    operation.ValidationMessage = "";
                }
            }
        }
        finally
        {
            _refreshingOperationState = false;
        }

        NotifyPaletteStateChanged();
    }

    private bool HasSingletonTarget(params string[] targets)
    {
        return Operations.Any(operation => targets.Any(target => string.Equals(operation.Target?.Trim(), target, StringComparison.OrdinalIgnoreCase)));
    }

    private void NotifyPaletteStateChanged()
    {
        OnPropertyChanged(nameof(CanAddRequestMethod));
        OnPropertyChanged(nameof(CanAddUrl));
        OnPropertyChanged(nameof(CanAddPath));
        OnPropertyChanged(nameof(CanAddParameter));
        OnPropertyChanged(nameof(CanAddHeader));
        OnPropertyChanged(nameof(CanAddBody));
        OnPropertyChanged(nameof(CanAddStatusCode));
        OnPropertyChanged(nameof(Summary));
    }

    private static string GetSingletonKey(string? target)
    {
        return target?.Trim() switch
        {
            "请求方法" or "Method" => "Method",
            "URL" or "完整URL" => "URL",
            "Path" or "路径" => "Path",
            "Body" or "请求体" or "响应体" => "Body",
            "状态码" or "StatusCode" => "StatusCode",
            _ => ""
        };
    }
}

public sealed class RequestRewriteOperationItem : ViewModelBase
{
    private static readonly string[] SetOnlyOperations = { "设置" };
    private static readonly string[] SetDeleteOperations = { "设置", "删除" };
    private static readonly string[] KeyValueOperations = { "设置", "添加", "删除" };

    private int _displayIndex;
    private string _editorDirection = "请求";
    private string _target = "协议头";
    private string _operation = "设置";
    private string _key = "";
    private string _value = "";
    private string _valueType = "String(UTF8)";
    private string _validationMessage = "";

    public RequestRewriteOperationItem()
    {
        UpdateAvailableOperations();
    }

    [JsonIgnore]
    public int DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (SetProperty(ref _displayIndex, value))
            {
                OnPropertyChanged(nameof(DisplayIndexText));
            }
        }
    }

    [JsonIgnore]
    public string DisplayIndexText => DisplayIndex <= 0 ? "--" : DisplayIndex.ToString("00");

    [JsonIgnore]
    public string EditorDirection
    {
        get => _editorDirection;
        set
        {
            if (SetProperty(ref _editorDirection, string.IsNullOrWhiteSpace(value) ? "请求" : value))
            {
                OnPropertyChanged(nameof(IsTargetAllowedForDirection));
                OnPropertyChanged(nameof(DisplayHint));
            }
        }
    }

    [JsonIgnore]
    public ObservableCollection<string> AvailableOperations { get; } = new();

    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, string.IsNullOrWhiteSpace(value) ? "协议头" : value))
            {
                UpdateAvailableOperations();
                NotifyEditorShapeChanged();
            }
        }
    }

    public string Operation
    {
        get => _operation;
        set
        {
            if (SetProperty(ref _operation, string.IsNullOrWhiteSpace(value) ? "设置" : value))
            {
                NotifyEditorShapeChanged();
            }
        }
    }

    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value ?? ""))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }

    public string ValueType
    {
        get => _valueType;
        set => SetProperty(ref _valueType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value);
    }

    public bool ShowsKey => IsKeyTarget(Target);

    public bool ShowsValue => !IsDeleteOperation(Operation);

    public bool ShowsValueType => ShowsValue && IsBodyTarget(Target);

    public bool UsesMultilineValue => ShowsValue && IsBodyTarget(Target);

    public double ValueBoxHeight => UsesMultilineValue ? 72 : 32;

    public double ValueBoxWidth => Target switch
    {
        "Body" or "请求体" or "响应体" => 340,
        "URL" or "完整URL" => 320,
        "状态码" or "StatusCode" or "请求方法" or "Method" => 150,
        _ => 260
    };

    public string KeyLabel => Target switch
    {
        "参数" or "URL参数" or "Query" => "参数名",
        "协议头" or "请求头" or "响应头" or "Header" => "头名称",
        _ => "键名"
    };

    public string ValueLabel => Target switch
    {
        "请求方法" or "Method" => "方法",
        "URL" or "完整URL" => "新URL",
        "Path" or "路径" => "路径",
        "参数" or "URL参数" or "Query" => "参数值",
        "协议头" or "请求头" or "响应头" or "Header" => "头值",
        "状态码" or "StatusCode" => "状态码",
        "Body" or "请求体" or "响应体" => "Body",
        _ => "值"
    };

    public string EditorHint
    {
        get
        {
            if (IsDeleteOperation(Operation))
            {
                return IsKeyTarget(Target) ? "删除时只需要填写键名。" : "删除该目标内容，不需要填写值。";
            }

            return Target switch
            {
                "Body" or "请求体" or "响应体" => "替换整段 Body，支持 String/Base64/HEX。",
                "状态码" or "StatusCode" => "填写 HTTP 状态码，例如 200、404、500。",
                "URL" or "完整URL" => "填写完整 URL；相对地址会按原请求地址解析。",
                "Path" or "路径" => "只改 URL Path，不影响域名。",
                "参数" or "URL参数" or "Query" => "设置会覆盖同名参数，添加会保留同名参数。",
                "协议头" or "请求头" or "响应头" or "Header" => "设置会覆盖同名协议头，添加会追加同名协议头。",
                "请求方法" or "Method" => "填写 GET、POST、PUT 等方法名。",
                _ => "命中后按顺序执行该动作。"
            };
        }
    }

    [JsonIgnore]
    public bool IsTargetAllowedForDirection => IsTargetAllowed(Target, EditorDirection);

    [JsonIgnore]
    public string ValidationMessage
    {
        get => _validationMessage;
        set
        {
            if (SetProperty(ref _validationMessage, value ?? ""))
            {
                OnPropertyChanged(nameof(HasValidationWarning));
                OnPropertyChanged(nameof(DisplayHint));
            }
        }
    }

    [JsonIgnore]
    public bool HasValidationWarning => !string.IsNullOrWhiteSpace(ValidationMessage);

    [JsonIgnore]
    public string DisplayHint => HasValidationWarning ? ValidationMessage : EditorHint;

    public string Summary => ShowsKey && !string.IsNullOrWhiteSpace(Key)
        ? $"{Operation}{Target} · {Key}"
        : $"{Operation}{Target}";

    public void Normalize()
    {
        Target = string.IsNullOrWhiteSpace(Target) ? "协议头" : Target.Trim();
        Operation = string.IsNullOrWhiteSpace(Operation) ? "设置" : Operation.Trim();
        if (!ShowsKey)
        {
            Key = "";
        }

        if (!ShowsValue)
        {
            Value = "";
        }

        if (!ShowsValueType)
        {
            ValueType = "String(UTF8)";
        }
    }

    private void NotifyEditorShapeChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsTargetAllowedForDirection));
        OnPropertyChanged(nameof(ShowsKey));
        OnPropertyChanged(nameof(ShowsValue));
        OnPropertyChanged(nameof(ShowsValueType));
        OnPropertyChanged(nameof(UsesMultilineValue));
        OnPropertyChanged(nameof(ValueBoxHeight));
        OnPropertyChanged(nameof(ValueBoxWidth));
        OnPropertyChanged(nameof(KeyLabel));
        OnPropertyChanged(nameof(ValueLabel));
        OnPropertyChanged(nameof(EditorHint));
        OnPropertyChanged(nameof(DisplayHint));
    }

    private void UpdateAvailableOperations()
    {
        string[] operations = GetAllowedOperations(Target);
        AvailableOperations.Clear();
        foreach (string operation in operations)
        {
            AvailableOperations.Add(operation);
        }

        if (!AvailableOperations.Contains(Operation))
        {
            _operation = AvailableOperations.FirstOrDefault() ?? "设置";
            OnPropertyChanged(nameof(Operation));
        }
    }

    private static bool IsDeleteOperation(string operation)
    {
        return string.Equals(operation?.Trim(), "删除", StringComparison.Ordinal);
    }

    private static bool IsKeyTarget(string target)
    {
        return target?.Trim() is "参数" or "URL参数" or "Query" or "协议头" or "请求头" or "响应头" or "Header";
    }

    private static bool IsBodyTarget(string target)
    {
        return target?.Trim() is "Body" or "请求体" or "响应体";
    }

    private static string[] GetAllowedOperations(string target)
    {
        return target?.Trim() switch
        {
            "参数" or "URL参数" or "Query" or "协议头" or "请求头" or "响应头" or "Header" => KeyValueOperations,
            "Path" or "路径" or "Body" or "请求体" or "响应体" => SetDeleteOperations,
            _ => SetOnlyOperations
        };
    }

    private static bool IsTargetAllowed(string target, string direction)
    {
        string normalizedDirection = string.IsNullOrWhiteSpace(direction) ? "请求" : direction.Trim();
        return normalizedDirection switch
        {
            "响应" => target?.Trim() is "状态码" or "StatusCode" or "协议头" or "请求头" or "响应头" or "Header" or "Body" or "请求体" or "响应体",
            _ => target?.Trim() is "请求方法" or "Method" or "URL" or "完整URL" or "Path" or "路径" or "参数" or "URL参数" or "Query" or "协议头" or "请求头" or "响应头" or "Header" or "Body" or "请求体" or "响应体"
        };
    }
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
        set
        {
            if (SetRuleProperty(ref _mappingType, string.IsNullOrWhiteSpace(value) ? "本地文件" : value))
            {
                OnPropertyChanged(nameof(DisplayValueType));
            }
        }
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
        set
        {
            if (SetRuleProperty(ref _valueType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value))
            {
                OnPropertyChanged(nameof(DisplayValueType));
            }
        }
    }

    public bool LegacyReplaceRule
    {
        get => _legacyReplaceRule;
        set
        {
            if (SetRuleProperty(ref _legacyReplaceRule, value))
            {
                OnPropertyChanged(nameof(DisplayValueType));
            }
        }
    }

    public string DisplayValueType => MappingType == "固定响应" || LegacyReplaceRule ? ValueType : "";

    public override string Summary => LegacyReplaceRule
        ? $"旧替换规则 · {ValueType}"
        : MappingType == "固定响应"
            ? $"{MappingType} · {ValueType}"
            : MappingType;
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
