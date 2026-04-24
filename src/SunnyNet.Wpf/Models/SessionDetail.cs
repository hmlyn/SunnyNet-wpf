using System.Collections.ObjectModel;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class SessionDetail : ViewModelBase
{
    private bool _hasSelection;
    private bool _isSocketSession;
    private int _inlineInterceptMode;
    private bool _syncingEditableRaw = true;
    private string _requestMethod = "";
    private string _requestUrl = "";
    private string _requestHeaders = "";
    private string _requestBody = "";
    private string _requestRaw = "";
    private string _editableRequestRaw = "";
    private string _requestQuery = "";
    private string _requestHex = "";
    private string _requestCookies = "";
    private string _requestJson = "";
    private string _requestImageText = "暂无图片请求";
    private bool _hasRequestBodyRows;
    private byte[] _requestHexBytes = Array.Empty<byte>();
    private int _requestHexHeaderLength;
    private byte[] _requestImageBytes = Array.Empty<byte>();
    private string _requestImageType = "";
    private string _responseHeaders = "";
    private string _responseBody = "";
    private string _responseRaw = "";
    private string _editableResponseRaw = "";
    private string _responseText = "";
    private string _responseHex = "";
    private string _responseCookies = "";
    private string _responseJson = "";
    private string _responseHtml = "";
    private string _responseImageText = "暂无图片响应";
    private byte[] _responseHexBytes = Array.Empty<byte>();
    private int _responseHexHeaderLength;
    private byte[] _responseImageBytes = Array.Empty<byte>();
    private string _responseImageType = "";
    private int _responseStateCode;
    private string _responseStateText = "";
    private string _summary = "请选择一个会话";
    private SocketEntry? _selectedSocketEntry;

    public bool HasSelection
    {
        get => _hasSelection;
        set => SetProperty(ref _hasSelection, value);
    }

    public string RequestMethod
    {
        get => _requestMethod;
        set => SetProperty(ref _requestMethod, value);
    }

    public string RequestUrl
    {
        get => _requestUrl;
        set => SetProperty(ref _requestUrl, value);
    }

    public string RequestHeaders
    {
        get => _requestHeaders;
        set => SetProperty(ref _requestHeaders, value);
    }

    public string RequestBody
    {
        get => _requestBody;
        set => SetProperty(ref _requestBody, value);
    }

    public string RequestRaw
    {
        get => _requestRaw;
        set
        {
            if (SetProperty(ref _requestRaw, value))
            {
                if (_syncingEditableRaw)
                {
                    EditableRequestRaw = value;
                }
            }
        }
    }

    public string EditableRequestRaw
    {
        get => _editableRequestRaw;
        set => SetProperty(ref _editableRequestRaw, value ?? "");
    }

    public string RequestQuery
    {
        get => _requestQuery;
        set => SetProperty(ref _requestQuery, value);
    }

    public string RequestHex
    {
        get => _requestHex;
        set => SetProperty(ref _requestHex, value);
    }

    public string RequestCookies
    {
        get => _requestCookies;
        set => SetProperty(ref _requestCookies, value);
    }

    public string RequestJson
    {
        get => _requestJson;
        set => SetProperty(ref _requestJson, value);
    }

    public string RequestImageText
    {
        get => _requestImageText;
        set => SetProperty(ref _requestImageText, value);
    }

    public bool HasRequestBodyRows
    {
        get => _hasRequestBodyRows;
        set => SetProperty(ref _hasRequestBodyRows, value);
    }

    public byte[] RequestHexBytes
    {
        get => _requestHexBytes;
        set => SetProperty(ref _requestHexBytes, value);
    }

    public int RequestHexHeaderLength
    {
        get => _requestHexHeaderLength;
        set => SetProperty(ref _requestHexHeaderLength, value);
    }

    public byte[] RequestImageBytes
    {
        get => _requestImageBytes;
        set => SetProperty(ref _requestImageBytes, value);
    }

    public string RequestImageType
    {
        get => _requestImageType;
        set => SetProperty(ref _requestImageType, value);
    }

    public string ResponseHeaders
    {
        get => _responseHeaders;
        set => SetProperty(ref _responseHeaders, value);
    }

    public string ResponseBody
    {
        get => _responseBody;
        set => SetProperty(ref _responseBody, value);
    }

    public string ResponseRaw
    {
        get => _responseRaw;
        set
        {
            if (SetProperty(ref _responseRaw, value))
            {
                if (_syncingEditableRaw)
                {
                    EditableResponseRaw = value;
                }
            }
        }
    }

    public string EditableResponseRaw
    {
        get => _editableResponseRaw;
        set => SetProperty(ref _editableResponseRaw, value ?? "");
    }

    public string ResponseText
    {
        get => _responseText;
        set => SetProperty(ref _responseText, value);
    }

    public string ResponseHex
    {
        get => _responseHex;
        set => SetProperty(ref _responseHex, value);
    }

    public string ResponseCookies
    {
        get => _responseCookies;
        set => SetProperty(ref _responseCookies, value);
    }

    public string ResponseJson
    {
        get => _responseJson;
        set => SetProperty(ref _responseJson, value);
    }

    public string ResponseHtml
    {
        get => _responseHtml;
        set => SetProperty(ref _responseHtml, value);
    }

    public string ResponseImageText
    {
        get => _responseImageText;
        set => SetProperty(ref _responseImageText, value);
    }

    public byte[] ResponseHexBytes
    {
        get => _responseHexBytes;
        set => SetProperty(ref _responseHexBytes, value);
    }

    public int ResponseHexHeaderLength
    {
        get => _responseHexHeaderLength;
        set => SetProperty(ref _responseHexHeaderLength, value);
    }

    public byte[] ResponseImageBytes
    {
        get => _responseImageBytes;
        set => SetProperty(ref _responseImageBytes, value);
    }

    public string ResponseImageType
    {
        get => _responseImageType;
        set => SetProperty(ref _responseImageType, value);
    }

    public int ResponseStateCode
    {
        get => _responseStateCode;
        set => SetProperty(ref _responseStateCode, value);
    }

    public string ResponseStateText
    {
        get => _responseStateText;
        set => SetProperty(ref _responseStateText, value);
    }

    public bool IsSocketSession
    {
        get => _isSocketSession;
        set => SetProperty(ref _isSocketSession, value);
    }

    public int InlineInterceptMode
    {
        get => _inlineInterceptMode;
        set
        {
            if (SetProperty(ref _inlineInterceptMode, value))
            {
                OnPropertyChanged(nameof(IsRequestInlineEditing));
                OnPropertyChanged(nameof(IsResponseInlineEditing));
            }
        }
    }

    public bool IsRequestInlineEditing => InlineInterceptMode == 1;

    public bool IsResponseInlineEditing => InlineInterceptMode == 2;

    public void EnableInlineIntercept(int mode)
    {
        EditableRequestRaw = RequestRaw;
        EditableResponseRaw = ResponseRaw;
        _syncingEditableRaw = false;
        InlineInterceptMode = mode;
    }

    public void DisableInlineIntercept()
    {
        InlineInterceptMode = 0;
        _syncingEditableRaw = true;
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public SocketEntry? SelectedSocketEntry
    {
        get => _selectedSocketEntry;
        set => SetProperty(ref _selectedSocketEntry, value);
    }

    public ObservableCollection<SocketEntry> SocketEntries { get; } = new();

    public ObservableCollection<DetailNameValueRow> RequestHeaderRows { get; } = new();

    public ObservableCollection<DetailNameValueRow> RequestQueryRows { get; } = new();

    public ObservableCollection<DetailNameValueRow> RequestCookieRows { get; } = new();

    public ObservableCollection<DetailNameValueRow> RequestBodyRows { get; } = new();

    public ObservableCollection<HexViewRow> RequestHexRows { get; } = new();

    public ObservableCollection<DetailNameValueRow> ResponseHeaderRows { get; } = new();

    public ObservableCollection<DetailNameValueRow> ResponseCookieRows { get; } = new();

    public ObservableCollection<HexViewRow> ResponseHexRows { get; } = new();

    public void Clear()
    {
        HasSelection = false;
        RequestMethod = "";
        RequestUrl = "";
        RequestHeaders = "";
        RequestBody = "";
        RequestRaw = "";
        EditableRequestRaw = "";
        RequestQuery = "";
        RequestHex = "";
        RequestCookies = "";
        RequestJson = "";
        RequestImageText = "暂无图片请求";
        HasRequestBodyRows = false;
        RequestHexBytes = Array.Empty<byte>();
        RequestHexHeaderLength = 0;
        RequestImageBytes = Array.Empty<byte>();
        RequestImageType = "";
        ResponseHeaders = "";
        ResponseBody = "";
        ResponseRaw = "";
        EditableResponseRaw = "";
        ResponseText = "";
        ResponseHex = "";
        ResponseCookies = "";
        ResponseJson = "";
        ResponseHtml = "";
        ResponseImageText = "暂无图片响应";
        ResponseHexBytes = Array.Empty<byte>();
        ResponseHexHeaderLength = 0;
        ResponseImageBytes = Array.Empty<byte>();
        ResponseImageType = "";
        ResponseStateCode = 0;
        ResponseStateText = "";
        IsSocketSession = false;
        DisableInlineIntercept();
        Summary = "请选择一个会话";
        SelectedSocketEntry = null;
        SocketEntries.Clear();
        RequestHeaderRows.Clear();
        RequestQueryRows.Clear();
        RequestCookieRows.Clear();
        RequestBodyRows.Clear();
        RequestHexRows.Clear();
        ResponseHeaderRows.Clear();
        ResponseCookieRows.Clear();
        ResponseHexRows.Clear();
    }
}
