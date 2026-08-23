using AfterSchoolManager.Models;
using Microsoft.Data.Sqlite;

namespace AfterSchoolManager.Services;

public sealed class ProposalService
{
    private readonly string _connectionString;
    private readonly SettlementService _settlement;

    public static IReadOnlyList<ProposalFeeTypeItem> FeeTypes { get; } = new[]
    {
        new ProposalFeeTypeItem { Code="INSTRUCTOR", DisplayName="강사료" },
        new ProposalFeeTypeItem { Code="OPERATING", DisplayName="수용비" },
        new ProposalFeeTypeItem { Code="TEXTBOOK", DisplayName="교재비" },
        new ProposalFeeTypeItem { Code="MATERIAL", DisplayName="재료비" },
        new ProposalFeeTypeItem { Code="TEXTBOOK_MATERIAL", DisplayName="교재·재료비 통합" }
    };

    public ProposalService(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource=databasePath, Mode=SqliteOpenMode.ReadWriteCreate, ForeignKeys=true
        }.ToString();
        _settlement = new SettlementService(databasePath);
    }

    private SqliteConnection Open()
    {
        var connection=new SqliteConnection(_connectionString);connection.Open();
        using var cmd=connection.CreateCommand();cmd.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";cmd.ExecuteNonQuery();
        return connection;
    }

    public ProposalLabelsItem GetLabels(long academicYearId)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();
        cmd.CommandText="""
            SELECT g.grade FROM support_policy_grade g
            JOIN support_program p ON p.id=g.program_id
            WHERE g.academic_year_id=$year AND p.code='VOUCHER' ORDER BY g.grade;
            """;
        cmd.Parameters.AddWithValue("$year",academicYearId);
        using var reader=cmd.ExecuteReader();var voucherGrades=new List<int>();while(reader.Read())voucherGrades.Add(reader.GetInt32(0));
        var voucherText=voucherGrades.Count==0?"이용권":string.Join(",",voucherGrades.Select(x=>$"{x}학년"));
        var generalGrades=Enumerable.Range(1,6).Except(voucherGrades).ToArray();
        var selfPay=generalGrades.Length==0?"일반 수익자":$"수익자({string.Join(",",generalGrades)}학년)";
        return new ProposalLabelsItem
        {
            SelfPayHeader=selfPay,
            VoucherOverHeader=$"{voucherText} 초과금",
            VoucherHeader=$"{voucherText} 지원금"
        };
    }

    public IReadOnlyList<ProposalDepartmentItem> GetDepartmentSummary(long workspaceId,string feeTypeCode)
    {
        var status=_settlement.GetStatus(workspaceId);
        if(!status.Exists)throw new InvalidOperationException("먼저 [정산 데이터 생성]을 실행하세요.");
        if(!status.IsCurrent)throw new InvalidOperationException("원본 데이터가 변경되어 정산 결과가 오래된 상태입니다. 정산 데이터를 다시 생성하세요.");
        var chargeTypes=feeTypeCode switch
        {
            "INSTRUCTOR" => new[]{"INSTRUCTOR"},
            "OPERATING" => new[]{"OPERATING"},
            "TEXTBOOK" => new[]{"TEXTBOOK"},
            "MATERIAL" => new[]{"MATERIAL"},
            "TEXTBOOK_MATERIAL" => new[]{"TEXTBOOK","MATERIAL"},
            _ => throw new ArgumentException("지원하지 않는 품의 종류입니다.")
        };

        using var connection=Open();using var cmd=connection.CreateCommand();
        var typeParameters=string.Join(",",chargeTypes.Select((_,i)=>$"$type{i}"));
        cmd.CommandText=$"""
            SELECT d.id,d.name,d.section_name,
              COALESCE(SUM(CASE WHEN a.resource_code='SELF_PAY' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='VOUCHER_OVER' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='VOUCHER' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='FREE_VOUCHER' THEN a.amount ELSE 0 END),0)
            FROM settlement_run r
            JOIN settlement s ON s.settlement_run_id=r.id
            JOIN settlement_allocation a ON a.settlement_id=s.id
            JOIN charge c ON c.id=a.charge_id
            JOIN enrollment e ON e.id=c.enrollment_id
            JOIN department d ON d.id=e.department_id
            WHERE r.workspace_id=$workspace AND r.is_active=1 AND c.charge_type IN ({typeParameters})
            GROUP BY d.id,d.name,d.section_name
            HAVING SUM(a.amount)>0
            ORDER BY d.name COLLATE NOCASE,d.section_name COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$workspace",workspaceId);
        for(var i=0;i<chargeTypes.Length;i++)cmd.Parameters.AddWithValue($"$type{i}",chargeTypes[i]);
        using var reader=cmd.ExecuteReader();var result=new List<ProposalDepartmentItem>();
        while(reader.Read())result.Add(new ProposalDepartmentItem
        {
            DepartmentId=reader.GetInt64(0),
            DepartmentName=string.IsNullOrWhiteSpace(reader.GetString(2))?reader.GetString(1):$"{reader.GetString(1)} {reader.GetString(2)}",
            SelfPayAmount=reader.GetInt64(3),VoucherOverAmount=reader.GetInt64(4),VoucherAmount=reader.GetInt64(5),FreeVoucherAmount=reader.GetInt64(6)
        });
        return result;
    }
}
