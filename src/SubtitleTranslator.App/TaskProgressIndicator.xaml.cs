using System.Windows;
using System.Windows.Controls;

namespace SubtitleTranslator.App;

public partial class TaskProgressIndicator : UserControl
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TaskProgressIndicator), new PropertyMetadata(0d));

    public TaskProgressIndicator() => InitializeComponent();

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
}
