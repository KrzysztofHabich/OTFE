using System.Windows;
using OTFE.Models;
using OTFE.ViewModels;

namespace OTFE;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void GanttChart_SpanSelected(object sender, RoutedPropertyChangedEventArgs<Span?> e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedSpan = e.NewValue;
        }
    }
}