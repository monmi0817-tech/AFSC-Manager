using System.IO;
using AfterSchoolManager.Models;
using ClosedXML.Excel;

namespace AfterSchoolManager.Services;

public sealed class ExcelService
{
    private readonly DatabaseService _db;
    public ExcelService(DatabaseService db) => _db = db;

    public void CreateStudentTemplate(string path) => CreateTemplate(path, "학생명단",
        "학년", "반", "번호", "이름", "비고");

    public void CreateEligibilityTemplate(string path) => CreateTemplate(path, "지원대상자",
        "학년", "반", "번호", "이름", "지원제도");

    public void CreateDepartmentTemplate(string path) => CreateTemplate(path, "부서정보",
        "부서명", "반명", "요일", "강사명", "강사료", "수용비", "교재비", "재료비");

    public void CreateEnrollmentTemplate(string path) => CreateTemplate(path, "수강데이터",
        "부서명", "반명", "학년", "반", "번호", "이름");

    public void ExportProposal(string path, WorkspaceItem workspace, ProposalFeeTypeItem feeType,
        ProposalLabelsItem labels, IReadOnlyList<ProposalDepartmentItem> rows, DateTime generatedAt)
    {
        using var wb=new XLWorkbook();var ws=wb.Worksheets.Add("품의자료");
        ws.Cell("A1").Value=$"{workspace.AcademicYear}학년도 {workspace.Name} {feeType.DisplayName} 품의자료";
        ws.Range("A1:F1").Merge();ws.Cell("A1").Style.Font.Bold=true;ws.Cell("A1").Style.Font.FontSize=16;
        ws.Cell("A1").Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
        ws.Cell("A2").Value=$"작업공간: {workspace.Name}  |  기간: {workspace.StartDate:yyyy-MM-dd} ~ {workspace.EndDate:yyyy-MM-dd}  |  정산 생성: {generatedAt:yyyy-MM-dd HH:mm:ss}";
        ws.Range("A2:F2").Merge();ws.Cell("A2").Style.Font.FontColor=XLColor.FromHtml("#667085");
        var headers=new[]{"부서명",labels.SelfPayHeader,labels.VoucherOverHeader,labels.VoucherHeader,labels.FreeVoucherHeader,"합계"};
        for(var col=0;col<headers.Length;col++)ws.Cell(4,col+1).Value=headers[col];
        var rowNumber=5;
        foreach(var row in rows)
        {
            ws.Cell(rowNumber,1).Value=row.DepartmentName;ws.Cell(rowNumber,2).Value=row.SelfPayAmount;
            ws.Cell(rowNumber,3).Value=row.VoucherOverAmount;ws.Cell(rowNumber,4).Value=row.VoucherAmount;
            ws.Cell(rowNumber,5).Value=row.FreeVoucherAmount;ws.Cell(rowNumber,6).FormulaA1=$"SUM(B{rowNumber}:E{rowNumber})";rowNumber++;
        }
        var totalRow=rowNumber;ws.Cell(totalRow,1).Value="합계";
        for(var col=2;col<=6;col++)ws.Cell(totalRow,col).FormulaA1=rows.Count==0?"0":$"SUM({ws.Cell(5,col).Address}:{ws.Cell(totalRow-1,col).Address})";
        var table=ws.Range(4,1,totalRow,6);table.Style.Border.OutsideBorder=XLBorderStyleValues.Thin;table.Style.Border.InsideBorder=XLBorderStyleValues.Thin;
        ws.Range(4,1,4,6).Style.Fill.BackgroundColor=XLColor.FromHtml("#1F4E78");ws.Range(4,1,4,6).Style.Font.FontColor=XLColor.White;ws.Range(4,1,4,6).Style.Font.Bold=true;
        ws.Range(totalRow,1,totalRow,6).Style.Fill.BackgroundColor=XLColor.FromHtml("#D9EAF7");ws.Range(totalRow,1,totalRow,6).Style.Font.Bold=true;
        ws.Range(5,2,totalRow,6).Style.NumberFormat.Format="#,##0";ws.Range(4,1,totalRow,6).Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
        ws.Range(4,2,totalRow,6).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Right;ws.Column(1).Width=24;
        for(var col=2;col<=6;col++)ws.Column(col).Width=19;ws.Row(1).Height=28;ws.SheetView.FreezeRows(4);
        ws.PageSetup.PageOrientation=XLPageOrientation.Landscape;ws.PageSetup.FitToPages(1,0);
        ws.PageSetup.Margins.Top=0.4;ws.PageSetup.Margins.Bottom=0.4;ws.PageSetup.Margins.Left=0.4;ws.PageSetup.Margins.Right=0.4;
        ws.PageSetup.PrintAreas.Add($"A1:F{totalRow}");wb.SaveAs(path);
    }

