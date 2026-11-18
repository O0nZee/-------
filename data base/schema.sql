-- SQLite schema for the graduation platform.
-- Existing records are preserved; safe to run again.
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_meta(version INTEGER PRIMARY KEY);
CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY,username TEXT NOT NULL UNIQUE COLLATE NOCASE,email TEXT NOT NULL UNIQUE COLLATE NOCASE,role TEXT NOT NULL,is_active INTEGER NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS supervisors(id TEXT PRIMARY KEY,user_id TEXT NOT NULL UNIQUE REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,quota INTEGER NOT NULL CHECK(quota>0),active_count INTEGER NOT NULL CHECK(active_count>=0 AND active_count<=quota),payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS teams(id TEXT PRIMARY KEY,leader_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,idea_id TEXT REFERENCES ideas(id) DEFERRABLE INITIALLY DEFERRED,supervisor_id TEXT REFERENCES supervisors(id) DEFERRABLE INITIALLY DEFERRED,is_archived INTEGER NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS team_members(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,student_id TEXT NOT NULL UNIQUE REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,payload TEXT NOT NULL,UNIQUE(team_id,student_id));
CREATE TABLE IF NOT EXISTS ideas(id TEXT PRIMARY KEY,student_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,team_id TEXT REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,status TEXT NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS archive(id TEXT PRIMARY KEY,title TEXT NOT NULL,year TEXT NOT NULL,team_id TEXT UNIQUE REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS reviews(id TEXT PRIMARY KEY,idea_id TEXT NOT NULL REFERENCES ideas(id) DEFERRABLE INITIALLY DEFERRED,reviewer_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS invitations(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,invitee_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,status TEXT NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS supervisor_assignments(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,supervisor_id TEXT NOT NULL REFERENCES supervisors(id) DEFERRABLE INITIALLY DEFERRED,is_active INTEGER NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS milestones(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,sequence INTEGER NOT NULL,completed INTEGER NOT NULL,payload TEXT NOT NULL,UNIQUE(id,team_id),UNIQUE(team_id,sequence));
CREATE TABLE IF NOT EXISTS tasks(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,milestone_id TEXT,status TEXT NOT NULL,due_date TEXT NOT NULL,payload TEXT NOT NULL,UNIQUE(id,team_id),FOREIGN KEY(milestone_id,team_id) REFERENCES milestones(id,team_id) DEFERRABLE INITIALLY DEFERRED);
CREATE TABLE IF NOT EXISTS task_assignments(id TEXT PRIMARY KEY,task_id TEXT NOT NULL,team_id TEXT NOT NULL,student_id TEXT NOT NULL,payload TEXT NOT NULL,UNIQUE(task_id,student_id),FOREIGN KEY(task_id,team_id) REFERENCES tasks(id,team_id) DEFERRABLE INITIALLY DEFERRED,FOREIGN KEY(team_id,student_id) REFERENCES team_members(team_id,student_id) DEFERRABLE INITIALLY DEFERRED);
CREATE TABLE IF NOT EXISTS submissions(id TEXT PRIMARY KEY,team_id TEXT NOT NULL REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,milestone_id TEXT NOT NULL,user_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,payload TEXT NOT NULL,FOREIGN KEY(milestone_id,team_id) REFERENCES milestones(id,team_id) DEFERRABLE INITIALLY DEFERRED);
CREATE TABLE IF NOT EXISTS notifications(id TEXT PRIMARY KEY,user_id TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,team_id TEXT REFERENCES teams(id) DEFERRABLE INITIALLY DEFERRED,dedup_key TEXT UNIQUE,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS mail_outbox(id TEXT PRIMARY KEY,status TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS audit_log(id TEXT PRIMARY KEY,actor_id TEXT REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,action TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_tasks_team ON tasks(team_id);
CREATE INDEX IF NOT EXISTS idx_ideas_team ON ideas(team_id,status);
CREATE INDEX IF NOT EXISTS idx_notifications_user ON notifications(user_id);
CREATE INDEX IF NOT EXISTS idx_mail_status ON mail_outbox(status);

-- Student profiles come from the same saved accounts used by registration/login.
-- This view stays current when a student registers or updates their profile.
-- Authentication hashes, salts and reset tokens are intentionally omitted.
DROP VIEW IF EXISTS students;
CREATE VIEW students AS
SELECT
    id,
    username,
    email,
    json_extract(payload, '$.profile.fullName') AS full_name,
    json_extract(payload, '$.profile.department') AS department,
    json_extract(payload, '$.profile.studyYear') AS study_year,
    json_extract(payload, '$.profile.phone') AS phone,
    json_extract(payload, '$.profile.academicIdentifier') AS academic_identifier,
    json_extract(payload, '$.profile.bio') AS bio,
    is_active,
    json_extract(payload, '$.createdAt') AS created_at,
    json_extract(payload, '$.profile.updatedAt') AS updated_at
FROM users
WHERE role = 'Student';

-- Academic content is linked to a department, study year and course.
CREATE TABLE IF NOT EXISTS study_departments(id TEXT PRIMARY KEY,name TEXT NOT NULL UNIQUE);
INSERT OR IGNORE INTO study_departments(id,name) VALUES
('computer-en','حاسب آلي إنجليزي'),
('computer-ar','حاسب آلي'),
('special-needs-tech','تكنولوجيا ذوي الاحتياجات الخاصة'),
('technology','تكنولوجيا'),
('theatre','مسرح'),
('media','إعلام'),
('art','فنية'),
('music','موسيقى');
CREATE TABLE IF NOT EXISTS study_courses(
    id TEXT PRIMARY KEY,
    department_id TEXT NOT NULL REFERENCES study_departments(id),
    study_year INTEGER NOT NULL CHECK(study_year BETWEEN 1 AND 4),
    semester INTEGER NOT NULL CHECK(semester IN (1,2)),
    name TEXT NOT NULL COLLATE NOCASE,
    description TEXT NOT NULL DEFAULT '',
    UNIQUE(department_id,study_year,semester,name)
);
CREATE TABLE IF NOT EXISTS study_resources(
    id TEXT PRIMARY KEY,
    course_id TEXT NOT NULL REFERENCES study_courses(id) DEFERRABLE INITIALLY DEFERRED,
    kind TEXT NOT NULL CHECK(kind IN ('Material','Summary','Exam')),
    title TEXT NOT NULL,
    exam_year TEXT NOT NULL DEFAULT '',
    original_file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    size_bytes INTEGER NOT NULL CHECK(size_bytes>0),
    sha256 TEXT NOT NULL,
    uploaded_by TEXT NOT NULL REFERENCES users(id) DEFERRABLE INITIALLY DEFERRED,
    created_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_study_courses_scope ON study_courses(department_id,study_year,semester);
CREATE INDEX IF NOT EXISTS idx_study_resources_course ON study_resources(course_id,kind);
