namespace AfterSchoolManager.Models;

public sealed class WorkspaceItem
{
    public long Id { get; init; }
    public long AcademicYearId { get; init; }
    public int AcademicYear { get; init; }
    public string Name { get; init; } = "";
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public long SourceRevision { get; init; }
    public string DisplayName => $"{AcademicYear}학년도 · {Name}";
    public string PeriodText => $"{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}";
}

public sealed class StudentItem
{
    public long Id { get; init; }
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string Name { get; init; } = "";
    public string? Note { get; init; }
    public string SupportType { get; init; } = "일반";
}

public sealed class EligibilityItem
{
    public long Id { get; init; }
    public long StudentId { get; init; }
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public string ProgramCode { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
}

public sealed class DepartmentItem
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string SectionName { get; init; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(SectionName) ? Name : $"{Name} {SectionName}";
    public string? Weekdays { get; init; }
    public string? InstructorName { get; init; }
    public long InstructorFee { get; init; }
    public long OperatingFee { get; init; }
    public long TextbookFee { get; init; }
    public long MaterialFee { get; init; }
}

public sealed class EnrollmentItem
{
    public long Id { get; init; }
    public long StudentId { get; init; }
    public long DepartmentId { get; init; }
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public string DepartmentName { get; init; } = "";
    public string SupportType { get; init; } = "일반";
    public string StatusCode { get; init; } = "ACTIVE";
    public string StatusText => StatusCode == "CANCELLED" ? "수강취소" : "수강중";
    public DateTime? CancelledAt { get; init; }
    public string? ChangeReason { get; init; }
    public long BaseInstructorFee { get; init; }
    public long BaseOperatingFee { get; init; }
    public long BaseTextbookFee { get; init; }
    public long BaseMaterialFee { get; init; }
    public long InstructorFee { get; init; }
    public long OperatingFee { get; init; }
    public long TextbookFee { get; init; }
    public long MaterialFee { get; init; }
    public long TotalFee => InstructorFee + OperatingFee + TextbookFee + MaterialFee;
    public long BaseTotalFee => BaseInstructorFee + BaseOperatingFee + BaseTextbookFee + BaseMaterialFee;
}

public sealed record ImportIssue(int RowNumber, string Message);
public sealed record ImportResult(int ImportedCount, IReadOnlyList<ImportIssue> Issues)
{
    public string ToSummary() => Issues.Count == 0
        ? $"{ImportedCount:N0}건을 정상적으로 가져왔습니다."
        : $"정상 {ImportedCount:N0}건, 오류 {Issues.Count:N0}건\n\n" +
          string.Join("\n", Issues.Take(15).Select(x => $"{x.RowNumber}행: {x.Message}")) +
          (Issues.Count > 15 ? $"\n외 {Issues.Count - 15:N0}건" : "");
}

public sealed class SupportSettingsItem
{
    public long VoucherDefault { get; init; }
    public long FreeVoucherDefault { get; init; }
    public string SourcePriority { get; init; } = "VOUCHER_FIRST";
    public string VoucherGrades { get; init; } = "3";
}

public sealed class BudgetOverrideItem
{
    public long Id { get; init; }
    public long StudentId { get; init; }
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public string ProgramCode { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public long Amount { get; init; }
    public string? ChangeReason { get; init; }
}

public sealed class SettlementStatusItem
{
    public bool Exists { get; init; }
    public bool IsCurrent { get; init; }
    public DateTime? GeneratedAt { get; init; }
    public long SourceRevision { get; init; }
    public string Message { get; init; } = "정산 데이터가 없습니다.";
}

public sealed class SelfPayResultItem
{
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public long InstructorFee { get; init; }
    public long OperatingFee { get; init; }
    public long TextbookFee { get; init; }
    public long MaterialFee { get; init; }
    public long Total => InstructorFee + OperatingFee + TextbookFee + MaterialFee;
}

public sealed class VoucherResultItem
{
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public long VoucherInstructorFee { get; init; }
    public long VoucherOperatingFee { get; init; }
    public long VoucherTextbookFee { get; init; }
    public long VoucherMaterialFee { get; init; }
    public long VoucherTotal => VoucherInstructorFee + VoucherOperatingFee + VoucherTextbookFee + VoucherMaterialFee;
    public long OverInstructorFee { get; init; }
    public long OverOperatingFee { get; init; }
    public long OverTextbookFee { get; init; }
    public long OverMaterialFee { get; init; }
    public long OverTotal => OverInstructorFee + OverOperatingFee + OverTextbookFee + OverMaterialFee;
}

public sealed class FreeVoucherResultItem
{
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public long InstructorFee { get; init; }
    public long OperatingFee { get; init; }
    public long TextbookFee { get; init; }
    public long MaterialFee { get; init; }
    public long Total => InstructorFee + OperatingFee + TextbookFee + MaterialFee;
}

public sealed class SettlementResourceRowItem
{
    public string FeeType { get; init; } = "";
    public long SelfPayAmount { get; init; }
    public long VoucherAmount { get; init; }
    public long VoucherOverAmount { get; init; }
    public long FreeVoucherAmount { get; init; }
    public long Total => SelfPayAmount + VoucherAmount + VoucherOverAmount + FreeVoucherAmount;
}

public sealed class ChangeHistoryItem
{
    public long Id { get; init; }
    public DateTime ChangedAt { get; init; }
    public string EntityType { get; init; } = "";
    public long EntityId { get; init; }
    public string Action { get; init; } = "";
    public string? FieldName { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }
}

public sealed class StudentDetailSummary
{
    public long StudentId { get; init; }
    public int Grade { get; init; }
    public string ClassName { get; init; } = "";
    public int StudentNumber { get; init; }
    public string StudentName { get; init; } = "";
    public string SupportType { get; init; } = "일반";
    public long VoucherBudget { get; init; }
    public long VoucherUsed { get; init; }
    public long VoucherBalance => Math.Max(0,VoucherBudget-VoucherUsed);
    public long FreeBudget { get; init; }
    public long FreeUsed { get; init; }
    public long FreeBalance => Math.Max(0,FreeBudget-FreeUsed);
}

public sealed class StudentUsageItem
{
    public string WorkspaceName { get; init; } = "";
    public string DepartmentName { get; init; } = "";
    public long InstructorFee { get; init; }
    public long OperatingFee { get; init; }
    public long TextbookFee { get; init; }
    public long MaterialFee { get; init; }
    public long VoucherAmount { get; init; }
    public long FreeVoucherAmount { get; init; }
    public long SelfPayAmount { get; init; }
    public long VoucherOverAmount { get; init; }
}

public sealed class ProposalFeeTypeItem
{
    public string Code { get; init; } = "INSTRUCTOR";
    public string DisplayName { get; init; } = "강사료";
    public override string ToString() => DisplayName;
}

public sealed class ProposalLabelsItem
{
    public string SelfPayHeader { get; init; } = "일반 수익자";
    public string VoucherOverHeader { get; init; } = "이용권 초과금";
    public string VoucherHeader { get; init; } = "이용권 지원금";
    public string FreeVoucherHeader { get; init; } = "자유수강권";
}

public sealed class ProposalDepartmentItem
{
    public long DepartmentId { get; init; }
    public string DepartmentName { get; init; } = "";
    public long SelfPayAmount { get; init; }
    public long VoucherOverAmount { get; init; }
    public long VoucherAmount { get; init; }
    public long FreeVoucherAmount { get; init; }
    public long TotalAmount => SelfPayAmount + VoucherOverAmount + VoucherAmount + FreeVoucherAmount;
}

public sealed class AppSettingsItem
{
    public string BackupDirectory { get; set; } = "";
    public string GitHubRepository { get; set; } = "";
}

public sealed class UpdateInfoItem
{
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string ReleaseName { get; init; } = "";
    public string ReleasePageUrl { get; init; } = "";
    public string? InstallerName { get; init; }
    public string? InstallerUrl { get; init; }
    public string? InstallerSha256 { get; init; }
    public bool IsUpdateAvailable { get; init; }
}
