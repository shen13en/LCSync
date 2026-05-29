using System.Windows;
using LCSync.ViewModels;

namespace LCSync.Views;

public partial class TeacherWindow : Window
{
    public TeacherWindow()
    {
        InitializeComponent();
        DataContext = new TeacherViewModel(this);
    }
}
