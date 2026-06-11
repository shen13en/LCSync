using System;
using System.Windows;
using System.Windows.Interop;
using LCSync.ViewModels;

namespace LCSync.Views;

public partial class StudentWindow : Window
{
    private WindowStyle _originalWindowStyle;
    private ResizeMode _originalResizeMode;
    private WindowState _originalWindowState;
    private bool _isFullscreen = false;
    private StudentViewModel? _viewModel;

    public StudentWindow()
    {
        InitializeComponent();
        _viewModel = new StudentViewModel(this);
        _viewModel.Disconnected += OnViewModelDisconnected;
        DataContext = _viewModel;
    }

    private void OnViewModelDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_isFullscreen)
            {
                ExitFullscreen();
            }
        });
    }

    private void OnDownloadFile(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string fileId)
        {
            _viewModel?.DownloadFile(fileId);
        }
    }

    private void ToggleFullscreen(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
        }
        else
        {
            EnterFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        _originalWindowStyle = WindowStyle;
        _originalResizeMode = ResizeMode;
        _originalWindowState = WindowState;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
    }

    private void ExitFullscreen()
    {
        WindowStyle = _originalWindowStyle;
        ResizeMode = _originalResizeMode;
        WindowState = _originalWindowState;
        _isFullscreen = false;
    }
}
