using System.Windows;

namespace AfterSchoolManager.Views;

public partial class WorkspaceDialog : Window
{
    public string WorkspaceName => NameBox.Text.Trim();
    public int AcademicYear { get; private set; }
    public DateTime StartDate => StartPicker.SelectedDate!.Value;
    public DateTime EndDate => EndPicker.SelectedDate!.Value;

    public WorkspaceDialog()
    {
        InitializeComponent();
        var today = DateTime.Today;
        YearBox.Text = (today.Month <= 2 ? today.Year - 1 : today.Year).ToString();
        StartPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        EndPicker.SelectedDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkspaceName)) { MessageBox.Show("작업공간명을 입력하세요."); return; }
        if (!int.TryParse(YearBox.Text, out var year) || year is < 2000 or > 2200) { MessageBox.Show("학년도를 올바르게 입력하세요."); return; }
        if (StartPicker.SelectedDate is null || EndPicker.SelectedDate is null || EndDate < StartDate) { MessageBox.Show("시작일과 종료일을 확인하세요."); return; }
        AcademicYear = year; DialogResult = true;
    }
}
