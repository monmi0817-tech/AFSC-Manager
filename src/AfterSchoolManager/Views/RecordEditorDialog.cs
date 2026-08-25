using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AfterSchoolManager.Models;

namespace AfterSchoolManager.Views;

public enum RecordEditorMode { Student, Eligibility, Department, EnrollmentAdd, EnrollmentEdit }

public sealed class RecordEditorDialog : Window
{
    private readonly RecordEditorMode _mode;
    private readonly IReadOnlyList<StudentItem> _students;
    private bool _syncing;
    private readonly ComboBox _grade=new(){IsEditable=true,Width=75};
    private readonly ComboBox _className=new(){IsEditable=true,Width=90};
    private readonly ComboBox _number=new(){IsEditable=true,Width=75};
    private readonly ComboBox _studentName=new(){IsEditable=true,Width=150};
    private readonly TextBox _note=new(){Width=310};
    private readonly ComboBox _program=new(){Width=190};
    private readonly DatePicker _effectiveFrom=new(){Width=160};
    private readonly TextBox _departmentName=new(){Width=180};
    private readonly TextBox _sectionName=new(){Width=90};
    private readonly TextBox _weekdays=new(){Width=100};
    private readonly TextBox _instructorName=new(){Width=140};
    private readonly TextBox _instructorFee=new(){Width=115,Text="0"};
    private readonly TextBox _operatingFee=new(){Width=115,Text="0"};
    private readonly TextBox _textbookFee=new(){Width=115,Text="0"};
    private readonly TextBox _materialFee=new(){Width=115,Text="0"};
    private readonly ComboBox _department=new(){Width=260,DisplayMemberPath="DisplayName"};
    private readonly TextBox _changeReason=new(){Width=360};