    public void ExportSelfPayResults(string path,WorkspaceItem workspace,IReadOnlyList<SelfPayResultItem> rows,DateTime generatedAt)
    {
        ExportSettlementResults(path,workspace,"수익자","수익자",
            new[]{"학년","반","번호","이름","강사료","수용비","교재비","재료비","합계"},
            rows.Select(row=>new object[]{row.Grade,row.ClassName,row.StudentNumber,row.StudentName,row.InstructorFee,row.OperatingFee,row.TextbookFee,row.MaterialFee,row.Total}),generatedAt);
    }

    public void ExportVoucherResults(string path,WorkspaceItem workspace,IReadOnlyList<VoucherResultItem> rows,DateTime generatedAt)
    {
        ExportSettlementResults(path,workspace,"방과후 이용권","방과후이용권",
            new[]{"학년","반","번호","이름","이용권 강사료","이용권 수용비","이용권 교재비","이용권 재료비","이용권 합계","초과 강사료","초과 수용비","초과 교재비","초과 재료비","초과 합계"},
            rows.Select(row=>new object[]{row.Grade,row.ClassName,row.StudentNumber,row.StudentName,row.VoucherInstructorFee,row.VoucherOperatingFee,row.VoucherTextbookFee,row.VoucherMaterialFee,row.VoucherTotal,row.OverInstructorFee,row.OverOperatingFee,row.OverTextbookFee,row.OverMaterialFee,row.OverTotal}),generatedAt);
    }

    public void ExportFreeVoucherResults(string path,WorkspaceItem workspace,IReadOnlyList<FreeVoucherResultItem> rows,DateTime generatedAt)
    {
        ExportSettlementResults(path,workspace,"자유수강권","자유수강권",
            new[]{"학년","반","번호","이름","강사료","수용비","교재비","재료비","합계"},
            rows.Select(row=>new object[]{row.Grade,row.ClassName,row.StudentNumber,row.StudentName,row.InstructorFee,row.OperatingFee,row.TextbookFee,row.MaterialFee,row.Total}),generatedAt);
    }

