using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class AppConfigState : ViewModelBase
{
    private int _port = 2024;
    private bool _disableUdp;
    private bool _disableTcp;
    private bool _disableCache;
    private bool _authentication;
    private string _globalProxy = "";
    private string _globalProxyRules = "";
    private bool _mustTcpOpen;
    private string _mustTcpRules = "";
    private string _certMode = "默认证书";
    private string _caFilePath = "";
    private string _keyFilePath = "";
    private string _scriptCode = "";
    private string _hostsRules = "";
    private string _replaceRules = "";
    private string _interceptRules = "";
    private string _ruleCenterRules = "";
    private string _goos = "windows";
    private bool _isDarkTheme;

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public bool DisableUdp
    {
        get => _disableUdp;
        set => SetProperty(ref _disableUdp, value);
    }

    public bool DisableTcp
    {
        get => _disableTcp;
        set => SetProperty(ref _disableTcp, value);
    }

    public bool DisableCache
    {
        get => _disableCache;
        set => SetProperty(ref _disableCache, value);
    }

    public bool Authentication
    {
        get => _authentication;
        set => SetProperty(ref _authentication, value);
    }

    public string GlobalProxy
    {
        get => _globalProxy;
        set => SetProperty(ref _globalProxy, value ?? "");
    }

    public string GlobalProxyRules
    {
        get => _globalProxyRules;
        set => SetProperty(ref _globalProxyRules, value ?? "");
    }

    public bool MustTcpOpen
    {
        get => _mustTcpOpen;
        set => SetProperty(ref _mustTcpOpen, value);
    }

    public string MustTcpRules
    {
        get => _mustTcpRules;
        set => SetProperty(ref _mustTcpRules, value ?? "");
    }

    public string CertMode
    {
        get => _certMode;
        set => SetProperty(ref _certMode, value ?? "默认证书");
    }

    public string CaFilePath
    {
        get => _caFilePath;
        set => SetProperty(ref _caFilePath, value ?? "");
    }

    public string KeyFilePath
    {
        get => _keyFilePath;
        set => SetProperty(ref _keyFilePath, value ?? "");
    }

    public string ScriptCode
    {
        get => _scriptCode;
        set => SetProperty(ref _scriptCode, value ?? "");
    }

    public string HostsRules
    {
        get => _hostsRules;
        set => SetProperty(ref _hostsRules, value ?? "");
    }

    public string ReplaceRules
    {
        get => _replaceRules;
        set => SetProperty(ref _replaceRules, value ?? "");
    }

    public string InterceptRules
    {
        get => _interceptRules;
        set => SetProperty(ref _interceptRules, value ?? "");
    }

    public string RuleCenterRules
    {
        get => _ruleCenterRules;
        set => SetProperty(ref _ruleCenterRules, value ?? "");
    }

    public string GOOS
    {
        get => _goos;
        set => SetProperty(ref _goos, value ?? "windows");
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }
}
