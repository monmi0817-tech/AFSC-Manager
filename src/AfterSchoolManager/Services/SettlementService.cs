using System.Globalization;
using AfterSchoolManager.Models;
using Microsoft.Data.Sqlite;

namespace AfterSchoolManager.Services;

public sealed class SettlementService
{
    private readonly string _connectionString;

    public SettlementService(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; cmd.ExecuteNonQuery();
        return c;
    }

    public SettlementStatusItem GetStatus(long workspaceId)
    {
        using var c = Open();
        var meta = ReadWorkspace(c, workspaceId);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,source_revision,policy_revision,generated_at FROM settlement_run WHERE workspace_id=$w AND is_active=1;";
        cmd.Parameters.AddWithValue("$w", workspaceId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new SettlementStatusItem { Exists=false,IsCurrent=false,SourceRevision=meta.SourceRevision,Message="아직 정산 데이터가 생성되지 않았습니다." };
        var runId=r.GetInt64(0);var runSource=r.GetInt64(1);var runPolicy=r.GetInt64(2);var generated=DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture);
        r.Close();
        var current = runSource==meta.SourceRevision && runPolicy==meta.PolicyRevision && DependenciesCurrent(c,runId);
        return new SettlementStatusItem
        {
            Exists=true,IsCurrent=current,GeneratedAt=generated,SourceRevision=meta.SourceRevision,
            Message=current?"정산 데이터가 최신 상태입니다.":"정산 생성 이후 원본 데이터 또는 지원금 설정이 변경되었습니다. 다시 생성해주세요."
        };
    }

    public void Generate(long workspaceId)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        var meta = ReadWorkspace(c, workspaceId, tx);
        var predecessors = ReadPredecessors(c, meta, tx);
        foreach (var previous in predecessors)
        {
            var run = ReadActiveRun(c, previous.Id, tx)
                ?? throw new InvalidOperationException($"선행 작업공간 '{previous.Name}'의 정산 데이터가 없습니다. 해당 작업공간부터 먼저 정산해주세요.");
            if (run.SourceRevision!=previous.SourceRevision || run.PolicyRevision!=meta.PolicyRevision || !DependenciesCurrent(c,run.Id,tx))
                throw new InvalidOperationException($"선행 작업공간 '{previous.Name}'의 정산 데이터가 오래된 상태입니다. 해당 작업공간부터 다시 정산해주세요.");
        }

        var sourcePriority = ReadSourcePriority(c, meta.AcademicYearId, tx);
        var charges = ReadCharges(c, workspaceId, tx);
        var students = charges.Select(x=>x.StudentId).Distinct().ToArray();
        var allocations = new List<AllocationDraft>();
        var summaries = new Dictionary<long,SummaryDraft>();

        foreach (var studentId in students)
        {
            var studentCharges=charges.Where(x=>x.StudentId==studentId).ToList();
            var eligibility=ReadEligibility(c,studentId,meta.StartDate,meta.EndDate,tx);
            var available=new Dictionary<string,long>();
            foreach(var code in sourcePriority.Where(eligibility.Contains))
            {
                var budget=ReadBudget(c,meta.AcademicYearId,studentId,code,tx);
                var previousUsed=ReadPreviousUsed(c,meta,studentId,code,tx);
                available[code]=Math.Max(0,budget-previousUsed);
            }
            var summary=new SummaryDraft();
            foreach(var charge in studentCharges)
            {
                var remaining=charge.Amount;summary.Total+=charge.Amount;
                foreach(var code in sourcePriority.Where(eligibility.Contains))
                {
                    if(remaining==0)break;
                    var amount=Math.Min(remaining,available.GetValueOrDefault(code));
                    if(amount<=0)continue;
                    allocations.Add(new AllocationDraft(studentId,charge.ChargeId,code,amount));
                    available[code]-=amount;remaining-=amount;summary.Add(code,amount);
                }
                if(remaining>0)
                {
                    var residual=eligibility.Contains("VOUCHER")?"VOUCHER_OVER":"SELF_PAY";
                    allocations.Add(new AllocationDraft(studentId,charge.ChargeId,residual,remaining));
                    summary.Add(residual,remaining);
                }
            }
            if(summary.Total!=summary.SelfPay+summary.Voucher+summary.VoucherOver+summary.FreeVoucher)
                throw new InvalidOperationException("정산 배분 합계가 수강료와 일치하지 않습니다.");
            summaries[studentId]=summary;
        }

