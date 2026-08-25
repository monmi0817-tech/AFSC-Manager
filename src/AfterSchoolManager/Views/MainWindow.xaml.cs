using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using AfterSchoolManager.Models;
using AfterSchoolManager.Services;
using AfterSchoolManager.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace AfterSchoolManager.Views;

public partial class MainWindow : Window
{
    private readonly DatabaseService _db = new(AppPaths.DatabasePath);
    private readonly ExcelService _excel;
    private readonly SettlementService _settlement = new(AppPaths.DatabasePath);
    private readonly ProposalService _proposal = new(AppPaths.DatabasePath);
    private readonly SettingsService _settingsService = new();
    private readonly BackupService _backup = new(AppPaths.DatabasePath);
    private readonly UpdateService _updates = new();
    private readonly DispatcherTimer _studentSearchTimer = new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly DispatcherTimer _enrollmentSearchTimer = new(){Interval=TimeSpan.FromMilliseconds(250)};
    private AppSettingsItem _settings = new();
    private UpdateInfoItem? _availableUpdate;
    private bool _ready;
    private bool _syncingStudentSelectors;
    private IReadOnlyList<StudentItem> _studentChoices=Array.Empty<StudentItem>();
    private Button? _activeNavButton;
    private WorkspaceItem? Current => WorkspaceCombo.SelectedItem as WorkspaceItem;

