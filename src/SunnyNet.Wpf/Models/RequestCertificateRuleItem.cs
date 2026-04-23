using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class RequestCertificateRuleItem : ViewModelBase
{
    private int _context;
    private string _usageRule = "解析及发送";
    private string _host = "";
    private string _certificateFile = "";
    private string _password = "";
    private string _loadState = "未载入";

    public int Context
    {
        get => _context;
        set => SetProperty(ref _context, value);
    }

    public string UsageRule
    {
        get => _usageRule;
        set
        {
            if (SetProperty(ref _usageRule, string.IsNullOrWhiteSpace(value) ? "解析及发送" : value))
            {
                LoadState = "未载入";
            }
        }
    }

    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value ?? ""))
            {
                LoadState = "未载入";
            }
        }
    }

    public string CertificateFile
    {
        get => _certificateFile;
        set
        {
            if (SetProperty(ref _certificateFile, value ?? ""))
            {
                LoadState = "未载入";
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? ""))
            {
                LoadState = "未载入";
            }
        }
    }

    public string LoadState
    {
        get => _loadState;
        set => SetProperty(ref _loadState, value ?? "");
    }
}
