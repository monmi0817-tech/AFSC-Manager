PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_info (
    version INTEGER NOT NULL,
    applied_at TEXT NOT NULL
);

INSERT INTO schema_info(version, applied_at)
SELECT 1, datetime('now')
WHERE NOT EXISTS (SELECT 1 FROM schema_info);

CREATE TABLE IF NOT EXISTS academic_year (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    year INTEGER NOT NULL UNIQUE CHECK(year BETWEEN 2000 AND 2200),
    policy_revision INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS workspace (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    name TEXT NOT NULL COLLATE NOCASE,
    start_date TEXT NOT NULL,
    end_date TEXT NOT NULL,
    settlement_order INTEGER NOT NULL DEFAULT 0,
    source_revision INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(length(trim(name)) > 0),
    CHECK(date(end_date) >= date(start_date)),
    UNIQUE(academic_year_id, name),
    UNIQUE(academic_year_id, start_date, settlement_order)
);

CREATE TABLE IF NOT EXISTS student (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    grade INTEGER NOT NULL CHECK(grade BETWEEN 1 AND 6),
    class_name TEXT NOT NULL,
    student_number INTEGER NOT NULL CHECK(student_number > 0),
    name TEXT NOT NULL,
    note TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(length(trim(class_name)) > 0),
    CHECK(length(trim(name)) > 0),
    UNIQUE(academic_year_id, grade, class_name, student_number)
);

CREATE TABLE IF NOT EXISTS support_program (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    code TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    default_budget_amount INTEGER NOT NULL DEFAULT 0 CHECK(default_budget_amount >= 0),
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
);

INSERT OR IGNORE INTO support_program(code, display_name, default_budget_amount)
VALUES ('VOUCHER', '방과후 이용권', 300000),
       ('FREE_VOUCHER', '자유수강권', 0);

CREATE TABLE IF NOT EXISTS support_eligibility (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL REFERENCES student(id) ON DELETE CASCADE,
    program_id INTEGER NOT NULL REFERENCES support_program(id) ON DELETE RESTRICT,
    effective_from TEXT NOT NULL,
    effective_to TEXT,
    note TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(effective_to IS NULL OR date(effective_to) >= date(effective_from)),
    UNIQUE(student_id, program_id, effective_from)
);

CREATE TABLE IF NOT EXISTS support_budget (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    student_id INTEGER NOT NULL REFERENCES student(id) ON DELETE CASCADE,
    program_id INTEGER NOT NULL REFERENCES support_program(id) ON DELETE RESTRICT,
    amount INTEGER NOT NULL CHECK(amount >= 0),
    revision INTEGER NOT NULL DEFAULT 0,
    change_reason TEXT,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(academic_year_id, student_id, program_id)
);

CREATE TABLE IF NOT EXISTS academic_year_support_setting (
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE CASCADE,
    program_id INTEGER NOT NULL REFERENCES support_program(id) ON DELETE CASCADE,
    default_budget_amount INTEGER NOT NULL DEFAULT 0 CHECK(default_budget_amount >= 0),
    PRIMARY KEY(academic_year_id, program_id)
);

CREATE TABLE IF NOT EXISTS support_policy_grade (
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE CASCADE,
    program_id INTEGER NOT NULL REFERENCES support_program(id) ON DELETE CASCADE,
    grade INTEGER NOT NULL CHECK(grade BETWEEN 1 AND 6),
    PRIMARY KEY(academic_year_id, program_id, grade)
);

CREATE TABLE IF NOT EXISTS support_source_priority (
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE CASCADE,
    program_id INTEGER NOT NULL REFERENCES support_program(id) ON DELETE CASCADE,
    priority INTEGER NOT NULL CHECK(priority > 0),
    PRIMARY KEY(academic_year_id, program_id),
    UNIQUE(academic_year_id, priority)
);

CREATE TABLE IF NOT EXISTS charge_type_priority (
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE CASCADE,
    charge_type TEXT NOT NULL CHECK(charge_type IN ('INSTRUCTOR','OPERATING','TEXTBOOK','MATERIAL','OTHER')),
    priority INTEGER NOT NULL CHECK(priority > 0),
    PRIMARY KEY(academic_year_id, charge_type),
    UNIQUE(academic_year_id, priority)
);

CREATE TABLE IF NOT EXISTS department (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    name TEXT NOT NULL COLLATE NOCASE,
    section_name TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
    weekdays TEXT,
    instructor_name TEXT,
    note TEXT,
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1)),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(length(trim(name)) > 0),
    UNIQUE(academic_year_id, name, section_name)
);

