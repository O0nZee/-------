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

    private static readonly string Schema = LoadSchema();

    private static string LoadSchema()
    {
        using var stream = typeof(SqliteStateStore).Assembly.GetManifestResourceStream("GraduationPlatform.Database.Schema.sql")
            ?? throw new InvalidOperationException("The embedded SQLite schema is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
