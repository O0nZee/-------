using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraduationPlatform.Infrastructure;

public sealed class PlatformOptions
{
    public string DataDirectory { get; set; } = "data";
    public string PublicUrl { get; set; } = "http://localhost:5000";
    public int MaxTeamMembers { get; set; } = 5;
    public int MaxSupervisorTeams { get; set; } = 3;
    public int MaxUploadMegabytes { get; set; } = 10;
    public bool DemoData { get; set; }
}

public static class PlatformJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
    public static T Copy<T>(T value) => Deserialize<T>(Serialize(value));
}

public sealed class PlatformSnapshot
{
    public List<User> Users { get; set; } = new();
    public List<ProjectIdea> Ideas { get; set; } = new();
    public List<ProjectArchive> Archive { get; set; } = new();
    public List<IdeaReview> Reviews { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public List<TeamInvitation> Invitations { get; set; } = new();
    public List<Supervisor> Supervisors { get; set; } = new();
    public List<SupervisorAssignment> SupervisorAssignments { get; set; } = new();
    public List<Milestone> Milestones { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
    public List<ProjectSubmission> Submissions { get; set; } = new();
    public List<PlatformNotification> Notifications { get; set; } = new();
    public List<MailRecord> Mail { get; set; } = new();
    public List<AuditRecord> Audit { get; set; } = new();
}

public sealed class SubmissionReview
{
    public string ReviewerId { get; set; }
    public string Decision { get; set; }
    public string Feedback { get; set; }
    public decimal? Grade { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ProjectSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TeamId { get; set; }
    public string MilestoneId { get; set; }
    public string UploadedBy { get; set; }
    public string Title { get; set; }
    public string OriginalFileName { get; set; }
    public string StoredFileName { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Decision { get; set; } = "PendingReview";
    public string Feedback { get; set; } = "";
    public decimal? Grade { get; set; }
    public List<SubmissionReview> Reviews { get; set; } = new();
}

public sealed class PlatformNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; }
    public string TeamId { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string Kind { get; set; }
    public string DedupKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

public sealed class MailRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string LastError { get; set; }
}

public sealed class AuditRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ActorId { get; set; }
    public string Action { get; set; }
    public string EntityId { get; set; }
    public string Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PlatformException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public PlatformException(string message, int status = 400, string code = "validation_error") : base(message)
    { Status = status; Code = code; }
}