CREATE TABLE IF NOT EXISTS department_fee (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    department_id INTEGER NOT NULL REFERENCES department(id) ON DELETE CASCADE,
    charge_type TEXT NOT NULL CHECK(charge_type IN ('INSTRUCTOR','OPERATING','TEXTBOOK','MATERIAL','OTHER')),
    amount INTEGER NOT NULL DEFAULT 0 CHECK(amount >= 0),
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(department_id, charge_type)
);

CREATE TABLE IF NOT EXISTS department_priority (
    academic_year_id INTEGER NOT NULL REFERENCES academic_year(id) ON DELETE CASCADE,
    department_id INTEGER NOT NULL REFERENCES department(id) ON DELETE CASCADE,
    priority INTEGER NOT NULL CHECK(priority > 0),
    PRIMARY KEY(academic_year_id, department_id),
    UNIQUE(academic_year_id, priority)
);

CREATE TABLE IF NOT EXISTS enrollment (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    workspace_id INTEGER NOT NULL REFERENCES workspace(id) ON DELETE CASCADE,
    student_id INTEGER NOT NULL REFERENCES student(id) ON DELETE RESTRICT,
    department_id INTEGER NOT NULL REFERENCES department(id) ON DELETE RESTRICT,
    status TEXT NOT NULL DEFAULT 'ACTIVE' CHECK(status IN ('ACTIVE','CANCELLED')),
    enrolled_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    cancelled_at TEXT,
    change_reason TEXT,
    allocation_order INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK(status <> 'CANCELLED' OR cancelled_at IS NOT NULL)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_enrollment_active
ON enrollment(workspace_id, student_id, department_id)
WHERE status = 'ACTIVE';

CREATE TABLE IF NOT EXISTS charge (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    enrollment_id INTEGER NOT NULL REFERENCES enrollment(id) ON DELETE CASCADE,
    charge_type TEXT NOT NULL CHECK(charge_type IN ('INSTRUCTOR','OPERATING','TEXTBOOK','MATERIAL','OTHER')),
    base_amount INTEGER NOT NULL CHECK(base_amount >= 0),
    actual_amount INTEGER NOT NULL CHECK(actual_amount >= 0),
    change_reason TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(enrollment_id, charge_type)
);

CREATE TABLE IF NOT EXISTS change_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    workspace_id INTEGER REFERENCES workspace(id) ON DELETE SET NULL,
    entity_type TEXT NOT NULL,
    entity_id INTEGER NOT NULL,
    action TEXT NOT NULL,
    field_name TEXT,
    old_value TEXT,
    new_value TEXT,
    reason TEXT,
    changed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS settlement_run (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    workspace_id INTEGER NOT NULL REFERENCES workspace(id) ON DELETE CASCADE,
    source_revision INTEGER NOT NULL,
    policy_revision INTEGER NOT NULL,
    generated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN (0,1))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_settlement_run_active
ON settlement_run(workspace_id)
WHERE is_active = 1;

CREATE TABLE IF NOT EXISTS settlement_dependency (
    settlement_run_id INTEGER NOT NULL REFERENCES settlement_run(id) ON DELETE CASCADE,
    workspace_id INTEGER NOT NULL REFERENCES workspace(id) ON DELETE RESTRICT,
    source_revision INTEGER NOT NULL,
    PRIMARY KEY(settlement_run_id, workspace_id)
);

CREATE TABLE IF NOT EXISTS settlement (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    settlement_run_id INTEGER NOT NULL REFERENCES settlement_run(id) ON DELETE CASCADE,
    student_id INTEGER NOT NULL REFERENCES student(id) ON DELETE RESTRICT,
    total_charge INTEGER NOT NULL CHECK(total_charge >= 0),
    self_pay_amount INTEGER NOT NULL DEFAULT 0 CHECK(self_pay_amount >= 0),
    voucher_amount INTEGER NOT NULL DEFAULT 0 CHECK(voucher_amount >= 0),
    voucher_over_amount INTEGER NOT NULL DEFAULT 0 CHECK(voucher_over_amount >= 0),
    free_voucher_amount INTEGER NOT NULL DEFAULT 0 CHECK(free_voucher_amount >= 0),
    UNIQUE(settlement_run_id, student_id),
    CHECK(total_charge = self_pay_amount + voucher_amount + voucher_over_amount + free_voucher_amount)
);

CREATE TABLE IF NOT EXISTS settlement_allocation (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    settlement_id INTEGER NOT NULL REFERENCES settlement(id) ON DELETE CASCADE,
    charge_id INTEGER NOT NULL REFERENCES charge(id) ON DELETE RESTRICT,
    resource_code TEXT NOT NULL CHECK(resource_code IN ('SELF_PAY','VOUCHER','VOUCHER_OVER','FREE_VOUCHER')),
    amount INTEGER NOT NULL CHECK(amount > 0),
    UNIQUE(settlement_id, charge_id, resource_code)
);

CREATE INDEX IF NOT EXISTS ix_workspace_year_date
ON workspace(academic_year_id, start_date, settlement_order);
CREATE INDEX IF NOT EXISTS ix_student_year_position
ON student(academic_year_id, grade, class_name, student_number);
CREATE INDEX IF NOT EXISTS ix_student_name ON student(name);
CREATE INDEX IF NOT EXISTS ix_eligibility_student_period
ON support_eligibility(student_id, program_id, effective_from, effective_to);
CREATE INDEX IF NOT EXISTS ix_department_year_name
ON department(academic_year_id, name, section_name);
CREATE INDEX IF NOT EXISTS ix_enrollment_workspace_status
ON enrollment(workspace_id, status);
CREATE INDEX IF NOT EXISTS ix_enrollment_student ON enrollment(student_id);
CREATE INDEX IF NOT EXISTS ix_enrollment_department ON enrollment(department_id);
CREATE INDEX IF NOT EXISTS ix_charge_enrollment_type ON charge(enrollment_id, charge_type);
CREATE INDEX IF NOT EXISTS ix_history_workspace_date ON change_history(workspace_id, changed_at);
CREATE INDEX IF NOT EXISTS ix_allocation_settlement_resource
ON settlement_allocation(settlement_id, resource_code);
CREATE INDEX IF NOT EXISTS ix_allocation_charge ON settlement_allocation(charge_id);
CREATE INDEX IF NOT EXISTS ix_settlement_run_workspace_active
ON settlement_run(workspace_id, is_active);
CREATE INDEX IF NOT EXISTS ix_settlement_run_active_id
ON settlement_run(is_active, id);
CREATE INDEX IF NOT EXISTS ix_settlement_student_run
ON settlement(student_id, settlement_run_id);

-- 이전 버전에서 이미 생성된 학년도에도 지원금 기본 정책을 보완한다.
INSERT OR IGNORE INTO academic_year_support_setting(academic_year_id,program_id,default_budget_amount)
SELECT a.id,p.id,p.default_budget_amount FROM academic_year a CROSS JOIN support_program p;

INSERT OR IGNORE INTO support_source_priority(academic_year_id,program_id,priority)
SELECT a.id,p.id,CASE p.code WHEN 'VOUCHER' THEN 1 ELSE 2 END
FROM academic_year a CROSS JOIN support_program p WHERE p.code IN ('VOUCHER','FREE_VOUCHER');

INSERT OR IGNORE INTO charge_type_priority(academic_year_id,charge_type,priority)
SELECT id,'INSTRUCTOR',1 FROM academic_year
UNION ALL SELECT id,'OPERATING',2 FROM academic_year
UNION ALL SELECT id,'TEXTBOOK',3 FROM academic_year
UNION ALL SELECT id,'MATERIAL',4 FROM academic_year
UNION ALL SELECT id,'OTHER',5 FROM academic_year;

INSERT OR IGNORE INTO support_policy_grade(academic_year_id,program_id,grade)
SELECT a.id,p.id,3 FROM academic_year a CROSS JOIN support_program p
WHERE a.year=2026 AND p.code='VOUCHER';

UPDATE schema_info SET version=5, applied_at=datetime('now');