        using(var deactivate=c.CreateCommand()){deactivate.Transaction=tx;deactivate.CommandText="UPDATE settlement_run SET is_active=0 WHERE workspace_id=$w AND is_active=1;";deactivate.Parameters.AddWithValue("$w",workspaceId);deactivate.ExecuteNonQuery();}
        long runId;
        using(var addRun=c.CreateCommand())
        {
            addRun.Transaction=tx;addRun.CommandText="INSERT INTO settlement_run(workspace_id,source_revision,policy_revision) VALUES($w,$source,$policy); SELECT last_insert_rowid();";
            addRun.Parameters.AddWithValue("$w",workspaceId);addRun.Parameters.AddWithValue("$source",meta.SourceRevision);addRun.Parameters.AddWithValue("$policy",meta.PolicyRevision);runId=Convert.ToInt64(addRun.ExecuteScalar());
        }
        foreach(var dependency in predecessors.Append(meta))
        {
            using var add=c.CreateCommand();add.Transaction=tx;add.CommandText="INSERT INTO settlement_dependency(settlement_run_id,workspace_id,source_revision) VALUES($run,$workspace,$revision);";
            add.Parameters.AddWithValue("$run",runId);add.Parameters.AddWithValue("$workspace",dependency.Id);add.Parameters.AddWithValue("$revision",dependency.SourceRevision);add.ExecuteNonQuery();
        }
        foreach(var pair in summaries)
        {
            long settlementId;var s=pair.Value;
            using(var add=c.CreateCommand())
            {
                add.Transaction=tx;add.CommandText="""
                    INSERT INTO settlement(settlement_run_id,student_id,total_charge,self_pay_amount,voucher_amount,voucher_over_amount,free_voucher_amount)
                    VALUES($run,$student,$total,$self,$voucher,$over,$free); SELECT last_insert_rowid();
                    """;
                add.Parameters.AddWithValue("$run",runId);add.Parameters.AddWithValue("$student",pair.Key);add.Parameters.AddWithValue("$total",s.Total);
                add.Parameters.AddWithValue("$self",s.SelfPay);add.Parameters.AddWithValue("$voucher",s.Voucher);add.Parameters.AddWithValue("$over",s.VoucherOver);add.Parameters.AddWithValue("$free",s.FreeVoucher);
                settlementId=Convert.ToInt64(add.ExecuteScalar());
            }
            foreach(var a in allocations.Where(x=>x.StudentId==pair.Key))
            {
                using var add=c.CreateCommand();add.Transaction=tx;add.CommandText="INSERT INTO settlement_allocation(settlement_id,charge_id,resource_code,amount) VALUES($settlement,$charge,$code,$amount);";
                add.Parameters.AddWithValue("$settlement",settlementId);add.Parameters.AddWithValue("$charge",a.ChargeId);add.Parameters.AddWithValue("$code",a.Code);add.Parameters.AddWithValue("$amount",a.Amount);add.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public IReadOnlyList<SelfPayResultItem> GetSelfPayResults(long workspaceId)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="""
            SELECT s.grade,s.class_name,s.student_number,s.name,
              COALESCE(SUM(CASE WHEN ch.charge_type='INSTRUCTOR' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN ch.charge_type='OPERATING' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN ch.charge_type='TEXTBOOK' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN ch.charge_type='MATERIAL' THEN a.amount ELSE 0 END),0)
            FROM settlement_run r
            JOIN settlement st ON st.settlement_run_id=r.id
            JOIN student s ON s.id=st.student_id
            JOIN settlement_allocation a ON a.settlement_id=st.id AND a.resource_code='SELF_PAY'
            JOIN charge ch ON ch.id=a.charge_id
            JOIN workspace w ON w.id=r.workspace_id
            WHERE r.workspace_id=$w AND r.is_active=1
              AND NOT EXISTS(
                SELECT 1 FROM support_eligibility e JOIN support_program p ON p.id=e.program_id
                WHERE e.student_id=s.id AND p.code='VOUCHER'
                  AND date(e.effective_from)<=date(w.end_date)
                  AND (e.effective_to IS NULL OR date(e.effective_to)>=date(w.start_date)))
            GROUP BY s.id
            ORDER BY s.grade,s.class_name,s.student_number;
            """;cmd.Parameters.AddWithValue("$w",workspaceId);using var r=cmd.ExecuteReader();var list=new List<SelfPayResultItem>();
        while(r.Read())list.Add(new SelfPayResultItem
        {
            Grade=r.GetInt32(0),ClassName=r.GetString(1),StudentNumber=r.GetInt32(2),StudentName=r.GetString(3),
            InstructorFee=r.GetInt64(4),OperatingFee=r.GetInt64(5),TextbookFee=r.GetInt64(6),MaterialFee=r.GetInt64(7)
        });return list;
    }

    public IReadOnlyList<VoucherResultItem> GetVoucherResults(long workspaceId)
    {
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="""
            SELECT s.grade,s.class_name,s.student_number,s.name,
              SUM(CASE WHEN a.resource_code='VOUCHER' AND ch.charge_type='INSTRUCTOR' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER' AND ch.charge_type='OPERATING' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER' AND ch.charge_type='TEXTBOOK' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER' AND ch.charge_type='MATERIAL' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER_OVER' AND ch.charge_type='INSTRUCTOR' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER_OVER' AND ch.charge_type='OPERATING' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER_OVER' AND ch.charge_type='TEXTBOOK' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN a.resource_code='VOUCHER_OVER' AND ch.charge_type='MATERIAL' THEN a.amount ELSE 0 END)
            FROM settlement_run r JOIN settlement st ON st.settlement_run_id=r.id
            JOIN student s ON s.id=st.student_id
            JOIN settlement_allocation a ON a.settlement_id=st.id AND a.resource_code IN ('VOUCHER','VOUCHER_OVER')
            JOIN charge ch ON ch.id=a.charge_id
            WHERE r.workspace_id=$workspace AND r.is_active=1
            GROUP BY s.id
            ORDER BY s.grade,s.class_name,s.student_number;
            """;
        cmd.Parameters.AddWithValue("$workspace",workspaceId);using var r=cmd.ExecuteReader();var list=new List<VoucherResultItem>();
        while(r.Read())list.Add(new VoucherResultItem
        {
            Grade=r.GetInt32(0),ClassName=r.GetString(1),StudentNumber=r.GetInt32(2),StudentName=r.GetString(3),
            VoucherInstructorFee=r.GetInt64(4),VoucherOperatingFee=r.GetInt64(5),VoucherTextbookFee=r.GetInt64(6),VoucherMaterialFee=r.GetInt64(7),
            OverInstructorFee=r.GetInt64(8),OverOperatingFee=r.GetInt64(9),OverTextbookFee=r.GetInt64(10),OverMaterialFee=r.GetInt64(11)
        });return list;
    }

    public IReadOnlyList<FreeVoucherResultItem> GetFreeVoucherResults(long workspaceId)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="""
            SELECT s.grade,s.class_name,s.student_number,s.name,
              SUM(CASE WHEN ch.charge_type='INSTRUCTOR' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN ch.charge_type='OPERATING' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN ch.charge_type='TEXTBOOK' THEN a.amount ELSE 0 END),
              SUM(CASE WHEN ch.charge_type='MATERIAL' THEN a.amount ELSE 0 END)
            FROM settlement_run r JOIN settlement st ON st.settlement_run_id=r.id
            JOIN student s ON s.id=st.student_id
            JOIN settlement_allocation a ON a.settlement_id=st.id AND a.resource_code='FREE_VOUCHER'
            JOIN charge ch ON ch.id=a.charge_id
            WHERE r.workspace_id=$workspace AND r.is_active=1
            GROUP BY s.id ORDER BY s.grade,s.class_name,s.student_number;
            """;cmd.Parameters.AddWithValue("$workspace",workspaceId);using var r=cmd.ExecuteReader();var list=new List<FreeVoucherResultItem>();
        while(r.Read())list.Add(new FreeVoucherResultItem
        {
            Grade=r.GetInt32(0),ClassName=r.GetString(1),StudentNumber=r.GetInt32(2),StudentName=r.GetString(3),
            InstructorFee=r.GetInt64(4),OperatingFee=r.GetInt64(5),TextbookFee=r.GetInt64(6),MaterialFee=r.GetInt64(7)
        });return list;
    }