    private static void ExportSettlementResults(string path,WorkspaceItem workspace,string title,string sheetName,
        IReadOnlyList<string> headers,IEnumerable<object[]> sourceRows,DateTime generatedAt)
    {
        var rows=sourceRows.ToArray();
        using var wb=new XLWorkbook();var ws=wb.Worksheets.Add(sheetName);
        var lastColumn=headers.Count;
        ws.Cell(1,1).Value=$"{workspace.AcademicYear}학년도 {workspace.Name} {title}";
        ws.Range(1,1,1,lastColumn).Merge();ws.Cell(1,1).Style.Font.Bold=true;ws.Cell(1,1).Style.Font.FontSize=16;
        ws.Cell(1,1).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
        ws.Cell(2,1).Value=$"작업공간: {workspace.Name}  |  기간: {workspace.StartDate:yyyy-MM-dd} ~ {workspace.EndDate:yyyy-MM-dd}  |  정산 생성: {generatedAt:yyyy-MM-dd HH:mm:ss}";
        ws.Range(2,1,2,lastColumn).Merge();ws.Cell(2,1).Style.Font.FontColor=XLColor.FromHtml("#667085");
        for(var col=0;col<headers.Count;col++)ws.Cell(4,col+1).Value=headers[col];
        var rowNumber=5;
        foreach(var values in rows)
        {
            for(var col=0;col<values.Length;col++)SetCellValue(ws.Cell(rowNumber,col+1),values[col]);
            rowNumber++;
        }
        var totalRow=rowNumber;ws.Cell(totalRow,4).Value="합계";
        for(var col=5;col<=lastColumn;col++)ws.Cell(totalRow,col).FormulaA1=rows.Length==0?"0":$"SUM({ws.Cell(5,col).Address}:{ws.Cell(totalRow-1,col).Address})";
        var table=ws.Range(4,1,totalRow,lastColumn);
        table.Style.Border.OutsideBorder=XLBorderStyleValues.Thin;table.Style.Border.InsideBorder=XLBorderStyleValues.Thin;
        ws.Range(4,1,4,lastColumn).Style.Fill.BackgroundColor=XLColor.FromHtml("#217346");
        ws.Range(4,1,4,lastColumn).Style.Font.FontColor=XLColor.White;ws.Range(4,1,4,lastColumn).Style.Font.Bold=true;
        ws.Range(totalRow,1,totalRow,lastColumn).Style.Fill.BackgroundColor=XLColor.FromHtml("#E2F0D9");
        ws.Range(totalRow,1,totalRow,lastColumn).Style.Font.Bold=true;
        ws.Range(5,5,totalRow,lastColumn).Style.NumberFormat.Format="#,##0";
        ws.Range(4,1,totalRow,lastColumn).Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
        ws.Range(4,1,totalRow,4).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Center;
        ws.Range(4,5,totalRow,lastColumn).Style.Alignment.Horizontal=XLAlignmentHorizontalValues.Right;
        ws.Column(1).Width=8;ws.Column(2).Width=9;ws.Column(3).Width=9;ws.Column(4).Width=14;
        for(var col=5;col<=lastColumn;col++)ws.Column(col).Width=16;
        ws.Row(1).Height=28;ws.SheetView.FreezeRows(4);ws.Range(4,1,totalRow-1,lastColumn).SetAutoFilter();
        ws.PageSetup.PageOrientation=XLPageOrientation.Landscape;ws.PageSetup.FitToPages(1,0);
        ws.PageSetup.Margins.Top=0.4;ws.PageSetup.Margins.Bottom=0.4;ws.PageSetup.Margins.Left=0.4;ws.PageSetup.Margins.Right=0.4;
        ws.PageSetup.PrintAreas.Add($"A1:{ws.Cell(totalRow,lastColumn).Address}");wb.SaveAs(path);
    }

    private static void SetCellValue(IXLCell cell,object value)
    {
        switch(value)
        {
            case int intValue:cell.Value=intValue;break;
            case long longValue:cell.Value=longValue;break;
            default:cell.Value=value.ToString()??"";break;
        }
    }

