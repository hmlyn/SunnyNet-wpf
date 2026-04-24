using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class ReplaceRuleItem : ViewModelBase
{
    private string _hash = Guid.NewGuid().ToString("N");
    private string _ruleType = "String(UTF8)";
    private string _sourceContent = "";
    private string _replacementContent = "";
    private string _state = "未保存";

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public string RuleType
    {
        get => _ruleType;
        set
        {
            if (SetProperty(ref _ruleType, string.IsNullOrWhiteSpace(value) ? "String(UTF8)" : value))
            {
                State = "未保存";
                OnPropertyChanged(nameof(UsesResponseFile));
            }
        }
    }

    public string SourceContent
    {
        get => _sourceContent;
        set
        {
            if (SetProperty(ref _sourceContent, value ?? ""))
            {
                State = "未保存";
            }
        }
    }

    public string ReplacementContent
    {
        get => _replacementContent;
        set
        {
            if (SetProperty(ref _replacementContent, value ?? ""))
            {
                State = "未保存";
            }
        }
    }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value ?? "");
    }

    public bool UsesResponseFile => RuleType == "响应文件";
}
