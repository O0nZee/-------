using GraduationPlatform.Common;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;
using GraduationPlatform.SupervisorManagement;
using System.IO.Compression;

namespace GraduationPlatform.Infrastructure;

public sealed partial class PlatformRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly FileStream _processLock;
    public SqliteStateStore Store { get; }
    public PlatformOptions Options { get; }
    public AuthService Auth { get; } = new();
    public ProjectIdeaService Ideas { get; } = new();
    public TeamService Teams { get; } = new();
    public TrackingService Tracking { get; } = new();
    public List<ProjectSubmission> Submissions { get; private set; } = new();
    public List<PlatformNotification> Notifications { get; private set; } = new();
    public List<MailRecord> Mail { get; private set; } = new();
    public List<AuditRecord> Audit { get; private set; } = new();
    public string UploadDirectory => Path.Combine(Store.DataDirectory,"uploads");

    public PlatformRuntime(PlatformOptions options, string contentRoot, string adminPassword = null)
    {
        Options = options;
        if (options.MaxTeamMembers < 2 || options.MaxTeamMembers > 10 || options.MaxSupervisorTeams < 1 || options.MaxSupervisorTeams > 20 || options.MaxUploadMegabytes < 1 || options.MaxUploadMegabytes > 50)
            throw new InvalidOperationException("Invalid platform limits.");
        var directory = Path.GetFullPath(Path.Combine(contentRoot, options.DataDirectory));
        Directory.CreateDirectory(directory);
        try { _processLock = new FileStream(Path.Combine(directory,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("This database is already used by another running platform server.",ex); }
        try
        {
            Store = new SqliteStateStore(directory);
            Directory.CreateDirectory(UploadDirectory);
            if (Store.IsInitialized) Restore(Store.Load());
            else
            {
                if (!options.DemoData && (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12))
                    throw new InvalidOperationException("Set Bootstrap__AdminPassword (at least 12 characters) before the first non-demo start.");
                Seed(adminPassword ?? "AdminPass123!");
                Store.Save(Capture());
            }
        }
        catch { _processLock.Dispose(); throw; }
    }

    public T Read<T>(Func<PlatformRuntime,T> action)
    {
        lock (_gate) return PlatformJson.Copy(action(this));
    }

    public string ReadJson(Func<PlatformRuntime,object> action)
    {
        lock (_gate) return PlatformJson.Serialize(new { isSuccess=true, data=action(this) });
    }

    public string Mutate(string actorId, string actionName, string entityId, Func<PlatformRuntime,object> action, string message="تم حفظ التغييرات")
    {
        lock (_gate)
        {
            string before = PlatformJson.Serialize(Capture());
            try
            {
                object data = action(this);
                Audit.Add(new AuditRecord { ActorId=actorId,Action=actionName,EntityId=entityId });
                string response = PlatformJson.Serialize(new { isSuccess=true, message, data });
                Store.Save(Capture());
                return response;
            }
            catch { Restore(PlatformJson.Deserialize<PlatformSnapshot>(before)); throw; }
        }
    }

    public User Authenticate(string identifier, string password)
    {
        lock (_gate)
        {
            var result=Auth.Login(identifier,password);
            if (!result.IsSuccess) throw new PlatformException("البريد أو اسم المستخدم أو كلمة المرور غير صحيحة",401,"invalid_credentials");
            return PlatformJson.Copy(result.Data);
        }
    }

    public PlatformSnapshot Capture() => new()
    {
        Users=Auth.ExportUsers(), Ideas=Ideas.ExportIdeas(), Archive=Ideas.ExportArchive(), Reviews=Ideas.ExportReviews(),
        Teams=Teams.GetAllTeams(), Invitations=Teams.ExportInvitations(), Supervisors=Teams.GetAllSupervisors(), SupervisorAssignments=Teams.ExportAssignments(),
        Milestones=Tracking.ExportMilestones(), Tasks=Tracking.ExportTasks(), Submissions=Submissions, Notifications=Notifications, Mail=Mail, Audit=Audit
    };

    private void Restore(PlatformSnapshot state)
    {
        Auth.ImportUsers(state.Users); Ideas.ImportState(state.Ideas,state.Archive,state.Reviews);
        Teams.ImportState(state.Teams,state.Invitations,state.Supervisors,state.SupervisorAssignments);
        Tracking.ImportState(state.Milestones,state.Tasks);
        Submissions=state.Submissions; Notifications=state.Notifications; Mail=state.Mail; Audit=state.Audit;
    }

    private void Seed(string adminPassword)
    {
        Require(Auth.Register("admin","admin@graduation.local",adminPassword,UserRoleType.Admin,"إدارة الكلية","إدارة الكلية","ADM-001"));
        if (!Options.DemoData)
        {
            Ideas.ImportState(Array.Empty<ProjectIdea>(),Array.Empty<ProjectArchive>(),Array.Empty<IdeaReview>());
            return;
        }
        var doctor=Require(Auth.Register("dr_mostafa","mostafa@graduation.local","DocPass123!",UserRoleType.Supervisor,"د. مصطفى كامل","علوم الحاسب","FAC-014"));
        var rania=Require(Auth.Register("dr_rania","rania@graduation.local","DocPass123!",UserRoleType.Supervisor,"د. رانيا محمود","نظم المعلومات","FAC-015"));
        var supervisor=Require(Teams.RegisterSupervisor(doctor.Id,doctor.Profile.FullName,"أستاذ مساعد",doctor.Profile.Department,"الأنظمة الذكية وإنترنت الأشياء",Options.MaxSupervisorTeams));
        Require(Teams.RegisterSupervisor(rania.Id,rania.Profile.FullName,"أستاذ مساعد",rania.Profile.Department,"نظم المعلومات الصحية",Options.MaxSupervisorTeams));
        var ahmed=Require(Auth.Register("ahmed","ahmed@graduation.local","AhmedPass123!",UserRoleType.Student,"أحمد منصور","علوم الحاسب","ST-101"));
        var karim=Require(Auth.Register("karim","karim@graduation.local","StudentPass123!",UserRoleType.Student,"كريم نبيل","علوم الحاسب","ST-102"));
        var mona=Require(Auth.Register("mona","mona@graduation.local","StudentPass123!",UserRoleType.Student,"منى علي","علوم الحاسب","ST-103"));
        var team=Require(Teams.CreateTeam(ahmed.Id,ahmed.Profile.FullName,"رواد الطاقة الذكية",Options.MaxTeamMembers));
        foreach (var member in new[] {karim,mona})
        {
            var invitation=Require(Teams.SendInvitation(ahmed.Id,team.Id,member.Id));
            Require(Teams.RespondToInvitation(invitation.Id,member.Id,member.Profile.FullName,true));
        }
        var admin=Auth.GetUsersByRole(UserRoleType.Admin).Single();
        foreach (var user in new[] { doctor, rania })
        {
            user.SupervisorPermissionIds = SupervisorPermissions.Catalog.Select(p => p.Id).ToList();
            user.SupervisorPermissionsUpdatedBy = admin.Id;
            user.SupervisorPermissionsUpdatedAt = DateTime.UtcNow;
        }
        Require(Teams.AssignSupervisorToTeam(admin.Id,supervisor.Id,team.Id));
        var idea=Require(Ideas.SubmitIdea(ahmed.Id,"Smart IoT Microgrid Energy Optimization for Campus","Deploying LoRaWAN power sensors and predictive models to dynamically balance campus energy loads.","ترشيد الطاقة داخل الحرم الجامعي",new List<string>{"Python","IoT","PyTorch","LoRaWAN"},"IoT & Smart Systems",team.Id));
        Require(Ideas.ReviewIdea(doctor.Id,idea.Id,IdeaStatus.Approved,"فكرة معتمدة؛ يرجى بدء خطة التنفيذ وتسليمات المراحل."));
        Require(Teams.LinkProjectIdea(team.Id,idea.Id));
        var milestones=Require(Tracking.InitializeDefaultMilestones(team.Id));
        var titles=new[] {"كتابة وثيقة المتطلبات SRS","مراجعة الدراسات السابقة","تصميم دائرة الحساسات","تدريب نموذج توقع الاستهلاك"};
        for(int i=0;i<titles.Length;i++)
        {
            var task=Require(Tracking.CreateTask(team.Id,milestones[i/2].Id,titles[i],"مهمة من خطة الفريق التجريبية",TaskPriority.High,DateTime.UtcNow,DateTime.UtcNow.AddDays(7+i*5)));
            Require(Tracking.AssignTaskToMember(task.Id,new[]{ahmed,ahmed,karim,mona}[i].Id,new[]{ahmed,ahmed,karim,mona}[i].Profile.FullName));
            if(i<2) Require(Tracking.UpdateTaskStatus(task.Id,TaskItemStatus.Completed));
            else if(i==2) Require(Tracking.UpdateTaskStatus(task.Id,TaskItemStatus.InProgress));
        }
    }

    public User Actor(string id)
    {
        var user=Auth.GetUserById(id);
        if(user==null || !user.IsActive) throw new PlatformException("يرجى تسجيل الدخول",401,"unauthenticated");
        return user;
    }

    public Team AccessTeam(User actor,string id,bool edit=false)
    {
        id=GraduationPlatform.Backend.ApiControllers.Input.Text(id,"معرف الفريق",64);
        var team=Teams.GetTeamById(id);
        if(team==null) throw new PlatformException("الفريق غير موجود",404,"not_found");
        bool allowed=actor.Role==UserRoleType.Admin || team.Members.Any(m=>m.StudentId==actor.Id)
            || (actor.Role==UserRoleType.Supervisor && Teams.GetSupervisor(team.AssignedSupervisorId ?? "")?.UserId==actor.Id);
        if(!allowed) throw new PlatformException("ليس لديك صلاحية على هذا الفريق",403,"forbidden");
        if(actor.Role==UserRoleType.Supervisor) RequireSupervisorPermission(actor,SupervisorPermissions.ViewTeams);
        if(edit && team.IsArchived) throw new PlatformException("المشروع مكتمل ومؤرشف؛ لا يمكن تعديل بياناته",409,"archived");
        return team;
    }

    public Team LeadTeam(User actor,string id)
    {
        var team=AccessTeam(actor,id,true);
        if(actor.Role!=UserRoleType.Admin && (actor.Role!=UserRoleType.Student || team.LeaderStudentId!=actor.Id))
            throw new PlatformException("هذا الإجراء متاح لقائد الفريق والإدارة",403,"forbidden");
        return team;
    }

    public Team ReviewTeam(User actor,string id,bool edit=true)
    {
        var team=AccessTeam(actor,id,edit);
        if(actor.Role==UserRoleType.Student) throw new PlatformException("هذا الإجراء متاح للمشرف والإدارة",403,"forbidden");
        return team;
    }

    public List<Team> VisibleTeams(User actor)
    {
        return Teams.GetAllTeams().Where(t=>actor.Role==UserRoleType.Admin || t.Members.Any(m=>m.StudentId==actor.Id)
            || (actor.Role==UserRoleType.Supervisor && SupervisorPermissions.Allows(actor,SupervisorPermissions.ViewTeams) && Teams.GetSupervisor(t.AssignedSupervisorId ?? "")?.UserId==actor.Id)).ToList();
    }

    public static object UserView(User user) => user==null ? null : user.Role==UserRoleType.Supervisor ? new
    { user.Id,user.Username,user.Email,user.Role,user.IsActive,user.CreatedAt,profile=user.Profile,permissions=user.SupervisorPermissionIds??new() } : (object)new
    { user.Id,user.Username,user.Email,user.Role,user.IsActive,user.CreatedAt,profile=user.Profile };

    public object TeamView(Team team) => new
    {
        team.Id,team.Name,team.LeaderStudentId,team.ProjectIdeaId,team.AssignedSupervisorId,team.MaxMembers,team.IsArchived,team.CompletedAt,team.CreatedAt,
        grade=Ideas.ExportArchive().FirstOrDefault(a=>a.TeamId==team.Id)?.FinalGrade,
        members=team.Members.Select(m=>new {m.Id,m.StudentId,m.MemberName,m.RoleInTeam,m.JoinedAt,isActive=Auth.GetUserById(m.StudentId)?.IsActive ?? false}),
        supervisor=Teams.GetSupervisor(team.AssignedSupervisorId ?? ""),
        idea=team.ProjectIdeaId==null ? null : Ideas.GetIdeaById(team.ProjectIdeaId),
        timeline=Tracking.GetProjectTimeline(team.Id)
    };

    public object TeamDetails(User actor,string id)
    {
        var team=AccessTeam(actor,id);
        return new {team=TeamView(team),milestones=Tracking.GetMilestones(id),tasks=Tracking.GetTasksByTeam(id),
            ideas=Ideas.ExportIdeas().Where(i=>i.TeamId==id).Select(i=>IdeaView(i)),
            invitations=Teams.ExportInvitations().Where(i=>i.TeamId==id).Select(i=>InvitationView(i)),
            submissions=Submissions.Where(s=>s.TeamId==id).Select(s=>SubmissionView(s))};
    }

    public object IdeaView(ProjectIdea idea) => new {idea.Id,idea.Title,idea.Description,idea.Objectives,idea.Technologies,idea.Track,idea.TeamId,idea.SubmitterStudentId,idea.Status,idea.CreatedAt,idea.UpdatedAt,
        teamName=Teams.GetTeamById(idea.TeamId ?? "")?.Name,reviews=Ideas.GetReviewsForIdea(idea.Id).Select(r=>new {r.Id,r.Decision,r.FeedbackComments,r.ReviewedAt,reviewerName=Auth.GetUserById(r.SupervisorId)?.Profile.FullName})};

    public object InvitationView(TeamInvitation invitation) => new {invitation.Id,invitation.TeamId,invitation.InviteeStudentId,invitation.Status,invitation.ProposedRole,invitation.SentAt,invitation.RespondedAt,
        teamName=Teams.GetTeamById(invitation.TeamId)?.Name,studentName=Auth.GetUserById(invitation.InviteeStudentId)?.Profile.FullName,studentEmail=Auth.GetUserById(invitation.InviteeStudentId)?.Email};

    public object SubmissionView(ProjectSubmission submission) => new {submission.Id,submission.TeamId,submission.MilestoneId,submission.Title,submission.OriginalFileName,submission.SizeBytes,submission.ContentType,submission.CreatedAt,submission.Decision,submission.Feedback,submission.Grade,
        uploadedByName=Auth.GetUserById(submission.UploadedBy)?.Profile.FullName,reviews=submission.Reviews.Select(r=>new {r.Decision,r.Feedback,r.Grade,r.CreatedAt,reviewerName=Auth.GetUserById(r.ReviewerId)?.Profile.FullName})};

    public void Notify(string userId,string title,string message,string kind,string teamId=null,string dedupKey=null)
    {
        var user=Auth.GetUserById(userId);
        if(user==null || !user.IsActive || (dedupKey!=null && Notifications.Any(n=>n.DedupKey==dedupKey))) return;
        Notifications.Add(new PlatformNotification{UserId=userId,Title=title,Message=message,Kind=kind,TeamId=teamId,DedupKey=dedupKey});
        QueueMail(user.Email,title,message+"\n\n"+Options.PublicUrl.TrimEnd('/')+"/");
    }

    public void NotifyTeam(Team team,string title,string message,string kind)
    {
        foreach(var member in team.Members) Notify(member.StudentId,title,message,kind,team.Id);
    }

    public void QueueMail(string address,string subject,string body) => Mail.Add(new MailRecord{To=address,Subject=subject,Body=body});

    public static void Require(Result result)
    {
        if(!result.IsSuccess) throw new PlatformException(result.Message);
    }
    public static T Require<T>(Result<T> result) { Require((Result)result); return result.Data; }

    public string CreateBackup()
    {
        lock(_gate)
        {
            string db=Store.CreateBackup(); string zip=Path.ChangeExtension(db,".zip");
            try
            {
                using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(db,"graduation.db",CompressionLevel.Optimal);
                    foreach(var file in Directory.EnumerateFiles(UploadDirectory,"*.bin")) archive.CreateEntryFromFile(file,"uploads/"+Path.GetFileName(file),CompressionLevel.Optimal);
                    var keys=Path.Combine(Store.DataDirectory,"keys");
                    if(Directory.Exists(keys)) foreach(var file in Directory.EnumerateFiles(keys,"*.xml")) archive.CreateEntryFromFile(file,"keys/"+Path.GetFileName(file),CompressionLevel.Optimal);
                }
                return zip;
            }
            catch { if(File.Exists(zip)) File.Delete(zip); throw; }
            finally { File.Delete(db); }
        }
    }

    public void Dispose() => _processLock.Dispose();
}
