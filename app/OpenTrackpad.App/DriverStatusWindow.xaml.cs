using System.Windows;

namespace OpenTrackpad.App;

public partial class DriverStatusWindow : Window
{
    public DriverStatusWindow(string statusText)
    {
        InitializeComponent();
        OutputBox.Text = statusText;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OutputBox.Text);
        }
        catch
        {
            // Clipboard can be temporarily locked by another process.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