    public StudentItem? SelectedStudent { get; private set; }
    public int Grade => ParseInt(_grade.Text,"학년");
    public string ClassName => Required(_className.Text,"반");
    public int StudentNumber => ParseInt(_number.Text,"번호");
    public string StudentName => Required(_studentName.Text,"이름");
    public string? Note => NullIfEmpty(_note.Text);
    public string ProgramCode => (_program.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"VOUCHER";
    public DateTime EffectiveFrom => _effectiveFrom.SelectedDate??DateTime.Today;
    public string DepartmentName => Required(_departmentName.Text,"부서명");
    public string SectionName => _sectionName.Text.Trim();
    public string? Weekdays => NullIfEmpty(_weekdays.Text);
    public string? InstructorName => NullIfEmpty(_instructorName.Text);
    public long InstructorFee => ParseMoney(_instructorFee.Text,"강사료");
    public long OperatingFee => ParseMoney(_operatingFee.Text,"수용비");
    public long TextbookFee => ParseMoney(_textbookFee.Text,"교재비");
    public long MaterialFee => ParseMoney(_materialFee.Text,"재료비");
    public DepartmentItem? SelectedDepartment => _department.SelectedItem as DepartmentItem;
    public string ChangeReason => _changeReason.Text.Trim();

    public RecordEditorDialog(RecordEditorMode mode,string title,IReadOnlyList<StudentItem> students,
        IReadOnlyList<DepartmentItem>? departments=null,object? existing=null,DateTime? defaultDate=null)
    {
        _mode=mode;_students=students;
        Title=title;Width=760;MaxHeight=720;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(243,246,251));ResizeMode=ResizeMode.NoResize;
        var root=new StackPanel{Margin=new Thickness(26)};
        root.Children.Add(new TextBlock{Text=title,FontSize=25,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,18)});
        var card=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(223,229,238)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(20)};
        var form=new StackPanel();card.Child=form;root.Children.Add(card);
        ConfigureControlAlignment();
        ConfigureStudentBoxes();
        if(mode is RecordEditorMode.Student or RecordEditorMode.Eligibility or RecordEditorMode.EnrollmentAdd or RecordEditorMode.EnrollmentEdit)
            form.Children.Add(StudentFields());
        switch(mode)
        {
            case RecordEditorMode.Student:
                form.Children.Add(Field("비고",_note));
                if(existing is StudentItem student){FillStudent(student);_note.Text=student.Note??"";}
                break;
            case RecordEditorMode.Eligibility:
                _program.Items.Add(new ComboBoxItem{Content="방과후 이용권",Tag="VOUCHER"});
                _program.Items.Add(new ComboBoxItem{Content="자유수강권",Tag="FREE_VOUCHER"});_program.SelectedIndex=0;
                form.Children.Add(Row(Field("지원제도",_program),Field("적용 시작일",_effectiveFrom)));
                _effectiveFrom.SelectedDate=defaultDate??DateTime.Today;
                if(existing is EligibilityItem eligibility)
                {
                    var eligibilityStudent=_students.FirstOrDefault(x=>x.Id==eligibility.StudentId);if(eligibilityStudent is not null)FillStudent(eligibilityStudent);
                    SetStudentFieldsEnabled(false);_program.SelectedIndex=eligibility.ProgramCode=="FREE_VOUCHER"?1:0;_effectiveFrom.SelectedDate=eligibility.EffectiveFrom;
                }
                break;
            case RecordEditorMode.Department:
                form.Children.Add(Row(Field("부서명",_departmentName),Field("반명",_sectionName),Field("요일",_weekdays),Field("강사명",_instructorName)));
                form.Children.Add(Row(Field("강사료",_instructorFee),Field("수용비",_operatingFee),Field("교재비",_textbookFee),Field("재료비",_materialFee)));
                if(existing is DepartmentItem department)LoadDepartment(department);
                break;
            case RecordEditorMode.EnrollmentAdd:
                _department.ItemsSource=departments??Array.Empty<DepartmentItem>();form.Children.Add(Field("부서",_department));
                break;
            case RecordEditorMode.EnrollmentEdit:
                SetStudentFieldsEnabled(false);_department.ItemsSource=departments??Array.Empty<DepartmentItem>();_department.IsEnabled=false;
                form.Children.Add(Field("부서",_department));
                form.Children.Add(Row(Field("강사료",_instructorFee),Field("수용비",_operatingFee),Field("교재비",_textbookFee),Field("재료비",_materialFee)));
                form.Children.Add(Field("변경 사유",_changeReason));
                if(existing is EnrollmentItem enrollment)LoadEnrollment(enrollment);
                break;
        }
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        var cancel=new Button{Content="취소",MinWidth=88};cancel.Click+=(_,_)=>Close();
        var save=new Button{Content="저장",MinWidth=88,Background=new SolidColorBrush(Color.FromRgb(75,103,209)),Foreground=Brushes.White};save.Click+=Save_Click;
        buttons.Children.Add(cancel);buttons.Children.Add(save);root.Children.Add(buttons);
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private void ConfigureStudentBoxes()
    {
        foreach(var (box,field) in new[]{(_grade,"Grade"),(_className,"Class"),(_number,"Number"),(_studentName,"Name")})
        {
            box.Tag=field;box.DropDownOpened+=(_,_)=>Populate(box,field);box.SelectionChanged+=StudentSelectionChanged;
        }
        Populate(_grade,"Grade");
    }
    private UIElement StudentFields()=>Row(Field("학년",_grade),Field("반",_className),Field("번호",_number),Field("이름",_studentName));
    private static StackPanel Field(string label,Control control)=>new()
    {
        Margin=new Thickness(4),HorizontalAlignment=HorizontalAlignment.Center,
        Children={new TextBlock{Text=label,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(2,0,2,3)},control}
    };
    private static WrapPanel Row(params UIElement[] fields)
    {
        var row=new WrapPanel{Margin=new Thickness(0,0,0,8),HorizontalAlignment=HorizontalAlignment.Center};
        foreach(var field in fields)row.Children.Add(field);return row;
    }

    private void ConfigureControlAlignment()
    {
        Control[] controls={_grade,_className,_number,_studentName,_note,_program,_effectiveFrom,_departmentName,_sectionName,
            _weekdays,_instructorName,_instructorFee,_operatingFee,_textbookFee,_materialFee,_department,_changeReason};
        foreach(var control in controls)
        {
            control.HorizontalAlignment=HorizontalAlignment.Center;
            control.HorizontalContentAlignment=HorizontalAlignment.Center;
            control.VerticalContentAlignment=VerticalAlignment.Center;
        }
        foreach(var textBox in new[]{_note,_departmentName,_sectionName,_weekdays,_instructorName,_changeReason})textBox.TextAlignment=TextAlignment.Center;
        foreach(var moneyBox in new[]{_instructorFee,_operatingFee,_textbookFee,_materialFee})moneyBox.TextAlignment=TextAlignment.Right;
        foreach(var combo in new[]{_grade,_className,_number,_studentName,_program,_department})combo.Loaded+=CenterComboText;
        _effectiveFrom.Loaded+=CenterDateText;
    }

    private static void CenterComboText(object sender,RoutedEventArgs e)
    {
        if(sender is not ComboBox combo)return;combo.ApplyTemplate();
        if(combo.Template.FindName("PART_EditableTextBox",combo) is TextBox editor)
        {
            editor.Margin=new Thickness(10,0,34,0);editor.TextAlignment=TextAlignment.Center;
        }
        if(combo.Template.FindName("SelectionContent",combo) is ContentPresenter content)
        {
            content.Margin=new Thickness(10,0,34,0);content.HorizontalAlignment=HorizontalAlignment.Stretch;
            content.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);
        }
    }

    private static void CenterDateText(object sender,RoutedEventArgs e)
    {
        if(sender is not DatePicker picker)return;picker.ApplyTemplate();
        var dateText=FindVisualChild<TextBox>(picker);if(dateText is not null)dateText.TextAlignment=TextAlignment.Center;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
        {
            var child=VisualTreeHelper.GetChild(parent,index);if(child is T match)return match;
            var nested=FindVisualChild<T>(child);if(nested is not null)return nested;
        }
        return null;
    }

    private void Populate(ComboBox target,string field)
    {
        var current=target.Text;IEnumerable<StudentItem> rows=_students;
        if((field is "Class" or "Number" or "Name")&&int.TryParse(_grade.Text,out var grade))rows=rows.Where(x=>x.Grade==grade);
        if((field is "Number" or "Name")&&!string.IsNullOrWhiteSpace(_className.Text))rows=rows.Where(x=>x.ClassName==_className.Text.Trim());
        if(field=="Name"&&int.TryParse(_number.Text,out var number))rows=rows.Where(x=>x.StudentNumber==number);
        string[] values=field switch
        {
            "Grade"=>rows.Select(x=>x.Grade.ToString()).Distinct().OrderBy(x=>int.Parse(x)).ToArray(),
            "Class"=>rows.Select(x=>x.ClassName).Distinct().OrderBy(x=>x).ToArray(),
            "Number"=>rows.Select(x=>x.StudentNumber.ToString()).Distinct().OrderBy(x=>int.Parse(x)).ToArray(),
            _=>rows.Select(x=>x.Name).Distinct().OrderBy(x=>x).ToArray()
        };
        _syncing=true;try{target.ItemsSource=values;target.Text=current;}finally{_syncing=false;}
    }
    private void StudentSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_syncing||sender is not ComboBox box||box.SelectedItem is null)return;
        var field=box.Tag?.ToString()??"";var selected=box.SelectedItem.ToString()??"";
        Dispatcher.BeginInvoke(new Action(()=>ApplySelection(field,selected)),DispatcherPriority.Background);
    }
    private void ApplySelection(string field,string selected)
    {
        if(_syncing)return;_syncing=true;
        try
        {
            switch(field)
            {
                case "Grade":_grade.Text=selected;_className.Text="";_number.Text="";_studentName.Text="";break;
                case "Class":_className.Text=selected;_number.Text="";_studentName.Text="";break;
                case "Number":_number.Text=selected;_studentName.Text="";break;
                case "Name":_studentName.Text=selected;break;
            }
            var matches=FindMatchingStudents(field=="Name");if(matches.Count==1)FillStudentCore(matches[0]);
        }
        finally{_syncing=false;}
    }
    private IReadOnlyList<StudentItem> FindMatchingStudents(bool includeName=true)
    {
        IEnumerable<StudentItem> rows=_students;
        if(int.TryParse(_grade.Text,out var grade))rows=rows.Where(x=>x.Grade==grade);
        if(!string.IsNullOrWhiteSpace(_className.Text))rows=rows.Where(x=>x.ClassName==_className.Text.Trim());
        if(int.TryParse(_number.Text,out var number))rows=rows.Where(x=>x.StudentNumber==number);
        if(includeName&&!string.IsNullOrWhiteSpace(_studentName.Text))rows=rows.Where(x=>x.Name==_studentName.Text.Trim());
        return rows.ToArray();
    }
    private void FillStudent(StudentItem student){_syncing=true;try{FillStudentCore(student);}finally{_syncing=false;}}
    private void FillStudentCore(StudentItem student){_grade.Text=student.Grade.ToString();_className.Text=student.ClassName;_number.Text=student.StudentNumber.ToString();_studentName.Text=student.Name;SelectedStudent=student;}
    private void SetStudentFieldsEnabled(bool enabled){_grade.IsEnabled=enabled;_className.IsEnabled=enabled;_number.IsEnabled=enabled;_studentName.IsEnabled=enabled;}
    private void LoadDepartment(DepartmentItem item){_departmentName.Text=item.Name;_sectionName.Text=item.SectionName;_weekdays.Text=item.Weekdays??"";_instructorName.Text=item.InstructorName??"";_instructorFee.Text=item.InstructorFee.ToString();_operatingFee.Text=item.OperatingFee.ToString();_textbookFee.Text=item.TextbookFee.ToString();_materialFee.Text=item.MaterialFee.ToString();}
    private void LoadEnrollment(EnrollmentItem item)
    {
        var student=_students.FirstOrDefault(x=>x.Id==item.StudentId);if(student is not null)FillStudent(student);
        _department.SelectedItem=_department.Items.Cast<DepartmentItem>().FirstOrDefault(x=>x.Id==item.DepartmentId);
        _instructorFee.Text=item.InstructorFee.ToString();_operatingFee.Text=item.OperatingFee.ToString();_textbookFee.Text=item.TextbookFee.ToString();_materialFee.Text=item.MaterialFee.ToString();_changeReason.Text=item.ChangeReason??"";
    }
    private void Save_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_mode==RecordEditorMode.Student){_=Grade;_=ClassName;_=StudentNumber;_=StudentName;}
            if(_mode is RecordEditorMode.Eligibility or RecordEditorMode.EnrollmentAdd)
            {
                var matches=FindMatchingStudents();if(matches.Count!=1)throw new InvalidOperationException(matches.Count==0?"학생정보에서 일치하는 학생을 찾지 못했습니다.":"여러 학생이 검색되었습니다. 학년·반·번호·이름을 더 입력하세요.");SelectedStudent=matches[0];
            }
            if(_mode==RecordEditorMode.EnrollmentAdd&&SelectedDepartment is null)throw new ArgumentException("부서를 선택하세요.");
            if(_mode==RecordEditorMode.Department){_=DepartmentName;_=InstructorFee;_=OperatingFee;_=TextbookFee;_=MaterialFee;}
            if(_mode==RecordEditorMode.EnrollmentEdit)
            {
                _=InstructorFee;_=OperatingFee;_=TextbookFee;_=MaterialFee;
                if(string.IsNullOrWhiteSpace(ChangeReason))throw new ArgumentException("변경 사유를 입력하세요.");
            }
            DialogResult=true;
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"입력값 확인",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    private static int ParseInt(string value,string name)=>int.TryParse(value.Trim(),out var number)?number:throw new ArgumentException($"{name}을(를) 숫자로 입력하세요.");
    private static long ParseMoney(string value,string name){var raw=value.Replace(",","").Replace("원","").Trim();return long.TryParse(raw,out var amount)&&amount>=0?amount:throw new ArgumentException($"{name}은(는) 0 이상의 금액이어야 합니다.");}
    private static string Required(string value,string name)=>string.IsNullOrWhiteSpace(value)?throw new ArgumentException($"{name}을(를) 입력하세요."):value.Trim();
    private static string? NullIfEmpty(string value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
