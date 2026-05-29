using System.Windows;
using System.Windows.Input;

namespace LCSync.ViewModels;

public class ModeSelectionViewModel : ViewModelBase
{
    private readonly Window _window;

    public ICommand OpenTeacherModeCommand { get; }
    public ICommand OpenStudentModeCommand { get; }

    public ModeSelectionViewModel(Window window)
    {
        _window = window;

        OpenTeacherModeCommand = new RelayCommand(OpenTeacherMode);
        OpenStudentModeCommand = new RelayCommand(OpenStudentMode);
    }

    private void OpenTeacherMode()
    {
        var teacherWindow = new Views.TeacherWindow();
        teacherWindow.Closed += (s, e) => _window.Close();
        _window.Hide();
        teacherWindow.Show();
    }

    private void OpenStudentMode()
    {
        var studentWindow = new Views.StudentWindow();
        studentWindow.Closed += (s, e) => _window.Close();
        _window.Hide();
        studentWindow.Show();
    }
}
