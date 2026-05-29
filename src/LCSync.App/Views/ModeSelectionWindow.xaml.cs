using System.Windows;
using LCSync.ViewModels;

namespace LCSync.Views;

public partial class ModeSelectionWindow : Window
{
    public ModeSelectionWindow()
    {
        InitializeComponent();
        DataContext = new ModeSelectionViewModel(this);
    }
}
