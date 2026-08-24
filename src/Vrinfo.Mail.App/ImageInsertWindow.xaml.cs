using System.Windows;

namespace Vrinfo.Mail.App;

public partial class ImageInsertWindow : System.Windows.Window
{
    public int MaxWidthPx => (int)WidthSlider.Value;
    public int JpegQuality => (int)QualitySlider.Value;

    public ImageInsertWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
