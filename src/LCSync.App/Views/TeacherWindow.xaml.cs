using System.Windows;
using LCSync.ViewModels;

namespace LCSync.Views;

public partial class TeacherWindow : Window
{
    private TeacherViewModel? _viewModel;

    public TeacherWindow()
    {
        InitializeComponent();
        _viewModel = new TeacherViewModel(this);
        DataContext = _viewModel;
    }

    private void OnDeleteSharedFile(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string fileId)
        {
            _viewModel?.DeleteSharedFile(fileId);
        }
    }
}
