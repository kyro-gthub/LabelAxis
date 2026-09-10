using System.Windows;

namespace LabelMaker.App.Views;

public partial class TextReportWindow : Window
{
    public TextReportWindow(string title, string content)
    {
        InitializeComponent();
        Title = title;
        Body.Text = content;
    }
}
