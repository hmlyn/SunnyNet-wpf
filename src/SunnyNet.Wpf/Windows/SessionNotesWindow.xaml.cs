using System.Windows;
using System.Windows.Input;

namespace SunnyNet.Wpf.Windows;

public partial class SessionNotesWindow : Window
{
    public SessionNotesWindow(string initialNotes, int sessionCount)
    {
        InitializeComponent();
        NotesTextBox.Text = initialNotes;
        SubtitleTextBlock.Text = sessionCount > 1
            ? $"将覆盖选中的 {sessionCount} 条会话备注，留空保存即可清除备注。"
            : "留空保存即可清除备注。";
        Loaded += (_, _) =>
        {
            NotesTextBox.Focus();
            NotesTextBox.SelectAll();
        };
    }

    public string NotesText { get; private set; } = "";

    private void Save_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SaveAndClose();
    }

    private void Cancel_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DialogResult = false;
        Close();
    }

    private void SessionNotesWindow_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveAndClose();
            keyEventArgs.Handled = true;
        }
    }

    private void SaveAndClose()
    {
        NotesText = NotesTextBox.Text ?? "";
        DialogResult = true;
        Close();
    }
}
