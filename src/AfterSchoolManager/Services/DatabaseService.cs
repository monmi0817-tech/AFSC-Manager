using System.Globalization;
using System.IO;
using AfterSchoolManager.Models;
using Microsoft.Data.Sqlite;

namespace AfterSchoolManager.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        const string resourceName = "AfterSchoolManager.Data.schema.sql";
        using var schemaStream = typeof(DatabaseService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"내장 데이터베이스 스키마를 찾을 수 없습니다: {resourceName}");
        using var schemaReader = new StreamReader(schemaStream);
        var schema = schemaReader.ReadToEnd();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkspaceItem> GetWorkspaces()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.id, w.academic_year_id, a.year, w.name, w.start_date, w.end_date, w.source_revision
            FROM workspace w JOIN academic_year a ON a.id=w.academic_year_id
            ORDER BY w.start_date DESC, w.settlement_order DESC, w.name;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<WorkspaceItem>();
        while (reader.Read()) result.Add(new WorkspaceItem
        {
            Id = reader.GetInt64(0), AcademicYearId = reader.GetInt64(1), AcademicYear = reader.GetInt32(2),
            Name = reader.GetString(3), StartDate = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            EndDate = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture), SourceRevision = reader.GetInt64(6)
        });
        return result;
    }

    public long CreateWorkspace(string name, int year, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("작업공간명을 입력하세요.");
        if (endDate.Date < startDate.Date) throw new ArgumentException("종료일은 시작일보다 빠를 수 없습니다.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        long academicYearId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO academic_year(year) VALUES($year); SELECT id FROM academic_year WHERE year=$year;";
            cmd.Parameters.AddWithValue("$year", year);
            academicYearId = Convert.ToInt64(cmd.ExecuteScalar());
        }
        EnsureYearPolicyDefaults(connection, transaction, academicYearId, year);
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO workspace(academic_year_id,name,start_date,end_date,settlement_order)
                VALUES($yearId,$name,$start,$end,
                  COALESCE((SELECT MAX(settlement_order)+1 FROM workspace WHERE academic_year_id=$yearId AND start_date=$start),0));
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$yearId", academicYearId);
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            transaction.Commit();
            return id;
        }
    }

    private static void EnsureYearPolicyDefaults(SqliteConnection connection, SqliteTransaction transaction, long academicYearId, int year)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO academic_year_support_setting(academic_year_id,program_id,default_budget_amount)
            SELECT $yearId,id,default_budget_amount FROM support_program;
            INSERT OR IGNORE INTO support_source_priority(academic_year_id,program_id,priority)
            SELECT $yearId,id,CASE code WHEN 'VOUCHER' THEN 1 ELSE 2 END
            FROM support_program WHERE code IN ('VOUCHER','FREE_VOUCHER');
            INSERT OR IGNORE INTO charge_type_priority(academic_year_id,charge_type,priority)
            VALUES($yearId,'INSTRUCTOR',1),($yearId,'OPERATING',2),($yearId,'TEXTBOOK',3),($yearId,'MATERIAL',4),($yearId,'OTHER',5);
            INSERT OR IGNORE INTO support_policy_grade(academic_year_id,program_id,grade)
            SELECT $yearId,id,3 FROM support_program WHERE code='VOUCHER' AND $year=2026;
            """;
        cmd.Parameters.AddWithValue("$yearId", academicYearId);
        cmd.Parameters.AddWithValue("$year", year);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<StudentItem> GetStudents(long academicYearId, string? keyword = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id,s.grade,s.class_name,s.student_number,s.name,s.note,
              CASE
                WHEN EXISTS(SELECT 1 FROM support_eligibility e JOIN support_program p ON p.id=e.program_id WHERE e.student_id=s.id AND p.code='VOUCHER')
                 AND EXISTS(SELECT 1 FROM support_eligibility e JOIN support_program p ON p.id=e.program_id WHERE e.student_id=s.id AND p.code='FREE_VOUCHER')
                  THEN '방과후 이용권 + 자유수강권'
                WHEN EXISTS(SELECT 1 FROM support_eligibility e JOIN support_program p ON p.id=e.program_id WHERE e.student_id=s.id AND p.code='VOUCHER') THEN '방과후 이용권'
                WHEN EXISTS(SELECT 1 FROM support_eligibility e JOIN support_program p ON p.id=e.program_id WHERE e.student_id=s.id AND p.code='FREE_VOUCHER') THEN '자유수강권'
                ELSE '일반' END support_type
            FROM student s
            WHERE s.academic_year_id=$yearId
              AND ($keyword='' OR s.name LIKE '%'||$keyword||'%' OR CAST(s.grade AS TEXT)||'-'||s.class_name||'-'||s.student_number LIKE '%'||$keyword||'%')
            ORDER BY s.grade,s.class_name,s.student_number,s.name;
            """;
        command.Parameters.AddWithValue("$yearId", academicYearId);
        command.Parameters.AddWithValue("$keyword", keyword?.Trim() ?? "");
        using var reader = command.ExecuteReader();
        var result = new List<StudentItem>();
        while (reader.Read()) result.Add(new StudentItem
        {
            Id = reader.GetInt64(0), Grade = reader.GetInt32(1), ClassName = reader.GetString(2),
            StudentNumber = reader.GetInt32(3), Name = reader.GetString(4),
            Note = reader.IsDBNull(5) ? null : reader.GetString(5), SupportType = reader.GetString(6)
        });
        return result;
    }

    public long AddStudent(long academicYearId, int grade, string className, int number, string name, string? note)
    {
        ValidateStudent(grade, className, number, name);
        using var connection = Open();using var transaction=connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText = """
            INSERT INTO student(academic_year_id,grade,class_name,student_number,name,note)
            VALUES($year,$grade,$class,$number,$name,$note);
            SELECT last_insert_rowid();
            """;
        AddStudentParameters(command, academicYearId, grade, className, number, name, note);
        var id=Convert.ToInt64(command.ExecuteScalar());
        SyncVoucherEligibility(connection,transaction,academicYearId);
        IncrementYearRevision(connection,transaction,academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"STUDENT",id,"ADD","student",null,$"{grade}-{className.Trim()}-{number} {name.Trim()}","학생정보 추가");
        transaction.Commit();return id;
    }

    public void UpdateStudent(long id, long academicYearId, int grade, string className, int number, string name, string? note)
    {
        ValidateStudent(grade, className, number, name);
        using var connection = Open();using var transaction=connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText = """
            UPDATE student SET grade=$grade,class_name=$class,student_number=$number,name=$name,
              note=$note,updated_at=CURRENT_TIMESTAMP
            WHERE id=$id AND academic_year_id=$year;
            """;
        AddStudentParameters(command, academicYearId, grade, className, number, name, note);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("수정할 학생을 찾지 못했습니다.");
        SyncVoucherEligibility(connection,transaction,academicYearId);
        IncrementYearRevision(connection,transaction,academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"STUDENT",id,"UPDATE","student",null,$"{grade}-{className.Trim()}-{number} {name.Trim()}","학생정보 수정");
        transaction.Commit();
    }

    private static void ValidateStudent(int grade, string className, int number, string name)
    {
        if (grade is < 1 or > 6) throw new ArgumentException("학년은 1~6이어야 합니다.");
        if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("반을 입력하세요.");
        if (number <= 0) throw new ArgumentException("번호는 1 이상이어야 합니다.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("이름을 입력하세요.");
    }

    private static void AddStudentParameters(SqliteCommand command, long yearId, int grade, string className, int number, string name, string? note)
    {
        command.Parameters.AddWithValue("$year", yearId);
        command.Parameters.AddWithValue("$grade", grade);
        command.Parameters.AddWithValue("$class", className.Trim());
        command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
    }

    public void DeleteStudent(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM student WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteAllStudents(long academicYearId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM student WHERE academic_year_id=$id;";
        command.Parameters.AddWithValue("$id", academicYearId);
        command.ExecuteNonQuery();
    }

    public StudentItem FindStudent(long academicYearId, int grade, string className, int number, string name)
    {
        var candidate = GetStudents(academicYearId).SingleOrDefault(x => x.Grade == grade && x.ClassName == className.Trim() && x.StudentNumber == number)
            ?? throw new InvalidOperationException("학생명단에서 학년·반·번호가 일치하는 학생을 찾지 못했습니다.");
        if (!string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.CurrentCulture))
            throw new InvalidOperationException($"학생 이름이 일치하지 않습니다. 학생명단: {candidate.Name}");
        return candidate;
    }

    public IReadOnlyList<EligibilityItem> GetEligibilities(long academicYearId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.id,s.id,s.grade,s.class_name,s.student_number,s.name,p.code,p.display_name,e.effective_from,e.effective_to
            FROM support_eligibility e
            JOIN student s ON s.id=e.student_id JOIN support_program p ON p.id=e.program_id
            WHERE s.academic_year_id=$year
            ORDER BY p.code,s.grade,s.class_name,s.student_number;
            """;
        command.Parameters.AddWithValue("$year", academicYearId);
        using var reader = command.ExecuteReader();
        var result = new List<EligibilityItem>();
        while (reader.Read()) result.Add(new EligibilityItem
        {
            Id=reader.GetInt64(0), StudentId=reader.GetInt64(1), Grade=reader.GetInt32(2), ClassName=reader.GetString(3),
            StudentNumber=reader.GetInt32(4), StudentName=reader.GetString(5), ProgramCode=reader.GetString(6), ProgramName=reader.GetString(7),
            EffectiveFrom=DateTime.Parse(reader.GetString(8),CultureInfo.InvariantCulture),
            EffectiveTo=reader.IsDBNull(9)?null:DateTime.Parse(reader.GetString(9),CultureInfo.InvariantCulture)
        });
        return result;
    }

    public void AddEligibility(long academicYearId, long studentId, string programCode, DateTime effectiveFrom)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        long programId,eligibilityId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT id FROM support_program WHERE code=$code AND is_active=1;";
            cmd.Parameters.AddWithValue("$code", programCode);
            programId = Convert.ToInt64(cmd.ExecuteScalar() ?? throw new InvalidOperationException("지원 제도를 찾지 못했습니다."));
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO support_eligibility(student_id,program_id,effective_from)
                SELECT $student,$program,$from
                WHERE EXISTS(SELECT 1 FROM student WHERE id=$student AND academic_year_id=$year)
                  AND NOT EXISTS(SELECT 1 FROM support_eligibility WHERE student_id=$student AND program_id=$program);
                """;
            cmd.Parameters.AddWithValue("$student", studentId); cmd.Parameters.AddWithValue("$program", programId);
            cmd.Parameters.AddWithValue("$from", effectiveFrom.ToString("yyyy-MM-dd")); cmd.Parameters.AddWithValue("$year", academicYearId);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("이미 등록된 지원 대상이거나 학생 학년도가 다릅니다.");
        }
        using(var idCmd=connection.CreateCommand()){idCmd.Transaction=transaction;idCmd.CommandText="SELECT last_insert_rowid();";eligibilityId=Convert.ToInt64(idCmd.ExecuteScalar());}
        IncrementYearRevision(connection, transaction, academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_ELIGIBILITY",eligibilityId,"ADD","program",null,programCode,"지원 대상자 등록");
        transaction.Commit();
    }

    public void DeleteEligibility(long academicYearId, long id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction; cmd.CommandText = "DELETE FROM support_eligibility WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
        }
        IncrementYearRevision(connection, transaction, academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_ELIGIBILITY",id,"DELETE","eligibility",id.ToString(),null,"지원 대상자 삭제");
        transaction.Commit();
    }

    public void DeleteAllEligibilities(long academicYearId)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="""
                DELETE FROM support_eligibility
                WHERE student_id IN (SELECT id FROM student WHERE academic_year_id=$year);
                """;cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();
        }
        IncrementYearRevision(connection,transaction,academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_ELIGIBILITY",academicYearId,"DELETE_ALL","eligibility",null,null,"지원대상자 전체 삭제");
        transaction.Commit();
    }

    public IReadOnlyList<DepartmentItem> GetDepartments(long academicYearId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id,d.name,d.section_name,d.weekdays,d.instructor_name,
              COALESCE(MAX(CASE WHEN f.charge_type='INSTRUCTOR' THEN f.amount END),0),
              COALESCE(MAX(CASE WHEN f.charge_type='OPERATING' THEN f.amount END),0),
              COALESCE(MAX(CASE WHEN f.charge_type='TEXTBOOK' THEN f.amount END),0),
              COALESCE(MAX(CASE WHEN f.charge_type='MATERIAL' THEN f.amount END),0)
            FROM department d LEFT JOIN department_fee f ON f.department_id=d.id
            WHERE d.academic_year_id=$year AND d.is_active=1
            GROUP BY d.id ORDER BY d.name,d.section_name;
            """;
        command.Parameters.AddWithValue("$year", academicYearId);
        using var reader = command.ExecuteReader();
        var result = new List<DepartmentItem>();
        while (reader.Read()) result.Add(new DepartmentItem
        {
            Id=reader.GetInt64(0),Name=reader.GetString(1),SectionName=reader.GetString(2),
            Weekdays=reader.IsDBNull(3)?null:reader.GetString(3),InstructorName=reader.IsDBNull(4)?null:reader.GetString(4),
            InstructorFee=reader.GetInt64(5),OperatingFee=reader.GetInt64(6),TextbookFee=reader.GetInt64(7),MaterialFee=reader.GetInt64(8)
        });
        return result;
    }

    public long SaveDepartment(long? id, long academicYearId, string name, string section, string? weekdays, string? instructor, long instructorFee, long operatingFee, long textbookFee, long materialFee)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("부서명을 입력하세요.");
        if (new[]{instructorFee,operatingFee,textbookFee,materialFee}.Any(x=>x<0)) throw new ArgumentException("금액은 0 이상이어야 합니다.");
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        long departmentId;
        using (var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;
            if (id is null)
            {
                cmd.CommandText="""INSERT INTO department(academic_year_id,name,section_name,weekdays,instructor_name) VALUES($year,$name,$section,$weekdays,$instructor); SELECT last_insert_rowid();""";
            }
            else
            {
                cmd.CommandText="""UPDATE department SET name=$name,section_name=$section,weekdays=$weekdays,instructor_name=$instructor,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND academic_year_id=$year; SELECT $id;""";
                cmd.Parameters.AddWithValue("$id",id.Value);
            }
            cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$name",name.Trim());cmd.Parameters.AddWithValue("$section",section?.Trim()??"");
            cmd.Parameters.AddWithValue("$weekdays",string.IsNullOrWhiteSpace(weekdays)?DBNull.Value:weekdays.Trim());
            cmd.Parameters.AddWithValue("$instructor",string.IsNullOrWhiteSpace(instructor)?DBNull.Value:instructor.Trim());
            departmentId=Convert.ToInt64(cmd.ExecuteScalar());
        }
        SaveFee(connection,transaction,departmentId,"INSTRUCTOR",instructorFee);
        SaveFee(connection,transaction,departmentId,"OPERATING",operatingFee);
        SaveFee(connection,transaction,departmentId,"TEXTBOOK",textbookFee);
        SaveFee(connection,transaction,departmentId,"MATERIAL",materialFee);
        AddYearHistory(connection,transaction,academicYearId,"DEPARTMENT",departmentId,id is null?"ADD":"UPDATE","fees",null,$"instructor={instructorFee},operating={operatingFee},textbook={textbookFee},material={materialFee}",id is null?"부서 등록":"부서정보 수정");
        transaction.Commit(); return departmentId;
    }

    private static void SaveFee(SqliteConnection connection,SqliteTransaction transaction,long departmentId,string type,long amount)
    {
        using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="""INSERT INTO department_fee(department_id,charge_type,amount) VALUES($id,$type,$amount) ON CONFLICT(department_id,charge_type) DO UPDATE SET amount=excluded.amount,updated_at=CURRENT_TIMESTAMP;""";
        cmd.Parameters.AddWithValue("$id",departmentId);cmd.Parameters.AddWithValue("$type",type);cmd.Parameters.AddWithValue("$amount",amount);cmd.ExecuteNonQuery();
    }

    public void DeleteDepartment(long id)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();
        cmd.CommandText="DELETE FROM department WHERE id=$id;";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
    }

    public void DeleteAllDepartments(long academicYearId)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var settlements=connection.CreateCommand())
        {
            settlements.Transaction=transaction;settlements.CommandText="DELETE FROM settlement_run WHERE workspace_id IN (SELECT id FROM workspace WHERE academic_year_id=$year);";
            settlements.Parameters.AddWithValue("$year",academicYearId);settlements.ExecuteNonQuery();
        }
        using(var enrollments=connection.CreateCommand())
        {
            enrollments.Transaction=transaction;enrollments.CommandText="DELETE FROM enrollment WHERE workspace_id IN (SELECT id FROM workspace WHERE academic_year_id=$year);";
            enrollments.Parameters.AddWithValue("$year",academicYearId);enrollments.ExecuteNonQuery();
        }
        using(var departments=connection.CreateCommand())
        {
            departments.Transaction=transaction;departments.CommandText="DELETE FROM department WHERE academic_year_id=$year;";
            departments.Parameters.AddWithValue("$year",academicYearId);departments.ExecuteNonQuery();
        }
        IncrementYearRevision(connection,transaction,academicYearId);
        AddYearHistory(connection,transaction,academicYearId,"DEPARTMENT",academicYearId,"DELETE_ALL","department",null,null,"부서정보 및 연결 수강 데이터 전체 삭제");
        transaction.Commit();
    }

    public IReadOnlyList<EnrollmentItem> GetEnrollments(long workspaceId, string? keyword=null)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();
        cmd.CommandText="""
            SELECT e.id,s.id,d.id,s.grade,s.class_name,s.student_number,s.name,
              CASE WHEN d.section_name='' THEN d.name ELSE d.name||' '||d.section_name END,
              CASE
                WHEN EXISTS(SELECT 1 FROM support_eligibility x JOIN support_program p ON p.id=x.program_id WHERE x.student_id=s.id AND p.code='VOUCHER')
                 AND EXISTS(SELECT 1 FROM support_eligibility x JOIN support_program p ON p.id=x.program_id WHERE x.student_id=s.id AND p.code='FREE_VOUCHER') THEN '방과후 이용권 + 자유수강권'
                WHEN EXISTS(SELECT 1 FROM support_eligibility x JOIN support_program p ON p.id=x.program_id WHERE x.student_id=s.id AND p.code='VOUCHER') THEN '방과후 이용권'
                WHEN EXISTS(SELECT 1 FROM support_eligibility x JOIN support_program p ON p.id=x.program_id WHERE x.student_id=s.id AND p.code='FREE_VOUCHER') THEN '자유수강권'
                ELSE '일반' END,e.status,e.cancelled_at,e.change_reason,
              COALESCE(MAX(CASE WHEN c.charge_type='INSTRUCTOR' THEN c.base_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='OPERATING' THEN c.base_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='TEXTBOOK' THEN c.base_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='MATERIAL' THEN c.base_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='INSTRUCTOR' THEN c.actual_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='OPERATING' THEN c.actual_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='TEXTBOOK' THEN c.actual_amount END),0),
              COALESCE(MAX(CASE WHEN c.charge_type='MATERIAL' THEN c.actual_amount END),0)
            FROM enrollment e JOIN student s ON s.id=e.student_id JOIN department d ON d.id=e.department_id
            LEFT JOIN charge c ON c.enrollment_id=e.id
            WHERE e.workspace_id=$workspace
              AND ($keyword='' OR s.name LIKE '%'||$keyword||'%' OR d.name LIKE '%'||$keyword||'%')
            GROUP BY e.id ORDER BY CASE e.status WHEN 'ACTIVE' THEN 0 ELSE 1 END,d.name,d.section_name,s.grade,s.class_name,s.student_number;
            """;
        cmd.Parameters.AddWithValue("$workspace",workspaceId);cmd.Parameters.AddWithValue("$keyword",keyword?.Trim()??"");
        using var reader=cmd.ExecuteReader();var result=new List<EnrollmentItem>();
        while(reader.Read())result.Add(new EnrollmentItem{Id=reader.GetInt64(0),StudentId=reader.GetInt64(1),DepartmentId=reader.GetInt64(2),Grade=reader.GetInt32(3),ClassName=reader.GetString(4),StudentNumber=reader.GetInt32(5),StudentName=reader.GetString(6),DepartmentName=reader.GetString(7),SupportType=reader.GetString(8),StatusCode=reader.GetString(9),CancelledAt=reader.IsDBNull(10)?null:DateTime.Parse(reader.GetString(10),CultureInfo.InvariantCulture),ChangeReason=reader.IsDBNull(11)?null:reader.GetString(11),BaseInstructorFee=reader.GetInt64(12),BaseOperatingFee=reader.GetInt64(13),BaseTextbookFee=reader.GetInt64(14),BaseMaterialFee=reader.GetInt64(15),InstructorFee=reader.GetInt64(16),OperatingFee=reader.GetInt64(17),TextbookFee=reader.GetInt64(18),MaterialFee=reader.GetInt64(19)});
        return result;
    }

    public long AddEnrollment(long workspaceId,long studentId,long departmentId)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();long id;
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="""
                INSERT INTO enrollment(workspace_id,student_id,department_id,allocation_order)
                SELECT $workspace,$student,$department,COALESCE((SELECT MAX(allocation_order)+1 FROM enrollment WHERE workspace_id=$workspace AND student_id=$student),0)
                WHERE EXISTS(SELECT 1 FROM workspace w JOIN student s ON s.academic_year_id=w.academic_year_id WHERE w.id=$workspace AND s.id=$student)
                  AND EXISTS(SELECT 1 FROM workspace w JOIN department d ON d.academic_year_id=w.academic_year_id WHERE w.id=$workspace AND d.id=$department);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$workspace",workspaceId);cmd.Parameters.AddWithValue("$student",studentId);cmd.Parameters.AddWithValue("$department",departmentId);
            id=Convert.ToInt64(cmd.ExecuteScalar());if(id==0)throw new InvalidOperationException("학생, 부서, 작업공간의 학년도가 일치하지 않습니다.");
        }
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="""
                INSERT INTO charge(enrollment_id,charge_type,base_amount,actual_amount)
                SELECT $id,charge_type,amount,amount FROM department_fee WHERE department_id=$department;
                """;cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$department",departmentId);cmd.ExecuteNonQuery();
        }
        IncrementWorkspaceRevision(connection,transaction,workspaceId);
        AddHistory(connection,transaction,workspaceId,"ENROLLMENT",id,"ADD",null,null,$"student={studentId},department={departmentId}","수강 등록");
        transaction.Commit();return id;
    }

    public void DeleteEnrollment(long workspaceId,long id)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="DELETE FROM enrollment WHERE id=$id AND workspace_id=$workspace;";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$workspace",workspaceId);if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("삭제할 수강 데이터를 찾지 못했습니다.");}
        IncrementWorkspaceRevision(connection,transaction,workspaceId);
        AddHistory(connection,transaction,workspaceId,"ENROLLMENT",id,"DELETE",null,"active",null,"입력 오류 데이터 삭제");
        transaction.Commit();
    }

    public void DeleteAllEnrollments(long workspaceId)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var settlements=connection.CreateCommand()){settlements.Transaction=transaction;settlements.CommandText="DELETE FROM settlement_run WHERE workspace_id=$workspace;";settlements.Parameters.AddWithValue("$workspace",workspaceId);settlements.ExecuteNonQuery();}
        using(var enrollments=connection.CreateCommand()){enrollments.Transaction=transaction;enrollments.CommandText="DELETE FROM enrollment WHERE workspace_id=$workspace;";enrollments.Parameters.AddWithValue("$workspace",workspaceId);enrollments.ExecuteNonQuery();}
        IncrementWorkspaceRevision(connection,transaction,workspaceId);
        AddHistory(connection,transaction,workspaceId,"ENROLLMENT",workspaceId,"DELETE_ALL",null,null,null,"수강 데이터 전체 삭제");
        transaction.Commit();
    }

    public void CancelEnrollment(long workspaceId,long enrollmentId,DateTime cancelledAt,string reason)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("수강취소 사유를 입력하세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="""
                UPDATE enrollment SET status='CANCELLED',cancelled_at=$date,change_reason=$reason,updated_at=CURRENT_TIMESTAMP
                WHERE id=$id AND workspace_id=$workspace AND status='ACTIVE';
                """;cmd.Parameters.AddWithValue("$date",cancelledAt.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$reason",reason.Trim());cmd.Parameters.AddWithValue("$id",enrollmentId);cmd.Parameters.AddWithValue("$workspace",workspaceId);
            if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("수강중 상태의 데이터를 선택하세요.");
        }
        IncrementWorkspaceRevision(connection,transaction,workspaceId);
        AddHistory(connection,transaction,workspaceId,"ENROLLMENT",enrollmentId,"CANCEL","status","ACTIVE","CANCELLED",reason.Trim());
        transaction.Commit();
    }

    public void UpdateEnrollmentAmounts(long workspaceId,long enrollmentId,long instructor,long operating,long textbook,long material,string reason)
    {
        if(new[]{instructor,operating,textbook,material}.Any(x=>x<0))throw new ArgumentException("실제 적용금액은 0 이상이어야 합니다.");
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("금액 변경 사유를 입력하세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction();var changed=0;
        var values=new Dictionary<string,long>{{"INSTRUCTOR",instructor},{"OPERATING",operating},{"TEXTBOOK",textbook},{"MATERIAL",material}};
        foreach(var pair in values)
        {
            long chargeId,oldAmount;
            using(var read=connection.CreateCommand())
            {
                read.Transaction=transaction;read.CommandText="""
                    SELECT c.id,c.actual_amount FROM charge c JOIN enrollment e ON e.id=c.enrollment_id
                    WHERE c.enrollment_id=$enrollment AND c.charge_type=$type AND e.workspace_id=$workspace;
                    """;read.Parameters.AddWithValue("$enrollment",enrollmentId);read.Parameters.AddWithValue("$type",pair.Key);read.Parameters.AddWithValue("$workspace",workspaceId);
                using var r=read.ExecuteReader();if(!r.Read())throw new InvalidOperationException("수강 비용 데이터를 찾지 못했습니다.");chargeId=r.GetInt64(0);oldAmount=r.GetInt64(1);
            }
            if(oldAmount==pair.Value)continue;
            using(var update=connection.CreateCommand()){update.Transaction=transaction;update.CommandText="UPDATE charge SET actual_amount=$amount,change_reason=$reason,updated_at=CURRENT_TIMESTAMP WHERE id=$id;";update.Parameters.AddWithValue("$amount",pair.Value);update.Parameters.AddWithValue("$reason",reason.Trim());update.Parameters.AddWithValue("$id",chargeId);update.ExecuteNonQuery();}
            AddHistory(connection,transaction,workspaceId,"CHARGE",chargeId,"AMOUNT_CHANGE",pair.Key+".actual_amount",oldAmount.ToString(),pair.Value.ToString(),reason.Trim());changed++;
        }
        if(changed==0)throw new InvalidOperationException("변경된 금액이 없습니다.");
        IncrementWorkspaceRevision(connection,transaction,workspaceId);transaction.Commit();
    }

    public (int UpdatedEnrollments,int PreservedManualCharges) RefreshDepartmentFees(long workspaceId)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();int enrollments,preserved;
        using(var count=connection.CreateCommand()){count.Transaction=transaction;count.CommandText="SELECT COUNT(*) FROM enrollment WHERE workspace_id=$workspace AND status='ACTIVE';";count.Parameters.AddWithValue("$workspace",workspaceId);enrollments=Convert.ToInt32(count.ExecuteScalar());}
        if(enrollments==0)throw new InvalidOperationException("현재 작업공간에 수강중인 데이터가 없습니다.");
        using(var count=connection.CreateCommand()){count.Transaction=transaction;count.CommandText="SELECT COUNT(*) FROM charge c JOIN enrollment e ON e.id=c.enrollment_id WHERE e.workspace_id=$workspace AND e.status='ACTIVE' AND (c.actual_amount<>c.base_amount OR COALESCE(c.change_reason,'')<>'');";count.Parameters.AddWithValue("$workspace",workspaceId);preserved=Convert.ToInt32(count.ExecuteScalar());}
        using(var insert=connection.CreateCommand())
        {
            insert.Transaction=transaction;insert.CommandText="""
                INSERT INTO charge(enrollment_id,charge_type,base_amount,actual_amount)
                SELECT e.id,f.charge_type,f.amount,f.amount FROM enrollment e JOIN department_fee f ON f.department_id=e.department_id
                LEFT JOIN charge c ON c.enrollment_id=e.id AND c.charge_type=f.charge_type
                WHERE e.workspace_id=$workspace AND e.status='ACTIVE' AND c.id IS NULL;
                """;insert.Parameters.AddWithValue("$workspace",workspaceId);insert.ExecuteNonQuery();
        }
        using(var update=connection.CreateCommand())
        {
            update.Transaction=transaction;update.CommandText="""
                UPDATE charge
                SET actual_amount=CASE WHEN actual_amount=base_amount AND COALESCE(change_reason,'')='' THEN
                      (SELECT f.amount FROM enrollment e JOIN department_fee f ON f.department_id=e.department_id AND f.charge_type=charge.charge_type WHERE e.id=charge.enrollment_id)
                    ELSE actual_amount END,
                    base_amount=(SELECT f.amount FROM enrollment e JOIN department_fee f ON f.department_id=e.department_id AND f.charge_type=charge.charge_type WHERE e.id=charge.enrollment_id),
                    updated_at=CURRENT_TIMESTAMP
                WHERE enrollment_id IN (SELECT id FROM enrollment WHERE workspace_id=$workspace AND status='ACTIVE')
                  AND EXISTS(SELECT 1 FROM enrollment e JOIN department_fee f ON f.department_id=e.department_id AND f.charge_type=charge.charge_type WHERE e.id=charge.enrollment_id);
                """;update.Parameters.AddWithValue("$workspace",workspaceId);update.ExecuteNonQuery();
        }
        IncrementWorkspaceRevision(connection,transaction,workspaceId);
        AddHistory(connection,transaction,workspaceId,"WORKSPACE",workspaceId,"REFRESH_DEPARTMENT_FEES",null,null,$"enrollments={enrollments},preserved={preserved}","부서정보 금액 다시 불러오기");
        transaction.Commit();return(enrollments,preserved);
    }

    public IReadOnlyList<ChangeHistoryItem> GetChangeHistory(long workspaceId)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();cmd.CommandText="""
            SELECT id,changed_at,entity_type,entity_id,action,field_name,old_value,new_value,reason
            FROM change_history WHERE workspace_id=$workspace ORDER BY changed_at DESC,id DESC LIMIT 2000;
            """;cmd.Parameters.AddWithValue("$workspace",workspaceId);using var r=cmd.ExecuteReader();var list=new List<ChangeHistoryItem>();
        while(r.Read())list.Add(new ChangeHistoryItem{Id=r.GetInt64(0),ChangedAt=DateTime.Parse(r.GetString(1),CultureInfo.InvariantCulture),EntityType=r.GetString(2),EntityId=r.GetInt64(3),Action=r.GetString(4),FieldName=r.IsDBNull(5)?null:r.GetString(5),OldValue=r.IsDBNull(6)?null:r.GetString(6),NewValue=r.IsDBNull(7)?null:r.GetString(7),Reason=r.IsDBNull(8)?null:r.GetString(8)});return list;
    }

    public (StudentDetailSummary Summary,IReadOnlyList<StudentUsageItem> Usage) GetStudentDetail(long academicYearId,int grade,string className,int number,string name)
    {
        var student=FindStudent(academicYearId,grade,className,number,name);using var connection=Open();
        long Budget(string code)
        {using var cmd=connection.CreateCommand();cmd.CommandText="""
            SELECT COALESCE((SELECT amount FROM support_budget WHERE academic_year_id=$year AND student_id=$student AND program_id=p.id),
                            (SELECT default_budget_amount FROM academic_year_support_setting WHERE academic_year_id=$year AND program_id=p.id),0)
            FROM support_program p WHERE p.code=$code;
            """;cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$student",student.Id);cmd.Parameters.AddWithValue("$code",code);return Convert.ToInt64(cmd.ExecuteScalar()??0);}
        long Used(string code)
        {using var cmd=connection.CreateCommand();cmd.CommandText="""
            SELECT COALESCE(SUM(a.amount),0) FROM settlement_allocation a JOIN settlement st ON st.id=a.settlement_id
            JOIN settlement_run r ON r.id=st.settlement_run_id JOIN workspace w ON w.id=r.workspace_id
            WHERE st.student_id=$student AND r.is_active=1 AND w.academic_year_id=$year AND a.resource_code=$code;
            """;cmd.Parameters.AddWithValue("$student",student.Id);cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$code",code);return Convert.ToInt64(cmd.ExecuteScalar());}
        var summary=new StudentDetailSummary{StudentId=student.Id,Grade=student.Grade,ClassName=student.ClassName,StudentNumber=student.StudentNumber,StudentName=student.Name,SupportType=student.SupportType,VoucherBudget=Budget("VOUCHER"),VoucherUsed=Used("VOUCHER"),FreeBudget=Budget("FREE_VOUCHER"),FreeUsed=Used("FREE_VOUCHER")};
        using var usageCmd=connection.CreateCommand();usageCmd.CommandText="""
            WITH fee AS (
              SELECT e.id enrollment_id,w.id workspace_id,w.name workspace_name,d.name||CASE WHEN d.section_name='' THEN '' ELSE ' '||d.section_name END department_name,
                COALESCE(MAX(CASE WHEN c.charge_type='INSTRUCTOR' THEN c.actual_amount END),0) instructor,
                COALESCE(MAX(CASE WHEN c.charge_type='OPERATING' THEN c.actual_amount END),0) operating,
                COALESCE(MAX(CASE WHEN c.charge_type='TEXTBOOK' THEN c.actual_amount END),0) textbook,
                COALESCE(MAX(CASE WHEN c.charge_type='MATERIAL' THEN c.actual_amount END),0) material
              FROM enrollment e JOIN workspace w ON w.id=e.workspace_id JOIN department d ON d.id=e.department_id LEFT JOIN charge c ON c.enrollment_id=e.id
              WHERE e.student_id=$student AND w.academic_year_id=$year GROUP BY e.id
            ), alloc AS (
              SELECT c.enrollment_id,
                SUM(CASE WHEN a.resource_code='VOUCHER' THEN a.amount ELSE 0 END) voucher,
                SUM(CASE WHEN a.resource_code='FREE_VOUCHER' THEN a.amount ELSE 0 END) free,
                SUM(CASE WHEN a.resource_code='SELF_PAY' THEN a.amount ELSE 0 END) selfpay,
                SUM(CASE WHEN a.resource_code='VOUCHER_OVER' THEN a.amount ELSE 0 END) over
              FROM settlement_allocation a JOIN charge c ON c.id=a.charge_id JOIN settlement st ON st.id=a.settlement_id
              JOIN settlement_run r ON r.id=st.settlement_run_id WHERE st.student_id=$student AND r.is_active=1 GROUP BY c.enrollment_id
            )
            SELECT fee.workspace_name,fee.department_name,fee.instructor,fee.operating,fee.textbook,fee.material,
              COALESCE(alloc.voucher,0),COALESCE(alloc.free,0),COALESCE(alloc.selfpay,0),COALESCE(alloc.over,0)
            FROM fee LEFT JOIN alloc ON alloc.enrollment_id=fee.enrollment_id
            ORDER BY fee.workspace_id,fee.department_name;
            """;usageCmd.Parameters.AddWithValue("$student",student.Id);usageCmd.Parameters.AddWithValue("$year",academicYearId);using var ur=usageCmd.ExecuteReader();var usage=new List<StudentUsageItem>();
        while(ur.Read())usage.Add(new StudentUsageItem{WorkspaceName=ur.GetString(0),DepartmentName=ur.GetString(1),InstructorFee=ur.GetInt64(2),OperatingFee=ur.GetInt64(3),TextbookFee=ur.GetInt64(4),MaterialFee=ur.GetInt64(5),VoucherAmount=ur.GetInt64(6),FreeVoucherAmount=ur.GetInt64(7),SelfPayAmount=ur.GetInt64(8),VoucherOverAmount=ur.GetInt64(9)});
        return(summary,usage);
    }

    public (int Students,int Enrollments,int Departments,int Supported) GetDashboard(long academicYearId,long workspaceId)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();
        cmd.CommandText="""
            SELECT
              (SELECT COUNT(*) FROM student WHERE academic_year_id=$year),
              (SELECT COUNT(DISTINCT student_id) FROM enrollment WHERE workspace_id=$workspace AND status='ACTIVE'),
              (SELECT COUNT(*) FROM department WHERE academic_year_id=$year AND is_active=1),
              (SELECT COUNT(DISTINCT e.student_id) FROM support_eligibility e JOIN student s ON s.id=e.student_id WHERE s.academic_year_id=$year);
            """;cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$workspace",workspaceId);
        using var r=cmd.ExecuteReader();r.Read();return(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3));
    }

    public SupportSettingsItem GetSupportSettings(long academicYearId)
    {
        using var connection=Open();
        using var cmd=connection.CreateCommand();
        cmd.CommandText="""
            SELECT
              COALESCE(MAX(CASE WHEN p.code='VOUCHER' THEN y.default_budget_amount END),0),
              COALESCE(MAX(CASE WHEN p.code='FREE_VOUCHER' THEN y.default_budget_amount END),0),
              COALESCE((SELECT CASE WHEN p2.code='VOUCHER' THEN 'VOUCHER_FIRST' ELSE 'FREE_FIRST' END
                FROM support_source_priority sp JOIN support_program p2 ON p2.id=sp.program_id
                WHERE sp.academic_year_id=$year ORDER BY sp.priority LIMIT 1),'VOUCHER_FIRST'),
              COALESCE((SELECT group_concat(grade, ',') FROM
                (SELECT grade FROM support_policy_grade pg JOIN support_program p3 ON p3.id=pg.program_id
                 WHERE pg.academic_year_id=$year AND p3.code='VOUCHER' ORDER BY grade)),'')
            FROM support_program p LEFT JOIN academic_year_support_setting y
              ON y.program_id=p.id AND y.academic_year_id=$year;
            """;
        cmd.Parameters.AddWithValue("$year",academicYearId);
        using var r=cmd.ExecuteReader();r.Read();
        return new SupportSettingsItem{VoucherDefault=r.GetInt64(0),FreeVoucherDefault=r.GetInt64(1),SourcePriority=r.GetString(2),VoucherGrades=r.GetString(3)};
    }

    public void SaveSupportSettings(long academicYearId,long voucherDefault,long freeDefault,string priority,string grades)
    {
        if(voucherDefault<0||freeDefault<0)throw new ArgumentException("지원 한도는 0 이상이어야 합니다.");
        var parsedGrades=grades.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Select(x=>int.TryParse(x,out var g)&&g is>=1 and<=6?g:throw new ArgumentException("대상 학년은 1~6을 쉼표로 구분해 입력하세요."))
            .Distinct().OrderBy(x=>x).ToArray();
        using var connection=Open();using var transaction=connection.BeginTransaction();
        SaveYearDefault(connection,transaction,academicYearId,"VOUCHER",voucherDefault);
        SaveYearDefault(connection,transaction,academicYearId,"FREE_VOUCHER",freeDefault);
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="DELETE FROM support_source_priority WHERE academic_year_id=$year;";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();
            var codes=priority=="FREE_FIRST"?new[]{"FREE_VOUCHER","VOUCHER"}:new[]{"VOUCHER","FREE_VOUCHER"};
            for(var i=0;i<codes.Length;i++)
            {using var add=connection.CreateCommand();add.Transaction=transaction;add.CommandText="INSERT INTO support_source_priority(academic_year_id,program_id,priority) SELECT $year,id,$priority FROM support_program WHERE code=$code;";add.Parameters.AddWithValue("$year",academicYearId);add.Parameters.AddWithValue("$priority",i+1);add.Parameters.AddWithValue("$code",codes[i]);add.ExecuteNonQuery();}
        }
        using(var cmd=connection.CreateCommand())
        {
            cmd.Transaction=transaction;cmd.CommandText="DELETE FROM support_policy_grade WHERE academic_year_id=$year AND program_id=(SELECT id FROM support_program WHERE code='VOUCHER');";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();
            foreach(var grade in parsedGrades){using var add=connection.CreateCommand();add.Transaction=transaction;add.CommandText="INSERT INTO support_policy_grade(academic_year_id,program_id,grade) SELECT $year,id,$grade FROM support_program WHERE code='VOUCHER';";add.Parameters.AddWithValue("$year",academicYearId);add.Parameters.AddWithValue("$grade",grade);add.ExecuteNonQuery();}
        }
        SyncVoucherEligibility(connection,transaction,academicYearId);
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="UPDATE academic_year SET policy_revision=policy_revision+1 WHERE id=$year;";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();}
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_POLICY",academicYearId,"UPDATE","policy",null,$"voucher={voucherDefault},free={freeDefault},priority={priority},grades={grades}","학년도 지원금 정책 변경");
        transaction.Commit();
    }

    private static void SyncVoucherEligibility(SqliteConnection connection,SqliteTransaction transaction,long academicYearId)
    {
        using(var remove=connection.CreateCommand())
        {
            remove.Transaction=transaction;remove.CommandText="""
                DELETE FROM support_eligibility
                WHERE program_id=(SELECT id FROM support_program WHERE code='VOUCHER')
                  AND student_id IN (
                    SELECT s.id FROM student s
                    WHERE s.academic_year_id=$year
                      AND NOT EXISTS(SELECT 1 FROM support_policy_grade g
                        WHERE g.academic_year_id=$year AND g.program_id=(SELECT id FROM support_program WHERE code='VOUCHER') AND g.grade=s.grade));
                """;remove.Parameters.AddWithValue("$year",academicYearId);remove.ExecuteNonQuery();
        }
        using(var normalize=connection.CreateCommand())
        {
            normalize.Transaction=transaction;normalize.CommandText="""
                UPDATE support_eligibility
                SET effective_from=COALESCE(
                      (SELECT MIN(w.start_date) FROM workspace w WHERE w.academic_year_id=$year),
                      printf('%04d-03-01',(SELECT year FROM academic_year WHERE id=$year))),
                    effective_to=NULL,
                    updated_at=CURRENT_TIMESTAMP
                WHERE program_id=(SELECT id FROM support_program WHERE code='VOUCHER')
                  AND student_id IN (
                    SELECT s.id FROM student s JOIN support_policy_grade g
                      ON g.academic_year_id=s.academic_year_id
                     AND g.program_id=(SELECT id FROM support_program WHERE code='VOUCHER')
                     AND g.grade=s.grade
                    WHERE s.academic_year_id=$year);
                """;normalize.Parameters.AddWithValue("$year",academicYearId);normalize.ExecuteNonQuery();
        }
        using(var add=connection.CreateCommand())
        {
            add.Transaction=transaction;add.CommandText="""
                INSERT INTO support_eligibility(student_id,program_id,effective_from)
                SELECT s.id,p.id,
                  COALESCE((SELECT MIN(w.start_date) FROM workspace w WHERE w.academic_year_id=$year),printf('%04d-03-01',a.year))
                FROM student s JOIN academic_year a ON a.id=s.academic_year_id
                JOIN support_policy_grade g ON g.academic_year_id=s.academic_year_id AND g.grade=s.grade
                JOIN support_program p ON p.id=g.program_id AND p.code='VOUCHER'
                WHERE s.academic_year_id=$year
                  AND NOT EXISTS(SELECT 1 FROM support_eligibility e WHERE e.student_id=s.id AND e.program_id=p.id);
                """;add.Parameters.AddWithValue("$year",academicYearId);add.ExecuteNonQuery();
        }
    }

    private static void SaveYearDefault(SqliteConnection c,SqliteTransaction t,long yearId,string code,long amount)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="""
            INSERT INTO academic_year_support_setting(academic_year_id,program_id,default_budget_amount)
            SELECT $year,id,$amount FROM support_program WHERE code=$code
            ON CONFLICT(academic_year_id,program_id) DO UPDATE SET default_budget_amount=excluded.default_budget_amount;
            """;cmd.Parameters.AddWithValue("$year",yearId);cmd.Parameters.AddWithValue("$amount",amount);cmd.Parameters.AddWithValue("$code",code);cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<BudgetOverrideItem> GetBudgetOverrides(long academicYearId)
    {
        using var connection=Open();using var cmd=connection.CreateCommand();cmd.CommandText="""
            SELECT b.id,s.id,s.grade,s.class_name,s.student_number,s.name,p.code,p.display_name,b.amount,b.change_reason
            FROM support_budget b JOIN student s ON s.id=b.student_id JOIN support_program p ON p.id=b.program_id
            WHERE b.academic_year_id=$year ORDER BY p.code,s.grade,s.class_name,s.student_number;
            """;cmd.Parameters.AddWithValue("$year",academicYearId);using var r=cmd.ExecuteReader();var list=new List<BudgetOverrideItem>();
        while(r.Read())list.Add(new BudgetOverrideItem{Id=r.GetInt64(0),StudentId=r.GetInt64(1),Grade=r.GetInt32(2),ClassName=r.GetString(3),StudentNumber=r.GetInt32(4),StudentName=r.GetString(5),ProgramCode=r.GetString(6),ProgramName=r.GetString(7),Amount=r.GetInt64(8),ChangeReason=r.IsDBNull(9)?null:r.GetString(9)});return list;
    }

    public void SaveBudgetOverride(long academicYearId,long studentId,string programCode,long amount,string reason)
    {
        if(amount<0)throw new ArgumentException("지원 한도는 0 이상이어야 합니다.");
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("개별 지원금 변경 사유를 입력하세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="""
            INSERT INTO support_budget(academic_year_id,student_id,program_id,amount,change_reason)
            SELECT $year,$student,id,$amount,$reason FROM support_program WHERE code=$code
            ON CONFLICT(academic_year_id,student_id,program_id) DO UPDATE SET amount=excluded.amount,revision=support_budget.revision+1,change_reason=excluded.change_reason,updated_at=CURRENT_TIMESTAMP;
            """;cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$student",studentId);cmd.Parameters.AddWithValue("$amount",amount);cmd.Parameters.AddWithValue("$reason",reason.Trim());cmd.Parameters.AddWithValue("$code",programCode);if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("지원 제도 또는 학생을 찾지 못했습니다.");}
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="UPDATE academic_year SET policy_revision=policy_revision+1 WHERE id=$year;";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();}
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_BUDGET",studentId,"UPDATE",programCode,null,amount.ToString(),reason.Trim());
        transaction.Commit();
    }

    public void DeleteBudgetOverride(long academicYearId,long id)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="DELETE FROM support_budget WHERE id=$id AND academic_year_id=$year;";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();}
        using(var cmd=connection.CreateCommand()){cmd.Transaction=transaction;cmd.CommandText="UPDATE academic_year SET policy_revision=policy_revision+1 WHERE id=$year;";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.ExecuteNonQuery();}
        AddYearHistory(connection,transaction,academicYearId,"SUPPORT_BUDGET",id,"DELETE","override",id.ToString(),null,"학생별 한도 삭제");
        transaction.Commit();
    }

    private static void IncrementWorkspaceRevision(SqliteConnection c,SqliteTransaction t,long workspaceId)
    {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="UPDATE workspace SET source_revision=source_revision+1,updated_at=CURRENT_TIMESTAMP WHERE id=$id;";cmd.Parameters.AddWithValue("$id",workspaceId);cmd.ExecuteNonQuery();}
    private static void IncrementYearRevision(SqliteConnection c,SqliteTransaction t,long yearId)
    {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="UPDATE workspace SET source_revision=source_revision+1,updated_at=CURRENT_TIMESTAMP WHERE academic_year_id=$id;";cmd.Parameters.AddWithValue("$id",yearId);cmd.ExecuteNonQuery();}
    private static void AddHistory(SqliteConnection c,SqliteTransaction t,long workspaceId,string entity,long entityId,string action,string? field,string? oldValue,string? newValue,string reason)
    {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="INSERT INTO change_history(workspace_id,entity_type,entity_id,action,field_name,old_value,new_value,reason) VALUES($w,$e,$id,$a,$f,$o,$n,$r);";cmd.Parameters.AddWithValue("$w",workspaceId);cmd.Parameters.AddWithValue("$e",entity);cmd.Parameters.AddWithValue("$id",entityId);cmd.Parameters.AddWithValue("$a",action);cmd.Parameters.AddWithValue("$f",(object?)field??DBNull.Value);cmd.Parameters.AddWithValue("$o",(object?)oldValue??DBNull.Value);cmd.Parameters.AddWithValue("$n",(object?)newValue??DBNull.Value);cmd.Parameters.AddWithValue("$r",reason);cmd.ExecuteNonQuery();}
    private static void AddYearHistory(SqliteConnection c,SqliteTransaction t,long academicYearId,string entity,long entityId,string action,string? field,string? oldValue,string? newValue,string reason)
    {using var cmd=c.CreateCommand();cmd.Transaction=t;cmd.CommandText="INSERT INTO change_history(workspace_id,entity_type,entity_id,action,field_name,old_value,new_value,reason) SELECT id,$e,$entityId,$action,$field,$old,$new,$reason FROM workspace WHERE academic_year_id=$year;";cmd.Parameters.AddWithValue("$year",academicYearId);cmd.Parameters.AddWithValue("$e",entity);cmd.Parameters.AddWithValue("$entityId",entityId);cmd.Parameters.AddWithValue("$action",action);cmd.Parameters.AddWithValue("$field",(object?)field??DBNull.Value);cmd.Parameters.AddWithValue("$old",(object?)oldValue??DBNull.Value);cmd.Parameters.AddWithValue("$new",(object?)newValue??DBNull.Value);cmd.Parameters.AddWithValue("$reason",reason);cmd.ExecuteNonQuery();}
}