    private static void CreateTemplate(string path, string sheetName, params string[] headers)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var header = ws.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE8F8");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(1);
        for (var column = 1; column <= headers.Length; column++) ws.Column(column).Width = 10;
        wb.SaveAs(path);
    }

    public ImportResult ImportStudents(string path, long academicYearId)
    {
        using var wb = new XLWorkbook(path); var ws = wb.Worksheet(1);
        var headers = ReadHeaders(ws);
        Require(headers, "학년", "반", "번호", "이름");
        var issues = new List<ImportIssue>(); var count = 0;
        foreach (var row in DataRows(ws))
        {
            try
            {
                _db.AddStudent(academicYearId, Int(row, headers, "학년"), Text(row, headers, "반"),
                    Int(row, headers, "번호"), Text(row, headers, "이름"), Optional(row, headers, "비고"));
                count++;
            }
            catch (Exception ex) { issues.Add(new ImportIssue(row.RowNumber(), Friendly(ex))); }
        }
        return new ImportResult(count, issues);
    }

    public ImportResult ImportEligibilities(string path, WorkspaceItem workspace)
    {
        using var wb = new XLWorkbook(path); var ws = wb.Worksheet(1); var headers = ReadHeaders(ws);
        Require(headers, "학년", "반", "번호", "이름", "지원제도");
        var issues = new List<ImportIssue>(); var count = 0;
        foreach (var row in DataRows(ws))
        {
            try
            {
                var student = _db.FindStudent(workspace.AcademicYearId, Int(row, headers, "학년"), Text(row, headers, "반"), Int(row, headers, "번호"), Text(row, headers, "이름"));
                _db.AddEligibility(workspace.AcademicYearId, student.Id, NormalizeProgram(Text(row, headers, "지원제도")), workspace.StartDate);
                count++;
            }
            catch (Exception ex) { issues.Add(new ImportIssue(row.RowNumber(), Friendly(ex))); }
        }
        return new ImportResult(count, issues);
    }

    public ImportResult ImportDepartments(string path, long academicYearId)
    {
        using var wb = new XLWorkbook(path); var ws = wb.Worksheet(1); var headers = ReadHeaders(ws);
        Require(headers, "부서명"); var issues = new List<ImportIssue>(); var count = 0;
        foreach (var row in DataRows(ws))
        {
            try
            {
                _db.SaveDepartment(null, academicYearId, Text(row, headers, "부서명"), Optional(row, headers, "반명") ?? "",
                    Optional(row, headers, "요일"), Optional(row, headers, "강사명"), Money(row, headers, "강사료"),
                    Money(row, headers, "수용비"), Money(row, headers, "교재비"), Money(row, headers, "재료비"));
                count++;
            }
            catch (Exception ex) { issues.Add(new ImportIssue(row.RowNumber(), Friendly(ex))); }
        }
        return new ImportResult(count, issues);
    }

    public ImportResult ImportEnrollments(string path, WorkspaceItem workspace)
    {
        using var wb = new XLWorkbook(path); var ws = wb.Worksheet(1); var headers = ReadHeaders(ws);
        Require(headers, "부서명", "학년", "반", "번호", "이름");
        var departments = _db.GetDepartments(workspace.AcademicYearId); var issues = new List<ImportIssue>(); var count = 0;
        foreach (var row in DataRows(ws))
        {
            try
            {
                var student = _db.FindStudent(workspace.AcademicYearId, Int(row, headers, "학년"), Text(row, headers, "반"), Int(row, headers, "번호"), Text(row, headers, "이름"));
                var departmentName = Text(row, headers, "부서명"); var section = Optional(row, headers, "반명") ?? "";
                var department = departments.SingleOrDefault(x => x.Name == departmentName && x.SectionName == section)
                    ?? throw new InvalidOperationException($"존재하지 않는 부서입니다: {departmentName} {section}".Trim());
                _db.AddEnrollment(workspace.Id, student.Id, department.Id); count++;
            }
            catch (Exception ex) { issues.Add(new ImportIssue(row.RowNumber(), Friendly(ex))); }
        }
        return new ImportResult(count, issues);
    }

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet ws)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in ws.Row(1).CellsUsed())
        {
            var name = cell.GetString().Trim(); if (name.Length > 0) result[name] = cell.Address.ColumnNumber;
        }
        return result;
    }

    private static void Require(Dictionary<string, int> headers, params string[] required)
    {
        var missing = required.Where(x => !headers.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("필수 열이 없습니다: " + string.Join(", ", missing));
    }

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet ws)
    {
        var last = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var i = 2; i <= last; i++) if (!ws.Row(i).IsEmpty()) yield return ws.Row(i);
    }

    private static string Text(IXLRow row, Dictionary<string, int> h, string key)
    {
        if (!h.TryGetValue(key, out var col)) throw new InvalidDataException($"{key} 열이 없습니다.");
        var value = row.Cell(col).GetFormattedString().Trim();
        if (value.Length == 0) throw new InvalidDataException($"{key} 값이 비어 있습니다.");
        return value;
    }

    private static string? Optional(IXLRow row, Dictionary<string, int> h, string key)
        => h.TryGetValue(key, out var col) ? NullIfEmpty(row.Cell(col).GetFormattedString()) : null;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int Int(IXLRow row, Dictionary<string, int> h, string key)
    {
        var raw = Text(row, h, key).Replace("학년", "").Trim();
        return int.TryParse(raw, out var value) ? value : throw new InvalidDataException($"{key}은(는) 정수여야 합니다.");
    }

    private static long Money(IXLRow row, Dictionary<string, int> h, string key)
    {
        var raw = Optional(row, h, key); if (raw is null) return 0;
        raw = raw.Replace(",", "").Replace("원", "").Trim();
        return long.TryParse(raw, out var value) && value >= 0 ? value : throw new InvalidDataException($"{key} 금액이 올바르지 않습니다.");
    }

    private static string NormalizeProgram(string value) => value.Trim() switch
    {
        "방과후 이용권" or "이용권" or "VOUCHER" => "VOUCHER",
        "자유수강권" or "FREE_VOUCHER" => "FREE_VOUCHER",
        _ => throw new InvalidDataException("지원제도는 방과후 이용권 또는 자유수강권이어야 합니다.")
    };

    private static string Friendly(Exception ex) => ex.InnerException?.Message ?? ex.Message;
}
