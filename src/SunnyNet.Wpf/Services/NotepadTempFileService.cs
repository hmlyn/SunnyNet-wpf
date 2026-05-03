using System.Diagnostics;
using System.IO;
using System.Text;

namespace SunnyNet.Wpf.Services;

public static class NotepadTempFileService
{
    private const string DirectoryName = "SunnyNetWpf";
    private const string FilePrefix = "sunnynet-view-";
    private const string FileExtension = ".txt";
    private const int RetentionHours = 12;
    private const int MaxRetainedFiles = 80;

    public static void OpenText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        CleanupExpiredFiles();

        string directory = GetTempDirectory();
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, $"{FilePrefix}{DateTime.Now:yyyyMMdd-HHmmss-fff}{FileExtension}");
        File.WriteAllText(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ProcessStartInfo startInfo = new("notepad.exe")
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(filePath);
        Process.Start(startInfo);
    }

    public static void CleanupExpiredFiles()
    {
        try
        {
            string directory = GetTempDirectory();
            if (!Directory.Exists(directory))
            {
                return;
            }

            FileInfo[] files = Directory
                .EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}", SearchOption.TopDirectoryOnly)
                .Select(static path => new FileInfo(path))
                .OrderByDescending(static file => file.LastWriteTimeUtc)
                .ToArray();

            DateTime expireBefore = DateTime.UtcNow.AddHours(-RetentionHours);
            for (int index = 0; index < files.Length; index++)
            {
                FileInfo file = files[index];
                if (file.LastWriteTimeUtc >= expireBefore && index < MaxRetainedFiles)
                {
                    continue;
                }

                TryDelete(file);
            }
        }
        catch
        {
        }
    }

    private static string GetTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), DirectoryName);
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            if (file.Exists)
            {
                file.Delete();
            }
        }
        catch
        {
        }
    }
}
