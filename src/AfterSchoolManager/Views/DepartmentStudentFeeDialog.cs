using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AfterSchoolManager.Models;

namespace AfterSchoolManager.Views;

public sealed class DepartmentStudentFeeDialog : Window
{
    private readonly DataGrid _grid;
    private readonly TextBox _reason=new(){Width=460,Text="부서정보 학생별 금액 수정",TextAlignment=TextAlignment.Center};
    public ObservableCollection<DepartmentStudentFeeEditItem> Items { get; }
    public string ChangeReason=>_reason.Text.Trim();

    public DepartmentStudentFeeDialog(DepartmentItem department,IEnumerable<EnrollmentItem> enrollments)
    {
        Items=new ObservableCollection<DepartmentStudentFeeEditItem>(enrollments.Select(DepartmentStudentFeeEditItem.FromEnrollment));
        Title=$"{department.DisplayName} 학생별 금액 수정";Width=1040;Height=680;MinWidth=900;MinHeight=560;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(243,246,251));
        var root=new Grid{Margin=new Thickness(26)};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var heading=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,16)};
        heading.Children.Add(new TextBlock{Text=$"{department.DisplayName} · 학생별 금액 수정",FontSize=25,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center});
        heading.Children.Add(new TextBlock{Text="현재 작업공간의 수강중 학생만 표시됩니다. 변경한 학생과 항목만 저장됩니다.",Foreground=new SolidColorBrush(Color.FromRgb(110,122,144)),Margin=new Thickness(0,5,0,0),TextAlignment=TextAlignment.Center});
        root.Children.Add(heading);
        _grid=new DataGrid{ItemsSource=Items,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=false,SelectionUnit=DataGridSelectionUnit.Cell,RowHeight=44,ColumnHeaderHeight=44};
        _grid.Columns.Add(ReadColumn("학년","Grade",62));_grid.Columns.Add(ReadColumn("반","ClassName",72));_grid.Columns.Add(ReadColumn("번호","StudentNumber",72));_grid.Columns.Add(ReadColumn("이름","StudentName",130));
        _grid.Columns.Add(MoneyColumn("강사료","InstructorFee"));_grid.Columns.Add(MoneyColumn("수용비","OperatingFee"));_grid.Columns.Add(MoneyColumn("교재비","TextbookFee"));_grid.Columns.Add(MoneyColumn("재료비","MaterialFee"));
        var total=MoneyColumn("합계","TotalFee");
        total.IsReadOnly=true;
        total.Binding=new Binding("TotalFee"){Mode=BindingMode.OneWay,StringFormat="N0"};
        _grid.Columns.Add(total);
        var card=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(223,229,238)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(12),Child=_grid};
        Grid.SetRow(card,1);root.Children.Add(card);
        var footer=new Grid{Margin=new Thickness(0,16,0,0)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var reasonPanel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        reasonPanel.Children.Add(new TextBlock{Text="변경 사유",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),FontWeight=FontWeights.SemiBold});reasonPanel.Children.Add(_reason);footer.Children.Add(reasonPanel);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        Grid.SetColumn(buttons,1);
        var cancel=new Button{Content="취소",MinWidth=88};cancel.Click+=(_,_)=>Close();
        var save=new Button{Content="저장",MinWidth=88,Background=new SolidColorBrush(Color.FromRgb(75,103,209)),Foreground=Brushes.White};save.Click+=Save_Click;
        buttons.Children.Add(cancel);buttons.Children.Add(save);footer.Children.Add(buttons);Grid.SetRow(footer,2);root.Children.Add(footer);Content=root;
    }

    private void Save_Click(object sender,RoutedEventArgs e)
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell,true);_grid.CommitEdit(DataGridEditingUnit.Row,true);
        if(Items.Any(x=>new[]{x.InstructorFee,x.OperatingFee,x.TextbookFee,x.MaterialFee}.Any(amount=>amount<0))){MessageBox.Show(this,"금액은 0 이상이어야 합니다.","입력값 확인",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        if(!Items.Any(x=>x.IsChanged)){MessageBox.Show(this,"변경된 학생별 금액이 없습니다.","확인",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(string.IsNullOrWhiteSpace(ChangeReason)){MessageBox.Show(this,"변경 사유를 입력하세요.","입력값 확인",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        DialogResult=true;
    }

    private static DataGridTextColumn ReadColumn(string header,string path,double width)=>new()
    {
        Header=header,Binding=new Binding(path),Width=width,IsReadOnly=true,ElementStyle=TextStyle(TextAlignment.Center)
    };
    private static DataGridTextColumn MoneyColumn(string header,string path)=>new()
    {
        Header=header,Binding=new Binding(path){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=112,
        ElementStyle=TextStyle(TextAlignment.Right),EditingElementStyle=EditStyle()
    };
    private static Style TextStyle(TextAlignment alignment)
    {
        var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,alignment));style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));return style;
    }
    private static Style EditStyle()
    {
        var style=new Style(typeof(TextBox));style.Setters.Add(new Setter(TextBox.TextAlignmentProperty,TextAlignment.Right));style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));return style;
    }
}