    public IReadOnlyList<SettlementResourceRowItem> GetResourceMatrix(long workspaceId)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="""
            WITH fee_type(code,display_name,sort_order) AS (
              VALUES ('INSTRUCTOR','강사료',1),('OPERATING','수용비',2),('TEXTBOOK','교재비',3),('MATERIAL','재료비',4)
            ), allocation AS (
              SELECT ch.charge_type,a.resource_code,a.amount
              FROM settlement_run r JOIN settlement st ON st.settlement_run_id=r.id
              JOIN settlement_allocation a ON a.settlement_id=st.id JOIN charge ch ON ch.id=a.charge_id
              WHERE r.workspace_id=$workspace AND r.is_active=1
            )
            SELECT f.display_name,
              COALESCE(SUM(CASE WHEN a.resource_code='SELF_PAY' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='VOUCHER' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='VOUCHER_OVER' THEN a.amount ELSE 0 END),0),
              COALESCE(SUM(CASE WHEN a.resource_code='FREE_VOUCHER' THEN a.amount ELSE 0 END),0)
            FROM fee_type f LEFT JOIN allocation a ON a.charge_type=f.code
            GROUP BY f.code,f.display_name,f.sort_order ORDER BY f.sort_order;
            """;cmd.Parameters.AddWithValue("$workspace",workspaceId);using var r=cmd.ExecuteReader();var list=new List<SettlementResourceRowItem>();
        while(r.Read())list.Add(new SettlementResourceRowItem
        {
            FeeType=r.GetString(0),SelfPayAmount=r.GetInt64(1),VoucherAmount=r.GetInt64(2),VoucherOverAmount=r.GetInt64(3),FreeVoucherAmount=r.GetInt64(4)
        });return list;
    }

    private static WorkspaceMeta ReadWorkspace(SqliteConnection c,long id,SqliteTransaction? tx=null)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
            SELECT w.id,w.academic_year_id,w.name,w.start_date,w.end_date,w.settlement_order,w.source_revision,a.policy_revision
            FROM workspace w JOIN academic_year a ON a.id=w.academic_year_id WHERE w.id=$id;
            """;cmd.Parameters.AddWithValue("$id",id);using var r=cmd.ExecuteReader();if(!r.Read())throw new InvalidOperationException("작업공간을 찾지 못했습니다.");
        return new WorkspaceMeta(r.GetInt64(0),r.GetInt64(1),r.GetString(2),DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture),DateTime.Parse(r.GetString(4),CultureInfo.InvariantCulture),r.GetInt32(5),r.GetInt64(6),r.GetInt64(7));
    }

    private static List<WorkspaceMeta> ReadPredecessors(SqliteConnection c,WorkspaceMeta current,SqliteTransaction tx)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
            SELECT w.id,w.academic_year_id,w.name,w.start_date,w.end_date,w.settlement_order,w.source_revision,a.policy_revision
            FROM workspace w JOIN academic_year a ON a.id=w.academic_year_id
            WHERE w.academic_year_id=$year AND (date(w.start_date)<date($start) OR (w.start_date=$start AND w.settlement_order<$order))
            ORDER BY w.start_date,w.settlement_order;
            """;cmd.Parameters.AddWithValue("$year",current.AcademicYearId);cmd.Parameters.AddWithValue("$start",current.StartDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$order",current.Order);
        using var r=cmd.ExecuteReader();var list=new List<WorkspaceMeta>();while(r.Read())list.Add(new WorkspaceMeta(r.GetInt64(0),r.GetInt64(1),r.GetString(2),DateTime.Parse(r.GetString(3),CultureInfo.InvariantCulture),DateTime.Parse(r.GetString(4),CultureInfo.InvariantCulture),r.GetInt32(5),r.GetInt64(6),r.GetInt64(7)));return list;
    }

    private static (long Id,long SourceRevision,long PolicyRevision)? ReadActiveRun(SqliteConnection c,long workspaceId,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT id,source_revision,policy_revision FROM settlement_run WHERE workspace_id=$w AND is_active=1;";cmd.Parameters.AddWithValue("$w",workspaceId);using var r=cmd.ExecuteReader();return r.Read()?(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2)):null;}

    private static bool DependenciesCurrent(SqliteConnection c,long runId,SqliteTransaction? tx=null)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
            SELECT CASE WHEN
              (SELECT COUNT(*) FROM settlement_dependency d JOIN workspace w ON w.id=d.workspace_id
                WHERE d.settlement_run_id=$run AND d.source_revision<>w.source_revision)>0
              OR (SELECT COUNT(*) FROM settlement_dependency WHERE settlement_run_id=$run)<>
                 (SELECT COUNT(*) FROM workspace w2 JOIN settlement_run r2 ON r2.id=$run JOIN workspace cur ON cur.id=r2.workspace_id
                  WHERE w2.academic_year_id=cur.academic_year_id AND
                    (date(w2.start_date)<date(cur.start_date) OR (w2.start_date=cur.start_date AND w2.settlement_order<=cur.settlement_order)))
              THEN 0 ELSE 1 END;
            """;cmd.Parameters.AddWithValue("$run",runId);return Convert.ToInt32(cmd.ExecuteScalar())==1;
    }

    private static List<string> ReadSourcePriority(SqliteConnection c,long yearId,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT p.code FROM support_source_priority s JOIN support_program p ON p.id=s.program_id WHERE s.academic_year_id=$year ORDER BY s.priority;";cmd.Parameters.AddWithValue("$year",yearId);using var r=cmd.ExecuteReader();var list=new List<string>();while(r.Read())list.Add(r.GetString(0));return list;}

    private static List<ChargeDraft> ReadCharges(SqliteConnection c,long workspaceId,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
        SELECT c.id,e.student_id,c.actual_amount FROM charge c JOIN enrollment e ON e.id=c.enrollment_id
        JOIN workspace w ON w.id=e.workspace_id LEFT JOIN charge_type_priority p ON p.academic_year_id=w.academic_year_id AND p.charge_type=c.charge_type
        WHERE e.workspace_id=$workspace AND c.actual_amount>0
        ORDER BY e.student_id,COALESCE(p.priority,99),e.allocation_order,c.id;
        """;cmd.Parameters.AddWithValue("$workspace",workspaceId);using var r=cmd.ExecuteReader();var list=new List<ChargeDraft>();while(r.Read())list.Add(new ChargeDraft(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2)));return list;}

    private static HashSet<string> ReadEligibility(SqliteConnection c,long studentId,DateTime start,DateTime end,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT p.code FROM support_eligibility e JOIN support_program p ON p.id=e.program_id WHERE e.student_id=$student AND date(e.effective_from)<=date($end) AND (e.effective_to IS NULL OR date(e.effective_to)>=date($start));";cmd.Parameters.AddWithValue("$student",studentId);cmd.Parameters.AddWithValue("$start",start.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$end",end.ToString("yyyy-MM-dd"));using var r=cmd.ExecuteReader();var set=new HashSet<string>();while(r.Read())set.Add(r.GetString(0));return set;}

    private static long ReadBudget(SqliteConnection c,long yearId,long studentId,string code,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
        SELECT COALESCE((SELECT b.amount FROM support_budget b WHERE b.academic_year_id=$year AND b.student_id=$student AND b.program_id=p.id),
                        (SELECT y.default_budget_amount FROM academic_year_support_setting y WHERE y.academic_year_id=$year AND y.program_id=p.id),0)
        FROM support_program p WHERE p.code=$code;
        """;cmd.Parameters.AddWithValue("$year",yearId);cmd.Parameters.AddWithValue("$student",studentId);cmd.Parameters.AddWithValue("$code",code);return Convert.ToInt64(cmd.ExecuteScalar()??0);}

    private static long ReadPreviousUsed(SqliteConnection c,WorkspaceMeta current,long studentId,string code,SqliteTransaction tx)
    {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="""
        SELECT COALESCE(SUM(a.amount),0) FROM settlement_allocation a JOIN settlement s ON s.id=a.settlement_id
        JOIN settlement_run r ON r.id=s.settlement_run_id JOIN workspace w ON w.id=r.workspace_id
        WHERE s.student_id=$student AND r.is_active=1 AND a.resource_code=$code AND w.academic_year_id=$year
          AND (date(w.start_date)<date($start) OR (w.start_date=$start AND w.settlement_order<$order));
        """;cmd.Parameters.AddWithValue("$student",studentId);cmd.Parameters.AddWithValue("$code",code);cmd.Parameters.AddWithValue("$year",current.AcademicYearId);cmd.Parameters.AddWithValue("$start",current.StartDate.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$order",current.Order);return Convert.ToInt64(cmd.ExecuteScalar());}

    private sealed record WorkspaceMeta(long Id,long AcademicYearId,string Name,DateTime StartDate,DateTime EndDate,int Order,long SourceRevision,long PolicyRevision);
    private sealed record ChargeDraft(long ChargeId,long StudentId,long Amount);
    private sealed record AllocationDraft(long StudentId,long ChargeId,string Code,long Amount);
    private sealed class SummaryDraft
    {
        public long Total,SelfPay,Voucher,VoucherOver,FreeVoucher;
        public void Add(string code,long amount){switch(code){case "SELF_PAY":SelfPay+=amount;break;case "VOUCHER":Voucher+=amount;break;case "VOUCHER_OVER":VoucherOver+=amount;break;case "FREE_VOUCHER":FreeVoucher+=amount;break;default:throw new InvalidOperationException($"알 수 없는 재원: {code}");}}
    }
}
