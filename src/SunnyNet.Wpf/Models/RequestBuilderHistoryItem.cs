using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class RequestBuilderHistoryItem : ViewModelBase
{
    private string _id = Guid.NewGuid().ToString("N");
    private DateTimeOffset _createdAt = DateTimeOffset.Now;
    private string _method = "GET";
    private string _url = "";
    private string _headers = "";
    private string _body = "";
    private string _bodyFormat = "文本";
    private string _httpVersion = "HTTP/1.1";
    private string _notes = "";

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value ?? Guid.NewGuid().ToString("N"));
    }

    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetProperty(ref _createdAt, value))
            {
                OnPropertyChanged(nameof(DisplayTime));
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Method
    {
        get => _method;
        set
        {
            if (SetProperty(ref _method, value ?? "GET"))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Headers
    {
        get => _headers;
        set => SetProperty(ref _headers, value ?? "");
    }

    public string Body
    {
        get => _body;
        set => SetProperty(ref _body, value ?? "");
    }

    public string BodyFormat
    {
        get => _bodyFormat;
        set => SetProperty(ref _bodyFormat, string.IsNullOrWhiteSpace(value) ? "文本" : value);
    }

    public string HttpVersion
    {
        get => _httpVersion;
        set => SetProperty(ref _httpVersion, string.IsNullOrWhiteSpace(value) ? "HTTP/1.1" : value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? "");
    }

    public string DisplayTime => $"{CreatedAt:MM-dd HH:mm:ss}";

    public string DisplayName => $"{DisplayTime}  {Method}  {Url}";
}