    public MainWindow()
    {
        _excel = new ExcelService(_db);
        InitializeComponent();
        ConfigureDataGridAlignment();
        _settings=_settingsService.Load();BackupDirectoryBox.Text=_settings.BackupDirectory;
        AppVersionText.Text=$"버전 {GetAppVersion()}";DataLocationText.Text=AppPaths.DatabasePath;
        _studentSearchTimer.Tick+=(_,_)=>{_studentSearchTimer.Stop();if(_ready&&Current is not null)StudentsGrid.ItemsSource=_db.GetStudents(Current.AcademicYearId,StudentSearchBox.Text);};
        _enrollmentSearchTimer.Tick+=(_,_)=>{_enrollmentSearchTimer.Stop();if(_ready&&Current is not null)EnrollmentGrid.ItemsSource=_db.GetEnrollments(Current.Id,EnrollmentSearchBox.Text);};
        ProposalFeeTypeCombo.ItemsSource=ProposalService.FeeTypes;
        ProposalFeeTypeCombo.SelectedIndex=0;
        ContentTabs.SelectedIndex = 0;
        _activeNavButton=DashboardNavButton;
        DashboardNavButton.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72,103,212));
        DashboardNavButton.Foreground=System.Windows.Media.Brushes.White;
        LoadWorkspaces();
        _ready = true;
        if (WorkspaceCombo.Items.Count == 0) Dispatcher.BeginInvoke(new Action(NewWorkspacePrompt));
        else
        {
            WorkspaceCombo.SelectedIndex = -1;
            WorkspaceCombo.SelectedIndex = 0;
        }
    }

    private void ConfigureDataGridAlignment()
    {
        var baseCellStyle=(Style)Application.Current.FindResource(typeof(DataGridCell));
        var amountCellStyle=new Style(typeof(DataGridCell),baseCellStyle);
        amountCellStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Right));
        amountCellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        var centerTextStyle=new Style(typeof(TextBlock));
        centerTextStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Center));
        centerTextStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch));
        centerTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));
        var amountTextStyle=new Style(typeof(TextBlock));
        amountTextStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Right));
        amountTextStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch));
        amountTextStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));

        var grids=new[]
        {
            StudentsGrid,EligibilityGrid,DepartmentGrid,EnrollmentGrid,SelfPayGrid,VoucherGrid,
            FreeVoucherGrid,SettlementMatrixGrid,BudgetOverrideGrid,StudentUsageGrid,ChangeHistoryGrid,ProposalGrid
        };
        foreach(var column in grids.SelectMany(grid=>grid.Columns).OfType<DataGridTextColumn>())
        {
            if(column.Binding is Binding binding&&binding.StringFormat?.Contains("N0",StringComparison.OrdinalIgnoreCase)==true)
            {
                column.CellStyle=amountCellStyle;
                column.ElementStyle=amountTextStyle;
            }
            else column.ElementStyle=centerTextStyle;
        }
    }

    private void LoadWorkspaces(long? selectId = null)
    {
        var items = _db.GetWorkspaces();
        WorkspaceCombo.ItemsSource = items;
        if (items.Count > 0) WorkspaceCombo.SelectedItem = selectId is null ? items[0] : items.FirstOrDefault(x => x.Id == selectId) ?? items[0];
    }

    private void NewWorkspacePrompt()
    {
        var dialog = new WorkspaceDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        TryRun(() =>
        {
            var id = _db.CreateWorkspace(dialog.WorkspaceName, dialog.AcademicYear, dialog.StartDate, dialog.EndDate);
            LoadWorkspaces(id);
        }, "작업공간을 생성했습니다.");
    }

    private void NewWorkspace_Click(object sender, RoutedEventArgs e) => NewWorkspacePrompt();

    private void WorkspaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || Current is null) return;
        WorkspacePeriodText.Text = $"{Current.AcademicYear}학년도 · {Current.Name} · {Current.PeriodText}";
        RevisionText.Text = $"원본 데이터 revision {Current.SourceRevision:N0}";
        SupportStartPicker.SelectedDate = Current.StartDate;
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (Current is null) return;
        _studentChoices=_db.GetStudents(Current.AcademicYearId);
        StudentsGrid.ItemsSource = string.IsNullOrWhiteSpace(StudentSearchBox.Text)
            ? _studentChoices : _db.GetStudents(Current.AcademicYearId, StudentSearchBox.Text);
        RefreshStudentSelectorSources();
        EligibilityGrid.ItemsSource = _db.GetEligibilities(Current.AcademicYearId);
        var departments = _db.GetDepartments(Current.AcademicYearId);
        DepartmentGrid.ItemsSource = departments;
        EnrollDepartmentCombo.ItemsSource = departments;
        EnrollmentGrid.ItemsSource = _db.GetEnrollments(Current.Id, EnrollmentSearchBox.Text);
        ChangeHistoryGrid.ItemsSource = _db.GetChangeHistory(Current.Id);
        var counts = _db.GetDashboard(Current.AcademicYearId, Current.Id);
        StudentCountText.Text = $"{counts.Students:N0}명"; EnrollmentCountText.Text = $"{counts.Enrollments:N0}명";
        DepartmentCountText.Text = $"{counts.Departments:N0}개"; SupportCountText.Text = $"{counts.Supported:N0}명";
        RefreshMvp2();
        RefreshProposal();
        RefreshRevisionHeader();
    }

    private void RefreshMvp2()
    {
        if (Current is null) return;
        var settings=_db.GetSupportSettings(Current.AcademicYearId);
        VoucherDefaultBox.Text=settings.VoucherDefault.ToString("N0");FreeDefaultBox.Text=settings.FreeVoucherDefault.ToString("N0");VoucherGradesBox.Text=settings.VoucherGrades;
        SourcePriorityCombo.SelectedItem=SourcePriorityCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(x=>x.Tag?.ToString()==settings.SourcePriority);
        BudgetOverrideGrid.ItemsSource=_db.GetBudgetOverrides(Current.AcademicYearId);

        var status=_settlement.GetStatus(Current.Id);
        SettlementStatusText.Text=status.Message;DashboardSettlementText.Text=status.Message;
        SettlementStatusText.Foreground=status.IsCurrent?System.Windows.Media.Brushes.SeaGreen:status.Exists?System.Windows.Media.Brushes.DarkOrange:System.Windows.Media.Brushes.SlateGray;
        SettlementGeneratedText.Text=status.GeneratedAt is null?"마지막 생성: 없음":$"마지막 생성: {status.GeneratedAt:yyyy-MM-dd HH:mm:ss}";

        var selfPay=_settlement.GetSelfPayResults(Current.Id);SelfPayGrid.ItemsSource=selfPay;
        var voucher=_settlement.GetVoucherResults(Current.Id);VoucherGrid.ItemsSource=voucher;
        var free=_settlement.GetFreeVoucherResults(Current.Id);FreeVoucherGrid.ItemsSource=free;
        var matrix=_settlement.GetResourceMatrix(Current.Id);SettlementMatrixGrid.ItemsSource=matrix;
        SelfPayInstructorTotalText.Text=Won(selfPay.Sum(x=>x.InstructorFee));SelfPayOperatingTotalText.Text=Won(selfPay.Sum(x=>x.OperatingFee));
        SelfPayTextbookTotalText.Text=Won(selfPay.Sum(x=>x.TextbookFee));SelfPayMaterialTotalText.Text=Won(selfPay.Sum(x=>x.MaterialFee));SelfPayGrandTotalText.Text=Won(selfPay.Sum(x=>x.Total));
        var voucherUsed=voucher.Sum(x=>x.VoucherTotal);var voucherOver=voucher.Sum(x=>x.OverTotal);
        VoucherCurrentTotalText.Text=Won(voucherUsed);VoucherCumulativeTotalText.Text=Won(voucherUsed+voucherOver);VoucherOverTotalText.Text=Won(voucherOver);
        FreeTotalText.Text=Won(free.Sum(x=>x.Total));
        SettlementSelfPayTotalText.Text=Won(matrix.Sum(x=>x.SelfPayAmount));SettlementVoucherTotalText.Text=Won(matrix.Sum(x=>x.VoucherAmount));
        SettlementOverTotalText.Text=Won(matrix.Sum(x=>x.VoucherOverAmount));SettlementFreeTotalText.Text=Won(matrix.Sum(x=>x.FreeVoucherAmount));
    }

    private static string Won(long amount)=>$"{amount:N0}원";

    private void RefreshProposal()
    {
        if(Current is null||ProposalFeeTypeCombo.SelectedItem is not ProposalFeeTypeItem feeType)return;
        var labels=_proposal.GetLabels(Current.AcademicYearId);
        ProposalSelfPayLabel.Text=labels.SelfPayHeader;ProposalOverLabel.Text=labels.VoucherOverHeader;ProposalVoucherLabel.Text=labels.VoucherHeader;
        ProposalSelfPayColumn.Header=labels.SelfPayHeader;ProposalOverColumn.Header=labels.VoucherOverHeader;ProposalVoucherColumn.Header=labels.VoucherHeader;
        var status=_settlement.GetStatus(Current.Id);
        ProposalStatusText.Text=status.IsCurrent
            ? $"최신 정산 기준 · {status.GeneratedAt:yyyy-MM-dd HH:mm:ss}"
            : "품의자료를 만들려면 최신 정산 데이터가 필요합니다.";
        if(!status.IsCurrent)
        {
            ProposalGrid.ItemsSource=Array.Empty<ProposalDepartmentItem>();SetProposalTotals(Array.Empty<ProposalDepartmentItem>());return;
        }
        try
        {
            var rows=_proposal.GetDepartmentSummary(Current.Id,feeType.Code);ProposalGrid.ItemsSource=rows;SetProposalTotals(rows);
        }
        catch(Exception ex)
        {
            ProposalStatusText.Text=$"품의 집계를 불러오지 못했습니다: {ex.Message}";
            ProposalGrid.ItemsSource=Array.Empty<ProposalDepartmentItem>();SetProposalTotals(Array.Empty<ProposalDepartmentItem>());
        }
    }

    private void SetProposalTotals(IEnumerable<ProposalDepartmentItem> rows)
    {
        var items=rows.ToArray();ProposalSelfPayTotalText.Text=Won(items.Sum(x=>x.SelfPayAmount));ProposalOverTotalText.Text=Won(items.Sum(x=>x.VoucherOverAmount));
        ProposalVoucherTotalText.Text=Won(items.Sum(x=>x.VoucherAmount));ProposalFreeTotalText.Text=Won(items.Sum(x=>x.FreeVoucherAmount));ProposalGrandTotalText.Text=Won(items.Sum(x=>x.TotalAmount));
    }

    private void ProposalFeeTypeCombo_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {if(_ready)RefreshProposal();}

    private void ExportProposal_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||ProposalFeeTypeCombo.SelectedItem is not ProposalFeeTypeItem feeType)return;
        try
        {
            var status=_settlement.GetStatus(Current.Id);
            if(!status.IsCurrent||status.GeneratedAt is null)throw new InvalidOperationException("최신 정산 데이터가 없습니다. [정산 데이터 생성]을 먼저 실행하세요.");
            var labels=_proposal.GetLabels(Current.AcademicYearId);var rows=_proposal.GetDepartmentSummary(Current.Id,feeType.Code);
            var safeName=string.Concat(Current.Name.Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch));
            var dialog=new SaveFileDialog{FileName=$"{Current.AcademicYear}학년도_{safeName}_{feeType.DisplayName}_품의자료.xlsx",Filter="Excel 통합 문서 (*.xlsx)|*.xlsx",AddExtension=true};
            if(dialog.ShowDialog()!=true)return;
            _excel.ExportProposal(dialog.FileName,Current,feeType,labels,rows,status.GeneratedAt.Value);
            MessageBox.Show($"부서 {rows.Count:N0}개 품의자료를 저장했습니다.\n\n{dialog.FileName}","완료",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void Nav_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button||!int.TryParse(button.Tag?.ToString(),out var index))return;
        if(_activeNavButton is not null){_activeNavButton.ClearValue(Control.BackgroundProperty);_activeNavButton.ClearValue(Control.ForegroundProperty);}
        _activeNavButton=button;button.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72,103,212));button.Foreground=System.Windows.Media.Brushes.White;
        ContentTabs.SelectedIndex=index;
    }

    private void GenerateSettlement_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        if(!Confirm("현재 수강 원본과 지원금 설정으로 정산 데이터를 생성할까요?\n기존 활성 정산 결과는 새 결과로 교체됩니다."))return;
        TryRun(()=>_settlement.Generate(Current.Id),"정산 데이터를 생성했습니다.");
    }

    private void SaveSupportSettings_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        var priority=(SourcePriorityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"VOUCHER_FIRST";
        TryRun(()=>_db.SaveSupportSettings(Current.AcademicYearId,Money(VoucherDefaultBox,"방과후 이용권 기본 한도"),Money(FreeDefaultBox,"자유수강권 기본 한도"),priority,VoucherGradesBox.Text),"학년도 지원금 정책을 저장했습니다. 기존 정산은 다시 생성해야 합니다.");
    }

    private void SaveBudgetOverride_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        TryRun(()=>
        {
            var student=_db.FindStudent(Current.AcademicYearId,Int(BudgetGradeBox,"학년"),BudgetClassBox.Text,Int(BudgetNumberBox,"번호"),BudgetNameBox.Text);
            var code=(BudgetProgramCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()??throw new InvalidOperationException("지원제도를 선택하세요.");
            _db.SaveBudgetOverride(Current.AcademicYearId,student.Id,code,Money(BudgetAmountBox,"개별 한도"),BudgetReasonBox.Text);
        },"학생별 지원 한도를 저장했습니다. 기존 정산은 다시 생성해야 합니다.");
    }

    private void DeleteBudgetOverride_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||BudgetOverrideGrid.SelectedItem is not BudgetOverrideItem item){MessageBox.Show("삭제할 학생별 한도를 선택하세요.");return;}
        if(!Confirm($"{item.StudentName} 학생의 {item.ProgramName} 개별 한도를 삭제하고 기본 한도를 적용할까요?"))return;
        TryRun(()=>_db.DeleteBudgetOverride(Current.AcademicYearId,item.Id),"학생별 한도를 삭제했습니다.");
    }

    private void RefreshRevisionHeader()
    {
        if (Current is null) return;
        var refreshed = _db.GetWorkspaces().FirstOrDefault(x => x.Id == Current.Id);
        if (refreshed is not null) RevisionText.Text = $"원본 데이터 revision {refreshed.SourceRevision:N0}";
    }

    private bool EnsureWorkspace()
    {
        if (Current is not null) return true;
        MessageBox.Show("먼저 작업공간을 생성하거나 선택하세요."); return false;
    }

    private void AddStudent_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureWorkspace()) return;
        var dialog=new RecordEditorDialog(RecordEditorMode.Student,"학생정보 추가",_studentChoices){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(() => _db.AddStudent(Current!.AcademicYearId,dialog.Grade,dialog.ClassName,dialog.StudentNumber,dialog.StudentName,dialog.Note),"학생을 추가했습니다.");
    }

    private void UpdateStudent_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || StudentsGrid.SelectedItem is not StudentItem item) { MessageBox.Show("수정할 학생을 선택하세요."); return; }
        var dialog=new RecordEditorDialog(RecordEditorMode.Student,"학생정보 수정",_studentChoices,existing:item){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(() => _db.UpdateStudent(item.Id,Current.AcademicYearId,dialog.Grade,dialog.ClassName,dialog.StudentNumber,dialog.StudentName,dialog.Note),"학생정보를 수정했습니다.");
    }

    private void DeleteStudent_Click(object sender, RoutedEventArgs e)
    {
        if (StudentsGrid.SelectedItem is not StudentItem item) { MessageBox.Show("삭제할 학생을 선택하세요."); return; }
        if (!Confirm($"{item.Name} 학생을 삭제할까요?\n수강 데이터가 연결되어 있으면 삭제되지 않습니다.")) return;
        TryRun(() => _db.DeleteStudent(item.Id), "학생을 삭제했습니다.");
    }

    private void DeleteAllStudents_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || !Confirm("현재 학년도의 학생정보 전체를 삭제할까요?\n연결된 수강 데이터가 있으면 삭제되지 않습니다.")) return;
        TryRun(() => _db.DeleteAllStudents(Current.AcademicYearId), "학생정보를 전체 삭제했습니다.");
    }

    private void StudentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StudentsGrid.SelectedItem is not StudentItem x) return;
        FillStudentSelector("Student",x);
        StudentNoteBox.Text=x.Note??"";
    }

    private void ClearStudentInputs_Click(object sender,RoutedEventArgs e)
    {
        StudentsGrid.SelectedItem=null;ClearStudentSelector("Student");StudentNoteBox.Clear();
    }

    private void StudentSearch_TextChanged(object sender, TextChangedEventArgs e)
    {if(!_ready)return;_studentSearchTimer.Stop();_studentSearchTimer.Start();}

    private void AddEligibility_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        var dialog=new RecordEditorDialog(RecordEditorMode.Eligibility,"지원 대상자 추가",_studentChoices,defaultDate:Current.StartDate){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(()=>_db.AddEligibility(Current.AcademicYearId,dialog.SelectedStudent!.Id,dialog.ProgramCode,dialog.EffectiveFrom),"지원 대상자를 추가했습니다.");
    }

    private void UpdateEligibility_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||EligibilityGrid.SelectedItem is not EligibilityItem item){MessageBox.Show("수정할 지원 대상자를 선택하세요.");return;}
        var dialog=new RecordEditorDialog(RecordEditorMode.Eligibility,"지원 대상자 수정",_studentChoices,existing:item){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(()=>_db.UpdateEligibility(Current.AcademicYearId,item.Id,dialog.ProgramCode,dialog.EffectiveFrom),"지원 대상자 정보를 수정했습니다. 기존 정산은 다시 생성해야 합니다.");
    }

    private void DeleteEligibility_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || EligibilityGrid.SelectedItem is not EligibilityItem item) { MessageBox.Show("삭제할 대상자를 선택하세요.");return; }
        if (!Confirm($"{item.StudentName} 학생의 {item.ProgramName} 자격을 삭제할까요?")) return;
        TryRun(()=>_db.DeleteEligibility(Current.AcademicYearId,item.Id),"지원 대상에서 삭제했습니다.");
    }
    private void DeleteAllEligibilities_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||!Confirm("현재 학년도의 지원대상자 데이터를 전체 삭제할까요?\n정산 결과는 오래된 상태로 변경됩니다."))return;
        TryRun(()=>_db.DeleteAllEligibilities(Current.AcademicYearId),"지원대상자 데이터를 전체 삭제했습니다.");
    }
    private void ClearEligibilityInputs_Click(object sender,RoutedEventArgs e)
    {
        EligibilityGrid.SelectedItem=null;ClearStudentSelector("Support");SupportProgramCombo.SelectedIndex=0;
        SupportStartPicker.SelectedDate=Current?.StartDate;
    }

    private void AddDepartment_Click(object sender, RoutedEventArgs e)
    {
        if(Current is null)return;
        var dialog=new RecordEditorDialog(RecordEditorMode.Department,"부서정보 추가",_studentChoices){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        SaveDepartment(dialog,null,"부서를 추가했습니다.");
    }
    private void UpdateDepartment_Click(object sender, RoutedEventArgs e)
    {
        if (DepartmentGrid.SelectedItem is not DepartmentItem item) { MessageBox.Show("수정할 부서를 선택하세요.");return; }
        var dialog=new RecordEditorDialog(RecordEditorMode.Department,"부서정보 수정",_studentChoices,existing:item){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        SaveDepartment(dialog,item.Id,"부서정보를 수정했습니다. 수강생 명단에도 적용하려면 [부서금액 다시 불러오기]를 실행하세요.");
    }
    private void SaveDepartment(RecordEditorDialog dialog,long? id,string message)
    {
        if (Current is null)return;
        TryRun(()=>_db.SaveDepartment(id,Current.AcademicYearId,dialog.DepartmentName,dialog.SectionName,dialog.Weekdays,dialog.InstructorName,
            dialog.InstructorFee,dialog.OperatingFee,dialog.TextbookFee,dialog.MaterialFee),message);
    }
    private void EditDepartmentStudentFees_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        if(DepartmentGrid.SelectedItem is not DepartmentItem department){MessageBox.Show("학생별 금액을 수정할 부서를 선택하세요.");return;}
        var enrollments=_db.GetEnrollments(Current.Id)
            .Where(x=>x.DepartmentId==department.Id&&x.StatusCode=="ACTIVE")
            .OrderBy(x=>x.Grade).ThenBy(x=>x.ClassName).ThenBy(x=>x.StudentNumber).ThenBy(x=>x.StudentName).ToArray();
        if(enrollments.Length==0){MessageBox.Show("선택한 부서에 현재 수강중인 학생이 없습니다.");return;}
        var dialog=new DepartmentStudentFeeDialog(department,enrollments){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        try
        {
            var changed=_db.UpdateDepartmentStudentAmounts(Current.Id,department.Id,dialog.Items.ToArray(),dialog.ChangeReason);
            RefreshAll();
            MessageBox.Show($"{changed:N0}명의 학생별 금액을 수정했습니다. 기존 정산 데이터가 있다면 다시 생성해 주세요.","저장 완료",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){ShowError(ex);}
    }
    private void DeleteDepartment_Click(object sender, RoutedEventArgs e)
    {
        if (DepartmentGrid.SelectedItem is not DepartmentItem item){MessageBox.Show("삭제할 부서를 선택하세요.");return;}
        if(!Confirm($"{item.DisplayName} 부서를 삭제할까요?\n수강 데이터가 연결되어 있으면 삭제되지 않습니다."))return;
        TryRun(()=>_db.DeleteDepartment(item.Id),"부서를 삭제했습니다.");
    }
    private void DeleteAllDepartments_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||!Confirm("현재 학년도의 부서정보를 전체 삭제할까요?\n연결된 모든 작업공간의 수강·정산 데이터도 함께 삭제됩니다."))return;
        TryRun(()=>_db.DeleteAllDepartments(Current.AcademicYearId),"부서정보와 연결된 수강 데이터를 전체 삭제했습니다.");
    }
    private void DepartmentGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(DepartmentGrid.SelectedItem is not DepartmentItem x)return;
        DeptNameBox.Text=x.Name;DeptSectionBox.Text=x.SectionName;DeptWeekdaysBox.Text=x.Weekdays??"";DeptInstructorBox.Text=x.InstructorName??"";
        DeptInstructorFeeBox.Text=x.InstructorFee.ToString();DeptOperatingFeeBox.Text=x.OperatingFee.ToString();DeptTextbookFeeBox.Text=x.TextbookFee.ToString();DeptMaterialFeeBox.Text=x.MaterialFee.ToString();
    }
    private void ClearDepartmentInputs_Click(object sender,RoutedEventArgs e)
    {
        DepartmentGrid.SelectedItem=null;DeptNameBox.Clear();DeptSectionBox.Clear();DeptWeekdaysBox.Clear();DeptInstructorBox.Clear();
        DeptInstructorFeeBox.Text="0";DeptOperatingFeeBox.Text="0";DeptTextbookFeeBox.Text="0";DeptMaterialFeeBox.Text="0";
    }

    private void AddEnrollment_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        var dialog=new RecordEditorDialog(RecordEditorMode.EnrollmentAdd,"수강 데이터 추가",_studentChoices,_db.GetDepartments(Current.AcademicYearId)){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(()=>_db.AddEnrollment(Current.Id,dialog.SelectedStudent!.Id,dialog.SelectedDepartment!.Id),"수강 데이터를 추가했습니다.");
    }
    private void DeleteEnrollment_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||EnrollmentGrid.SelectedItem is not EnrollmentItem item){MessageBox.Show("삭제할 수강 데이터를 선택하세요.");return;}
        if(!Confirm($"{item.StudentName} / {item.DepartmentName} 수강 입력을 완전히 삭제할까요?\n실제 중도취소가 아니라 잘못 입력한 데이터에만 사용하세요."))return;
        TryRun(()=>_db.DeleteEnrollment(Current.Id,item.Id),"잘못 입력된 수강 데이터를 삭제했습니다.");
    }
    private void EnrollmentGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(EnrollmentGrid.SelectedItem is not EnrollmentItem item)return;
        var student=_studentChoices.FirstOrDefault(x=>x.Id==item.StudentId);
        if(student is not null)FillStudentSelector("Enroll",student);
        EnrollDepartmentCombo.SelectedItem=EnrollDepartmentCombo.Items.Cast<DepartmentItem>().FirstOrDefault(x=>x.Id==item.DepartmentId);
        ActualInstructorBox.Text=item.InstructorFee.ToString();ActualOperatingBox.Text=item.OperatingFee.ToString();ActualTextbookBox.Text=item.TextbookFee.ToString();ActualMaterialBox.Text=item.MaterialFee.ToString();
        EnrollmentChangeReasonBox.Text=item.ChangeReason??"";
    }
    private void ClearEnrollmentInputs_Click(object sender,RoutedEventArgs e)
    {
        EnrollmentGrid.SelectedItem=null;ClearStudentSelector("Enroll");EnrollDepartmentCombo.SelectedItem=null;
        ActualInstructorBox.Clear();ActualOperatingBox.Clear();ActualTextbookBox.Clear();ActualMaterialBox.Clear();EnrollmentChangeReasonBox.Clear();
    }
    private void UpdateEnrollmentAmounts_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||EnrollmentGrid.SelectedItem is not EnrollmentItem item){MessageBox.Show("금액을 변경할 수강 데이터를 선택하세요.");return;}
        var dialog=new RecordEditorDialog(RecordEditorMode.EnrollmentEdit,"수강 데이터 수정",_studentChoices,_db.GetDepartments(Current.AcademicYearId),item){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        TryRun(()=>_db.UpdateEnrollmentAmounts(Current.Id,item.Id,dialog.InstructorFee,dialog.OperatingFee,dialog.TextbookFee,dialog.MaterialFee,dialog.ChangeReason),"실제 적용금액을 변경했습니다. 기존 정산은 다시 생성해야 합니다.");
    }
    private void CancelEnrollment_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||EnrollmentGrid.SelectedItem is not EnrollmentItem item){MessageBox.Show("취소할 수강 데이터를 선택하세요.");return;}
        var dialog=new RecordEditorDialog(RecordEditorMode.EnrollmentEdit,"수강취소 사유 입력",_studentChoices,_db.GetDepartments(Current.AcademicYearId),item){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        if(!Confirm($"{item.StudentName} / {item.DepartmentName} 수강을 취소 처리할까요?\n행은 삭제되지 않고 취소 이력으로 보존됩니다."))return;
        TryRun(()=>_db.CancelEnrollment(Current.Id,item.Id,DateTime.Today,dialog.ChangeReason),"수강취소 상태로 변경했습니다. 기존 정산은 다시 생성해야 합니다.");
    }
    private void DeleteAllEnrollments_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null||!Confirm("현재 작업공간의 수강 데이터를 전체 삭제할까요?\n현재 작업공간의 정산 결과도 함께 삭제됩니다."))return;
        TryRun(()=>_db.DeleteAllEnrollments(Current.Id),"현재 작업공간의 수강 데이터를 전체 삭제했습니다.");
    }
    private void RefreshDepartmentFees_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        if(!Confirm("현재 작업공간의 수강중 데이터에 최신 부서 기본금액을 다시 불러올까요?\n수기로 변경한 실제 적용금액은 보존됩니다."))return;
        try
        {
            var result=_db.RefreshDepartmentFees(Current.Id);RefreshAll();
            MessageBox.Show($"{result.UpdatedEnrollments:N0}건의 수강 데이터에 부서 기본금액을 다시 불러왔습니다.\n수기 조정 비용 {result.PreservedManualCharges:N0}건의 실제 적용금액은 보존했습니다.\n\n기존 정산은 다시 생성해야 합니다.","완료",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){ShowError(ex);}
    }
    private void LoadStudentDetail_Click(object sender,RoutedEventArgs e)
    {
        if(Current is null)return;
        try
        {
            var grade=OptionalInt(DetailGradeBox.Text,"학년");var number=OptionalInt(DetailNumberBox.Text,"번호");
            var className=DetailClassBox.Text.Trim();var name=DetailNameBox.Text.Trim();
            if(grade is null&&number is null&&className.Length==0&&name.Length==0)throw new ArgumentException("학년, 반, 번호, 이름 중 하나 이상을 입력하세요.");
            var matches=_studentChoices.Where(x=>(grade is null||x.Grade==grade)&&(number is null||x.StudentNumber==number)
                &&(className.Length==0||x.ClassName.Equals(className,StringComparison.OrdinalIgnoreCase))
                &&(name.Length==0||x.Name.Contains(name,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(matches.Length==0)throw new InvalidOperationException("입력한 조건과 일치하는 학생이 없습니다.");
            if(matches.Length>1)throw new InvalidOperationException($"{matches.Length}명의 학생이 검색되었습니다. 학생을 한 명으로 구분할 조건을 더 입력하세요.");
            var student=matches[0];FillStudentSelector("Detail",student);
            var detail=_db.GetStudentDetail(Current.AcademicYearId,student.Grade,student.ClassName,student.StudentNumber,student.Name);
            var s=detail.Summary;DetailStudentText.Text=$"{s.StudentName} · {s.Grade}학년 {s.ClassName}반 {s.StudentNumber}번\n{s.SupportType}";
            var hasVoucher=s.SupportType.Contains("방과후 이용권",StringComparison.Ordinal);
            var hasFreeVoucher=s.SupportType.Contains("자유수강권",StringComparison.Ordinal);
            DetailVoucherText.Text=hasVoucher
                ? $"한도 {s.VoucherBudget:N0}원\n사용 {s.VoucherUsed:N0}원 · 잔액 {s.VoucherBalance:N0}원"
                : "해당없음";
            DetailFreeText.Text=hasFreeVoucher
                ? $"한도 {s.FreeBudget:N0}원\n사용 {s.FreeUsed:N0}원 · 잔액 {s.FreeBalance:N0}원"
                : "해당없음";
            StudentUsageGrid.ItemsSource=detail.Usage;
        }
        catch(Exception ex){ShowError(ex);}
    }
    private void ClearStudentDetail_Click(object sender,RoutedEventArgs e)
    {
        ClearStudentSelector("Detail");
        DetailStudentText.Text="";DetailVoucherText.Text="";DetailFreeText.Text="";StudentUsageGrid.ItemsSource=null;
    }
    private void ClearBudgetInputs_Click(object sender,RoutedEventArgs e)
    {
        BudgetOverrideGrid.SelectedItem=null;ClearStudentSelector("Budget");BudgetProgramCombo.SelectedIndex=0;BudgetAmountBox.Clear();BudgetReasonBox.Clear();
    }

    private void StudentSelector_DropDownOpened(object sender,EventArgs e)
    {
        if(sender is not ComboBox combo||combo.Tag is not string tag)return;
        var parts=tag.Split(':');if(parts.Length!=2)return;
        PopulateStudentSelector(parts[0],parts[1]);
    }

    private void StudentSelector_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready||_syncingStudentSelectors||sender is not ComboBox combo||combo.SelectedItem is null||combo.Tag is not string tag)return;
        var parts=tag.Split(':');if(parts.Length!=2)return;
        var selected=combo.SelectedItem.ToString()??"";
        Dispatcher.BeginInvoke(new Action(() => ApplyStudentSelectorSelection(parts[0],parts[1],selected)));
    }

    private void ApplyStudentSelectorSelection(string group,string field,string selected)
    {
        if(_syncingStudentSelectors)return;
        var boxes=StudentSelectorBoxes(group);_syncingStudentSelectors=true;
        try
        {
            switch(field)
            {
                case "Grade": boxes.Grade.Text=selected;boxes.Class.Text="";boxes.Number.Text="";boxes.Name.Text="";break;
                case "Class": boxes.Class.Text=selected;boxes.Number.Text="";boxes.Name.Text="";break;
                case "Number": boxes.Number.Text=selected;boxes.Name.Text="";break;
                case "Name": boxes.Name.Text=selected;break;
            }
            if(group=="Detail"&&field is "Grade" or "Class" or "Name")return;
            var matches=FilterStudents(boxes,field=="Name");
            if(matches.Count==1)
            {
                FillStudentSelectorCore(boxes,matches[0]);
                if(group=="Student")StudentNoteBox.Text=matches[0].Note??"";
            }
        }
        finally{_syncingStudentSelectors=false;}
    }

    private void RefreshStudentSelectorSources()
    {
        foreach(var group in new[]{"Student","Support","Enroll","Budget","Detail"})PopulateStudentSelector(group,"Grade");
    }

    private void PopulateStudentSelector(string group,string field)
    {
        var boxes=StudentSelectorBoxes(group);var current=field switch
        {
            "Grade"=>boxes.Grade.Text,"Class"=>boxes.Class.Text,"Number"=>boxes.Number.Text,_=>boxes.Name.Text
        };
        IEnumerable<StudentItem> rows=_studentChoices;
        var detailNeedsStudentScope=group=="Detail"&&field is "Number" or "Name";
        var detailScopeReady=int.TryParse(boxes.Grade.Text,out var detailGrade)&&!string.IsNullOrWhiteSpace(boxes.Class.Text);
        string[] values;
        if(detailNeedsStudentScope&&!detailScopeReady)
        {
            values=Array.Empty<string>();
        }
        else
        {
            if(group=="Detail")
            {
                if(detailNeedsStudentScope)rows=rows.Where(x=>x.Grade==detailGrade&&x.ClassName==boxes.Class.Text.Trim());
            }
            else if((field is "Class" or "Number" or "Name")&&int.TryParse(boxes.Grade.Text,out var grade))rows=rows.Where(x=>x.Grade==grade);
            if(group!="Detail"&&(field is "Number" or "Name")&&!string.IsNullOrWhiteSpace(boxes.Class.Text))rows=rows.Where(x=>x.ClassName==boxes.Class.Text.Trim());
            if(field=="Name"&&int.TryParse(boxes.Number.Text,out var number))rows=rows.Where(x=>x.StudentNumber==number);
            values=field switch
            {
                "Grade"=>rows.Select(x=>x.Grade.ToString()).Distinct().OrderBy(x=>int.Parse(x)).ToArray(),
                "Class"=>rows.Select(x=>x.ClassName).Distinct().OrderBy(x=>x).ToArray(),
                "Number"=>rows.Select(x=>x.StudentNumber.ToString()).Distinct().OrderBy(x=>int.Parse(x)).ToArray(),
                _=>rows.Select(x=>x.Name).Distinct().OrderBy(x=>x).ToArray()
            };
        }
        var target=field switch{"Grade"=>boxes.Grade,"Class"=>boxes.Class,"Number"=>boxes.Number,_=>boxes.Name};
        _syncingStudentSelectors=true;try{target.ItemsSource=values;target.Text=current;}finally{_syncingStudentSelectors=false;}
    }

    private IReadOnlyList<StudentItem> FilterStudents((ComboBox Grade,ComboBox Class,ComboBox Number,ComboBox Name) boxes,bool includeName)
    {
        IEnumerable<StudentItem> rows=_studentChoices;
        if(int.TryParse(boxes.Grade.Text,out var grade))rows=rows.Where(x=>x.Grade==grade);
        if(!string.IsNullOrWhiteSpace(boxes.Class.Text))rows=rows.Where(x=>x.ClassName==boxes.Class.Text.Trim());
        if(int.TryParse(boxes.Number.Text,out var number))rows=rows.Where(x=>x.StudentNumber==number);
        if(includeName&&!string.IsNullOrWhiteSpace(boxes.Name.Text))rows=rows.Where(x=>x.Name==boxes.Name.Text.Trim());
        return rows.ToArray();
    }

    private (ComboBox Grade,ComboBox Class,ComboBox Number,ComboBox Name) StudentSelectorBoxes(string group)=>group switch
    {
        "Student"=>(StudentGradeBox,StudentClassBox,StudentNumberBox,StudentNameBox),
        "Support"=>(SupportGradeBox,SupportClassBox,SupportNumberBox,SupportNameBox),
        "Enroll"=>(EnrollGradeBox,EnrollClassBox,EnrollNumberBox,EnrollNameBox),
        "Budget"=>(BudgetGradeBox,BudgetClassBox,BudgetNumberBox,BudgetNameBox),
        "Detail"=>(DetailGradeBox,DetailClassBox,DetailNumberBox,DetailNameBox),
        _=>throw new ArgumentOutOfRangeException(nameof(group))
    };

    private void FillStudentSelector(string group,StudentItem student)
    {
        _syncingStudentSelectors=true;try{FillStudentSelectorCore(StudentSelectorBoxes(group),student);}finally{_syncingStudentSelectors=false;}
    }
    private static void FillStudentSelectorCore((ComboBox Grade,ComboBox Class,ComboBox Number,ComboBox Name) boxes,StudentItem student)
    {boxes.Grade.Text=student.Grade.ToString();boxes.Class.Text=student.ClassName;boxes.Number.Text=student.StudentNumber.ToString();boxes.Name.Text=student.Name;}
    private void ClearStudentSelector(string group)
    {
        var boxes=StudentSelectorBoxes(group);_syncingStudentSelectors=true;
        try{boxes.Grade.Text="";boxes.Class.Text="";boxes.Number.Text="";boxes.Name.Text="";boxes.Grade.SelectedItem=null;boxes.Class.SelectedItem=null;boxes.Number.SelectedItem=null;boxes.Name.SelectedItem=null;}
        finally{_syncingStudentSelectors=false;}
    }
    private void EnrollmentSearch_TextChanged(object sender,TextChangedEventArgs e)
    {if(!_ready)return;_enrollmentSearchTimer.Stop();_enrollmentSearchTimer.Start();}

    private void SaveMaintenanceSettings_Click(object sender,RoutedEventArgs e)
    {
        try{_settings.BackupDirectory=BackupDirectoryBox.Text.Trim();_settingsService.Save(_settings);MessageBox.Show("백업 폴더 설정을 저장했습니다.","완료",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex){ShowError(ex);}
    }

    private void SelectBackupFolder_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="기본 백업 폴더 선택",InitialDirectory=Directory.Exists(BackupDirectoryBox.Text)?BackupDirectoryBox.Text:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(dialog.ShowDialog()==true)BackupDirectoryBox.Text=dialog.FolderName;
    }

    private void BackupNow_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            _settings.BackupDirectory=BackupDirectoryBox.Text.Trim();_settingsService.Save(_settings);Directory.CreateDirectory(_settings.BackupDirectory);
            var path=Path.Combine(_settings.BackupDirectory,$"방과후통합관리_전체백업_{DateTime.Now:yyyyMMdd_HHmmss}.afbackup");_backup.CreateBackup(path);
            BackupStatusText.Text=$"마지막 백업: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{path}";MessageBox.Show($"업무 데이터 전체를 백업했습니다.\n\n{path}","백업 완료",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void BackupAs_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{FileName=$"방과후통합관리_전체백업_{DateTime.Now:yyyyMMdd_HHmmss}.afbackup",Filter="방과후 통합관리 백업 (*.afbackup)|*.afbackup",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;try{_backup.CreateBackup(dialog.FileName);BackupStatusText.Text=$"마지막 백업: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{dialog.FileName}";MessageBox.Show("지정한 위치에 백업했습니다.","백업 완료",MessageBoxButton.OK,MessageBoxImage.Information);}catch(Exception ex){ShowError(ex);}
    }

    private void RestoreBackup_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="방과후 통합관리 백업 (*.afbackup)|*.afbackup"};if(dialog.ShowDialog()!=true)return;
        if(!Confirm("선택한 백업으로 현재 업무 데이터를 복원할까요?\n현재 데이터는 복원 전에 자동으로 안전백업됩니다."))return;
        try
        {
            var safety=_backup.RestoreBackup(dialog.FileName,AppPaths.RecoveryDirectory);_db.Initialize();LoadWorkspaces();
            if(WorkspaceCombo.Items.Count>0){WorkspaceCombo.SelectedIndex=-1;WorkspaceCombo.SelectedIndex=0;}
            BackupStatusText.Text=$"복원 완료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            MessageBox.Show($"백업 데이터로 복원했습니다.\n\n복원 전 자동백업:\n{safety}","복원 완료",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){ShowError(ex);}
    }

    private void OpenBackupFolder_Click(object sender,RoutedEventArgs e)
    {
        try{var folder=BackupDirectoryBox.Text.Trim();Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){ShowError(ex);}
    }

    private async void CheckUpdate_Click(object sender,RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled=false;UpdateStatusText.Text="최신 Release를 확인하고 있습니다...";DownloadUpdateButton.IsEnabled=false;
        try
        {
            _settings.BackupDirectory=BackupDirectoryBox.Text.Trim();_settingsService.Save(_settings);
            _availableUpdate=await _updates.CheckAsync();UpdateStatusText.Text=_availableUpdate.IsUpdateAvailable
                ? $"새 버전 {_availableUpdate.LatestVersion}을 사용할 수 있습니다. (현재 { _availableUpdate.CurrentVersion})"
                : $"현재 최신 버전입니다. ({_availableUpdate.CurrentVersion})";
            DownloadUpdateButton.IsEnabled=_availableUpdate.InstallerUrl is not null;OpenReleaseButton.IsEnabled=true;
        }
        catch(Exception ex){UpdateStatusText.Text=ex.Message;ShowError(ex);}
        finally{CheckUpdateButton.IsEnabled=true;}
    }

    private async void DownloadUpdate_Click(object sender,RoutedEventArgs e)
    {
        if(_availableUpdate is null)return;if(!Confirm($"버전 {_availableUpdate.LatestVersion} 설치파일을 다운로드할까요?\n업무 DB는 설치 폴더와 분리되어 유지됩니다."))return;
        DownloadUpdateButton.IsEnabled=false;UpdateProgress.Value=0;
        try
        {
            var progress=new Progress<int>(value=>{UpdateProgress.Value=value;UpdateStatusText.Text=$"업데이트 설치파일 다운로드 중... {value}%";});
            var path=await _updates.DownloadInstallerAsync(_availableUpdate,AppPaths.DownloadDirectory,progress);UpdateStatusText.Text=_availableUpdate.InstallerSha256 is null?"다운로드가 완료되었습니다.":"다운로드와 SHA-256 무결성 확인이 완료되었습니다.";
            if(Confirm("다운로드가 완료되었습니다. 프로그램을 종료하고 설치를 시작할까요?")){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});Application.Current.Shutdown();}
        }
        catch(Exception ex){ShowError(ex);DownloadUpdateButton.IsEnabled=true;}
    }

    private void OpenRelease_Click(object sender,RoutedEventArgs e)
    {try{Process.Start(new ProcessStartInfo(UpdateService.ReleasesPageUrl){UseShellExecute=true});}catch(Exception ex){ShowError(ex);}}

    private static string GetAppVersion(){var v=Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0,0);return $"{v.Major}.{v.Minor}.{Math.Max(0,v.Build)}";}

    private static int? OptionalInt(string value,string field)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        return int.TryParse(value.Trim(),out var number)?number:throw new ArgumentException($"{field}은(는) 숫자로 입력하세요.");
    }

    private void StudentTemplate_Click(object sender,RoutedEventArgs e)=>SaveTemplate("학생명단_업로드양식.xlsx",_excel.CreateStudentTemplate);
    private void EligibilityTemplate_Click(object sender,RoutedEventArgs e)=>SaveTemplate("지원대상자_업로드양식.xlsx",_excel.CreateEligibilityTemplate);
    private void DepartmentTemplate_Click(object sender,RoutedEventArgs e)=>SaveTemplate("부서정보_업로드양식.xlsx",_excel.CreateDepartmentTemplate);
    private void EnrollmentTemplate_Click(object sender,RoutedEventArgs e)=>SaveTemplate("수강데이터_업로드양식.xlsx",_excel.CreateEnrollmentTemplate);

    private void StudentImport_Click(object sender,RoutedEventArgs e)
    {if(Current is null)return;ImportExcel(path=>_excel.ImportStudents(path,Current.AcademicYearId));}
    private void EligibilityImport_Click(object sender,RoutedEventArgs e)
    {if(Current is null)return;ImportExcel(path=>_excel.ImportEligibilities(path,Current));}
    private void DepartmentImport_Click(object sender,RoutedEventArgs e)
    {if(Current is null)return;ImportExcel(path=>_excel.ImportDepartments(path,Current.AcademicYearId));}
    private void EnrollmentImport_Click(object sender,RoutedEventArgs e)
    {if(Current is null)return;ImportExcel(path=>_excel.ImportEnrollments(path,Current));}

    private void SaveTemplate(string fileName,Action<string> action)
    {
        var dialog=new SaveFileDialog{FileName=fileName,Filter="Excel 통합 문서 (*.xlsx)|*.xlsx",AddExtension=true};
        if(dialog.ShowDialog()!=true)return;TryRun(()=>action(dialog.FileName),"업로드 양식을 저장했습니다.",false);
    }
    private void ImportExcel(Func<string,ImportResult> importer)
    {
        var dialog=new OpenFileDialog{Filter="Excel 통합 문서 (*.xlsx)|*.xlsx"};if(dialog.ShowDialog()!=true)return;
        try{var result=importer(dialog.FileName);RefreshAll();MessageBox.Show(result.ToSummary(),"가져오기 결과",MessageBoxButton.OK,result.Issues.Count==0?MessageBoxImage.Information:MessageBoxImage.Warning);}
        catch(Exception ex){ShowError(ex);}
    }

    private void TryRun(Action action,string success,bool refresh=true)
    {
        try{action();if(refresh)RefreshAll();MessageBox.Show(success,"완료",MessageBoxButton.OK,MessageBoxImage.Information);}
        catch(Exception ex){ShowError(ex);}
    }
    private static void ShowError(Exception ex)
    {
        var message=ex is SqliteException sql&&sql.SqliteErrorCode==19
            ? "중복 데이터이거나 연결된 데이터 때문에 처리할 수 없습니다. 입력값과 기존 자료를 확인하세요.\n\n"+sql.Message
            : ex.InnerException?.Message??ex.Message;
        MessageBox.Show(message,"처리할 수 없음",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    private static bool Confirm(string message)=>MessageBox.Show(message,"확인",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private static int Int(ComboBox box,string name)=>int.TryParse(box.Text.Trim(),out var value)?value:throw new ArgumentException($"{name}을(를) 숫자로 입력하세요.");
    private static long Money(TextBox box,string name)
    {var raw=box.Text.Replace(",","").Replace("원","").Trim();return long.TryParse(raw,out var value)&&value>=0?value:throw new ArgumentException($"{name}은(는) 0 이상의 금액이어야 합니다.");}
}
