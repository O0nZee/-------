using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;
using Microsoft.Data.Sqlite;

namespace GraduationPlatform.Infrastructure;

public sealed class SqliteStateStore
{
    private readonly string _connectionString;
    public string DataDirectory { get; }
    public string DatabasePath { get; }

    public SqliteStateStore(string directory)
    {
        DataDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "graduation.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, Pooling = false }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;" + Schema;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    public bool IsInitialized
    {
        get
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM schema_meta WHERE version=1";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }

    public PlatformSnapshot Load()
    {
        using var db = Open();
        return new PlatformSnapshot
        {
            Users = Read<User>(db, "users"), Ideas = Read<ProjectIdea>(db, "ideas"), Archive = Read<ProjectArchive>(db, "archive"),
            Reviews = Read<IdeaReview>(db, "reviews"), Teams = Read<Team>(db, "teams"), Invitations = Read<TeamInvitation>(db, "invitations"),
            Supervisors = Read<Supervisor>(db, "supervisors"), SupervisorAssignments = Read<SupervisorAssignment>(db, "supervisor_assignments"),
            Milestones = Read<Milestone>(db, "milestones"), Tasks = Read<TaskItem>(db, "tasks"),
            Submissions = Read<ProjectSubmission>(db, "submissions"), Notifications = Read<PlatformNotification>(db, "notifications"),
            Mail = Read<MailRecord>(db, "mail_outbox"), Audit = Read<AuditRecord>(db, "audit_log")
        };
    }

    private static List<T> Read<T>(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM " + table + " ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        var items = new List<T>();
        while (reader.Read()) items.Add(PlatformJson.Deserialize<T>(reader.GetString(0)));
        return items;
    }

    public void Save(PlatformSnapshot state)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var clear = db.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "PRAGMA defer_foreign_keys=ON; DELETE FROM task_assignments; DELETE FROM team_members; DELETE FROM audit_log; DELETE FROM mail_outbox; DELETE FROM notifications; DELETE FROM submissions; DELETE FROM tasks; DELETE FROM milestones; DELETE FROM reviews; DELETE FROM invitations; DELETE FROM supervisor_assignments; DELETE FROM teams; DELETE FROM ideas; DELETE FROM supervisors; DELETE FROM archive; DELETE FROM users;";
            clear.ExecuteNonQuery();
        }
        foreach (var u in state.Users) Insert(db, tx, "users", u.Id, u, ("username",u.Username),("email",u.Email),("role",u.Role.ToString()),("is_active",u.IsActive));
        foreach (var s in state.Supervisors) Insert(db, tx, "supervisors", s.Id, s, ("user_id",s.UserId),("quota",s.MaxSupervisedTeams),("active_count",s.ActiveTeamsCount));
        foreach (var t in state.Teams)
        {
            Insert(db, tx, "teams", t.Id, t, ("leader_id",t.LeaderStudentId),("idea_id",t.ProjectIdeaId),("supervisor_id",t.AssignedSupervisorId),("is_archived",t.IsArchived));
            foreach (var m in t.Members) Insert(db, tx, "team_members", m.Id, m, ("team_id",t.Id),("student_id",m.StudentId));
        }
        foreach (var i in state.Ideas) Insert(db, tx, "ideas", i.Id, i, ("student_id",i.SubmitterStudentId),("team_id",i.TeamId),("status",i.Status.ToString()));
        foreach (var a in state.Archive) Insert(db, tx, "archive", a.Id, a, ("title",a.Title),("year",a.AcademicYear),("team_id",a.TeamId));
        foreach (var r in state.Reviews) Insert(db, tx, "reviews", r.Id, r, ("idea_id",r.ProjectIdeaId),("reviewer_id",r.SupervisorId));
        foreach (var i in state.Invitations) Insert(db, tx, "invitations", i.Id, i, ("team_id",i.TeamId),("invitee_id",i.InviteeStudentId),("status",i.Status.ToString()));
        foreach (var a in state.SupervisorAssignments) Insert(db, tx, "supervisor_assignments", a.Id, a, ("team_id",a.TeamId),("supervisor_id",a.SupervisorId),("is_active",a.IsActive));
        foreach (var m in state.Milestones) Insert(db, tx, "milestones", m.Id, m, ("team_id",m.TeamId),("sequence",m.SequenceOrder),("completed",m.IsCompleted));
        foreach (var t in state.Tasks)
        {
            Insert(db, tx, "tasks", t.Id, t, ("team_id",t.TeamId),("milestone_id",t.MilestoneId),("status",t.Status.ToString()),("due_date",t.DueDate.ToUniversalTime().ToString("o")));
            foreach (var a in t.Assignments) Insert(db, tx, "task_assignments", a.Id, a, ("task_id",t.Id),("team_id",t.TeamId),("student_id",a.AssignedStudentId));
        }
        foreach (var s in state.Submissions) Insert(db, tx, "submissions", s.Id, s, ("team_id",s.TeamId),("milestone_id",s.MilestoneId),("user_id",s.UploadedBy));
        foreach (var n in state.Notifications) Insert(db, tx, "notifications", n.Id, n, ("user_id",n.UserId),("team_id",n.TeamId),("dedup_key",n.DedupKey));
        foreach (var m in state.Mail) Insert(db, tx, "mail_outbox", m.Id, m, ("status",m.Status),("created_at",m.CreatedAt.ToString("o")));
        foreach (var a in state.Audit) Insert(db, tx, "audit_log", a.Id, a, ("actor_id",a.ActorId),("action",a.Action),("created_at",a.CreatedAt.ToString("o")));
        using (var meta = db.CreateCommand())
        {
            meta.Transaction = tx; meta.CommandText = "INSERT OR IGNORE INTO schema_meta(version) VALUES(1)"; meta.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void Insert(SqliteConnection db, SqliteTransaction tx, string table, string id, object payload, params (string Column, object Value)[] fields)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        var columns = new List<string> { "id", "payload" }; var parameters = new List<string> { "$id", "$payload" };
        cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$payload",PlatformJson.Serialize(payload));
        for (int i=0;i<fields.Length;i++)
        {
            string parameter = "$p"+i; columns.Add(fields[i].Column); parameters.Add(parameter);
            cmd.Parameters.AddWithValue(parameter, fields[i].Value ?? DBNull.Value);
        }
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(',',columns)}) VALUES ({string.Join(',',parameters)})";
        cmd.ExecuteNonQuery();
    }

    public string CreateBackup()
    {
        string backupDirectory = Path.Combine(DataDirectory,"backups"); Directory.CreateDirectory(backupDirectory);
        string path = Path.Combine(backupDirectory,$"graduation-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.db");
        using var source = Open(); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false }.ToString());
        target.Open(); source.BackupDatabase(target); return path;
    }

    private const string Schema = """
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
    """;
}
