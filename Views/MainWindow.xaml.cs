using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Git2Auditor.Models;

namespace Git2Auditor.Views;

public partial class MainWindow : Window
{
    public MainWindow(ViewModels.MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupsListBox.SelectedItem is GroupHealthData selectedGroup)
        {
            var firstStudent = StudentsGrid.Items.Cast<StudentPerformanceRecord>()
                .FirstOrDefault(r => r.Student.GroupId == selectedGroup.GroupId);
            
            if (firstStudent != null)
            {
                // WPF 技巧：先滚动到最后一行，再滚动到目标行，通常能将目标行置于顶部
                if (StudentsGrid.Items.Count > 0)
                {
                    StudentsGrid.ScrollIntoView(StudentsGrid.Items[StudentsGrid.Items.Count - 1]);
                    StudentsGrid.UpdateLayout();
                }
                StudentsGrid.ScrollIntoView(firstStudent);
            }
        }
    }
}
