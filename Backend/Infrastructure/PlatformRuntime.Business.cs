using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;
using System.Globalization;
using System.Text.RegularExpressions;
using GraduationPlatform.SupervisorManagement;

namespace GraduationPlatform.Infrastructure;

public sealed partial class PlatformRuntime
{
    public User RegisterUser(RegisterRequest request,UserRoleType role)
    {
        var user=Require(Auth.Register(Input.Username(request.Username),Input.Text(request.Email,"البريد الإلكتروني",254),Input.Password(request.Password),role,
            Input.Text(request.FullName,"الاسم الكامل",100),Input.Text(request.Department,"القسم",100),Input.Text(request.AcademicId,"الرقم الأكاديمي",50,false)));
        if(role==UserRoleType.Supervisor) Require(Teams.RegisterSupervisor(user.Id,user.Profile.FullName,"د.",user.Profile.Department,"",Options.MaxSupervisorTeams));
        Notify(user.Id,"مرحبًا بك في المنصة","تم إنشاء حسابك بنجاح. يمكنك متابعة مشروع التخرج من حسابك.","Welcome");
        return user;
    }

    public void SynchronizeProfile(User user)
    {
        foreach(var member in Teams.GetAllTeams().SelectMany(t=>t.Members).Where(m=>m.StudentId==user.Id))
            member.MemberName=user.Profile.FullName;
        foreach(var assignment in Tracking.ExportTasks().SelectMany(t=>t.Assignments).Where(a=>a.AssignedStudentId==user.Id))
            assignment.StudentName=user.Profile.FullName;
        var supervisor=Teams.GetSupervisorByUser(user.Id);
        if(supervisor!=null){supervisor.Name=user.Profile.FullName;supervisor.Department=user.Profile.Department;}
    }

    public object CreateTeam(User actor,TeamRequest request)
    {
        if(actor.Role!=UserRoleType.Student) throw new PlatformException("إنشاء الفرق متاح للطلاب",403,"forbidden");
        int size=request.MaxMembers??Options.MaxTeamMembers;
        if(size<2 || size>Options.MaxTeamMembers) throw new PlatformException($"سعة الفريق يجب أن تكون بين ٢ و{Options.MaxTeamMembers}");
        var team=Require(Teams.CreateTeam(actor.Id,actor.Profile.FullName,Input.Text(request.Name,"اسم الفريق",100),size));
        Require(Tracking.InitializeDefaultMilestones(team.Id));
        return TeamView(team);
    }

    public object Invite(User actor,string teamId,InvitationRequest request)
    {
        var team=LeadTeam(actor,teamId);
        var email=Input.Text(request.Email,"بريد الطالب",254);
        var student=Auth.ExportUsers().FirstOrDefault(u=>u.Email.Equals(email,StringComparison.OrdinalIgnoreCase) && u.Role==UserRoleType.Student && u.IsActive);
        if(student==null) throw new PlatformException("لم يتم العثور على حساب طالب نشط بهذا البريد",404,"not_found");
        var invitation=Require(Teams.SendInvitation(team.LeaderStudentId,team.Id,student.Id,Input.Text(request.ProposedRole??"Member","الدور بالفريق",80)));
        Notify(student.Id,"دعوة للانضمام إلى فريق",$"دعاك فريق «{team.Name}» للانضمام بدور {invitation.ProposedRole}.","Invitation",team.Id);
        return InvitationView(invitation);
    }

    public object RespondInvitation(User actor,string invitationId,bool accept)
    {
        if(actor.Role!=UserRoleType.Student) throw new PlatformException("الدعوات متاحة للطلاب",403,"forbidden");
        var invitation=Teams.GetInvitation(invitationId)??throw new PlatformException("الدعوة غير موجودة",404,"not_found");
        if(invitation.InviteeStudentId!=actor.Id) throw new PlatformException("الدعوة تخص طالبًا آخر",403,"forbidden");
        Require(Teams.RespondToInvitation(invitationId,actor.Id,actor.Profile.FullName,accept));
        var team=Teams.GetTeamById(invitation.TeamId);
        Notify(team.LeaderStudentId,"رد على دعوة الفريق",$"{actor.Profile.FullName} {(accept?"قبل":"رفض")} دعوة فريق «{team.Name}».","InvitationResponse",team.Id);
        return InvitationView(invitation);
    }

    public object AssignSupervisor(User actor,string teamId,string supervisorId)
    {
        if(actor.Role!=UserRoleType.Admin) throw new PlatformException("إسناد المشرفين متاح للإدارة",403,"forbidden");
        var team=AccessTeam(actor,teamId,true);
        var supervisor=Teams.GetSupervisor(supervisorId??"")??throw new PlatformException("المشرف غير موجود",404,"not_found");
        if(Auth.GetUserById(supervisor.UserId)?.IsActive!=true || Auth.GetUserById(supervisor.UserId)?.Role!=UserRoleType.Supervisor)
            throw new PlatformException("حساب المشرف غير نشط أو لا يملك دور المشرف");
        string previous=team.AssignedSupervisorId;
        Require(Teams.AssignSupervisorToTeam(actor.Id,supervisor.Id,team.Id));
        if(previous!=supervisor.Id)
        {
            NotifyTeam(team,"إسناد المشرف الأكاديمي",$"تم إسناد {supervisor.Name} لفريق «{team.Name}».","SupervisorAssigned");
            Notify(supervisor.UserId,"فريق جديد تحت إشرافك",$"تم إسناد فريق «{team.Name}» إليك.","SupervisorAssigned",team.Id);
            var old=Teams.GetSupervisor(previous??"");
            if(old!=null) Notify(old.UserId,"تحديث الإشراف",$"تم نقل الإشراف على فريق «{team.Name}» إلى {supervisor.Name}.","SupervisorChanged",team.Id);
        }
        return TeamView(team);
    }

    public object SubmitIdea(User actor,IdeaRequest request,string existingId=null)
    {
        var team=LeadTeam(actor,request.TeamId);
        if(actor.Role!=UserRoleType.Student) throw new PlatformException("تقديم الفكرة متاح لقائد الفريق من الطلاب",403,"forbidden");
        string title=Input.Text(request.Title,"عنوان المشروع",200);
        string description=Input.Text(request.Description,"وصف المشروع",10000);
        if(description.Length<20) throw new PlatformException("وصف المشروع يجب أن يكون ٢٠ حرفًا على الأقل");
        string objectives=Input.Text(request.Objectives,"أهداف المشروع",10000,false);
        string track=Input.Text(request.Track??"General","المسار",100);
        var technologies=Input.Technologies(request.Technologies);
        ProjectIdea idea;
        if(existingId==null)
        {
            if(team.ProjectIdeaId!=null || Ideas.ExportIdeas().Any(i=>i.TeamId==team.Id && i.Status!=IdeaStatus.Rejected))
                throw new PlatformException("للفريق فكرة قيد المراجعة أو معتمدة بالفعل؛ عدّل الفكرة الحالية أو انتظر قرار المشرف",409,"conflict");
            idea=Require(Ideas.SubmitIdea(actor.Id,title,description,objectives,technologies,track,team.Id));
        }
        else
        {
            var previous=Ideas.GetIdeaById(existingId)??throw new PlatformException("الفكرة غير موجودة",404,"not_found");
            if(previous.TeamId!=team.Id) throw new PlatformException("الفكرة تخص فريقًا آخر",403,"forbidden");
            if((team.ProjectIdeaId!=null && team.ProjectIdeaId!=existingId) || Ideas.ExportIdeas().Any(i=>i.TeamId==team.Id && i.Id!=existingId && i.Status!=IdeaStatus.Rejected))
                throw new PlatformException("للفريق فكرة أخرى قيد المراجعة أو معتمدة",409,"conflict");
            idea=Require(Ideas.UpdateIdea(existingId,title,description,objectives,technologies,track));
        }
        var supervisor=Teams.GetSupervisor(team.AssignedSupervisorId??"");
        if(supervisor!=null) Notify(supervisor.UserId,"فكرة تحتاج مراجعتك",$"قدّم فريق «{team.Name}» فكرة «{idea.Title}» للمراجعة.","IdeaSubmitted",team.Id);
        return IdeaView(idea);
    }

    public object ReviewIdea(User actor,string id,ReviewRequest request)
    {
        RequireSupervisorPermission(actor,SupervisorPermissions.ReviewIdeas);
        var idea=Ideas.GetIdeaById(id)??throw new PlatformException("الفكرة غير موجودة",404,"not_found");
        var team=ReviewTeam(actor,idea.TeamId);
        var decision=Input.EnumValue<IdeaStatus>(request.Decision,"قرار المراجعة");
        if(decision!=IdeaStatus.Approved && decision!=IdeaStatus.Rejected && decision!=IdeaStatus.ModificationRequested) throw new PlatformException("قرار المراجعة غير صالح");
        var feedback=Input.Text(request.Feedback,"ملاحظات المراجعة",5000,decision!=IdeaStatus.Approved);
        if(decision==IdeaStatus.Approved && team.ProjectIdeaId!=null && team.ProjectIdeaId!=idea.Id) throw new PlatformException("للفريق فكرة أخرى معتمدة؛ يجب إعادة فتحها أولًا",409,"conflict");
        Require(Ideas.ReviewIdea(actor.Id,id,decision,feedback));
        if(decision==IdeaStatus.Approved) Require(Teams.LinkProjectIdea(team.Id,idea.Id));
        else if(team.ProjectIdeaId==idea.Id) team.ProjectIdeaId=null;
        string status=decision==IdeaStatus.Approved?"معتمدة":decision==IdeaStatus.Rejected?"مرفوضة":"تحتاج تعديلات";
        NotifyTeam(team,"قرار مراجعة فكرة المشروع",$"فكرة «{idea.Title}» {status}.\n{feedback}","IdeaReviewed");
        return IdeaView(idea);
    }

    private void RequireApproved(Team team)
    {
        if(team.ProjectIdeaId==null || Ideas.GetIdeaById(team.ProjectIdeaId)?.Status!=IdeaStatus.Approved)
            throw new PlatformException("يجب اعتماد فكرة المشروع قبل تنفيذ المهام وتسليم الملفات",409,"idea_not_approved");
    }

    private void StudentTaskAccess(User actor,Team team,TaskItem task=null)
    {
        if(actor.Role==UserRoleType.Supervisor) throw new PlatformException("المشرف يراجع التنفيذ؛ تعديل المهام متاح للفريق والإدارة",403,"forbidden");
        if(task!=null && actor.Role!=UserRoleType.Admin && team.LeaderStudentId!=actor.Id && !task.Assignments.Any(a=>a.AssignedStudentId==actor.Id))
            throw new PlatformException("يمكنك تعديل المهام المسندة إليك فقط",403,"forbidden");
        RequireApproved(team);
    }

    public object SaveTask(User actor,TaskRequest request,string existingId=null)
    {
        var task=existingId==null?null:Tracking.GetTask(existingId)??throw new PlatformException("المهمة غير موجودة",404,"not_found");
        var team=AccessTeam(actor,task?.TeamId??request.TeamId,true);
        StudentTaskAccess(actor,team,task);
        string title=Input.Text(request.Title,"عنوان المهمة",200),description=Input.Text(request.Description,"وصف المهمة",5000,false);
        var priority=Input.EnumValue<TaskPriority>(request.Priority??"Medium","الأولوية");
        DateTime start=Input.UtcDate(request.StartDate??task?.StartDate??DateTime.UtcNow,"بداية المهمة");
        DateTime due=(request.DueDate??task?.DueDate)??throw new PlatformException("يرجى إدخال موعد تسليم المهمة");
        due=Input.UtcDate(due,"موعد تسليم المهمة");
        string milestoneId=string.IsNullOrWhiteSpace(request.MilestoneId)?null:request.MilestoneId;
        if(task==null) task=Require(Tracking.CreateTask(team.Id,milestoneId,title,description,priority,start,due));
        else Require(Tracking.UpdateTask(task.Id,title,description,priority,start,due,milestoneId));
        var assigned=request.AssignedStudentIds;
        if(assigned==null && existingId==null) assigned=actor.Role==UserRoleType.Student?new List<string>{actor.Id}:new List<string>();
        if(assigned!=null)
        {
            assigned=assigned.Distinct().ToList();
            if(assigned.Count>team.MaxMembers) throw new PlatformException("عدد الطلاب المكلفين أكبر من سعة الفريق");
            if(actor.Role!=UserRoleType.Admin && team.LeaderStudentId!=actor.Id && (assigned.Count!=1 || assigned[0]!=actor.Id))
                throw new PlatformException("إسناد المهام للطلاب الآخرين متاح لقائد الفريق",403,"forbidden");
            foreach(var id in assigned)
                if(!team.Members.Any(m=>m.StudentId==id) || Auth.GetUserById(id)?.IsActive!=true) throw new PlatformException("الطالب المكلّف يجب أن يكون عضوًا نشطًا في الفريق");
            var previous=task.Assignments.Select(a=>a.AssignedStudentId).ToHashSet();
            task.Assignments.Clear();
            foreach(var id in assigned)
            {
                Require(Tracking.AssignTaskToMember(task.Id,id,Auth.GetUserById(id).Profile.FullName));
                if(!previous.Contains(id)) Notify(id,"مهمة جديدة مسندة إليك",$"المهمة: {task.Title}\nالفريق: {team.Name}","TaskAssigned",team.Id);
            }
        }
        return task;
    }

    public object ChangeTaskStatus(User actor,string id,string status)
    {
        var task=Tracking.GetTask(id)??throw new PlatformException("المهمة غير موجودة",404,"not_found");
        var team=AccessTeam(actor,task.TeamId,true); StudentTaskAccess(actor,team,task);
        Require(Tracking.UpdateTaskStatus(id,Input.EnumValue<TaskItemStatus>(status,"حالة المهمة")));
        return new {task,timeline=Tracking.GetProjectTimeline(team.Id)};
    }

    public object ReviewSubmission(User actor,string id,ReviewRequest request)
    {
        RequireSupervisorPermission(actor,SupervisorPermissions.ReviewSubmissions);
        var submission=Submissions.FirstOrDefault(s=>s.Id==id)??throw new PlatformException("التسليم غير موجود",404,"not_found");
        var team=ReviewTeam(actor,submission.TeamId);
        var decision=Input.EnumValue<IdeaStatus>(request.Decision,"قرار المراجعة");
        if(decision!=IdeaStatus.Approved && decision!=IdeaStatus.Rejected && decision!=IdeaStatus.ModificationRequested) throw new PlatformException("قرار المراجعة غير صالح");
        if(request.Grade.HasValue && (request.Grade<0 || request.Grade>100)) throw new PlatformException("الدرجة يجب أن تكون من ٠ إلى ١٠٠");
        if(decision==IdeaStatus.Approved && !request.Grade.HasValue) throw new PlatformException("يرجى إدخال درجة التسليم عند اعتماده");
        submission.Decision=decision.ToString(); submission.Feedback=Input.Text(request.Feedback,"الملاحظات",5000,decision!=IdeaStatus.Approved); submission.Grade=request.Grade;
        submission.Reviews.Add(new SubmissionReview{ReviewerId=actor.Id,Decision=submission.Decision,Feedback=submission.Feedback,Grade=submission.Grade});
        NotifyTeam(team,"مراجعة تسليم مرحلة",$"تمت مراجعة «{submission.Title}».\n{submission.Feedback}","SubmissionReviewed");
        return SubmissionView(submission);
    }

    public object ArchiveTeam(User actor,string id,ArchiveRequest request)
    {
        RequireSupervisorPermission(actor,SupervisorPermissions.ArchiveProjects);
        var team=ReviewTeam(actor,id); RequireApproved(team);
        if(!request.Grade.HasValue || request.Grade<0 || request.Grade>100) throw new PlatformException("يرجى إدخال الدرجة النهائية من ٠ إلى ١٠٠");
        var tasks=Tracking.GetTasksByTeam(id);var milestones=Tracking.GetMilestones(id);
        if(tasks.Count==0 || tasks.Any(t=>t.Status!=TaskItemStatus.Completed) || milestones.Any(m=>!m.IsCompleted)) throw new PlatformException("أكمل مهام جميع المراحل قبل الأرشفة",409,"incomplete_project");
        if(milestones.Any(m=>!Submissions.Any(s=>s.MilestoneId==m.Id && s.Decision=="Approved"))) throw new PlatformException("يجب اعتماد تسليم واحد على الأقل لكل مرحلة قبل الأرشفة",409,"missing_submissions");
        string year=Input.AcademicYear(request.AcademicYear);
        var idea=Ideas.GetIdeaById(team.ProjectIdeaId);
        var archived=Require(Ideas.AddArchiveProject(idea.Title,idea.Description,year,Auth.GetUserById(team.LeaderStudentId).Profile.Department,idea.Track,idea.Technologies,Teams.GetSupervisor(team.AssignedSupervisorId??"")?.Name??actor.Profile.FullName,request.Grade.Value.ToString(CultureInfo.InvariantCulture)));
        archived.TeamId=team.Id; Require(Teams.ArchiveTeam(id));
        NotifyTeam(team,"اكتمل مشروع التخرج",$"تم أرشفة مشروع «{idea.Title}» بدرجة {request.Grade}/100 للعام {year}.","ProjectArchived");
        return archived;
    }

    public object Dashboard(User actor)
    {
        var teams=VisibleTeams(actor);var ids=teams.Select(t=>t.Id).ToHashSet();
        var ideas=Ideas.ExportIdeas().Where(i=>ids.Contains(i.TeamId??""));
        var alerts=teams.Where(t=>!t.IsArchived).SelectMany(t=>Tracking.CheckDeadlineAlerts(t.Id)).ToList();
        return new {statistics=new {students=actor.Role==UserRoleType.Admin?Auth.GetUsersByRole(UserRoleType.Student).Count:teams.SelectMany(t=>t.Members).Select(m=>m.StudentId).Distinct().Count(),
            teams=teams.Count,approvedIdeas=ideas.Count(i=>i.Status==IdeaStatus.Approved),pendingIdeas=ideas.Count(i=>i.Status==IdeaStatus.PendingReview),
            supervisors=actor.Role==UserRoleType.Admin?Auth.GetUsersByRole(UserRoleType.Supervisor).Count:teams.Select(t=>t.AssignedSupervisorId).Where(s=>s!=null).Distinct().Count(),completedTeams=teams.Count(t=>t.IsArchived),overdueTasks=alerts.Count(a=>a.IsOverdue)},
            teams=teams.Select(t=>TeamView(t)),alerts,unreadNotifications=Notifications.Count(n=>n.UserId==actor.Id && n.ReadAt==null)};
    }

    public object Reports(User actor)
    {
        RequireSupervisorPermission(actor,SupervisorPermissions.ViewReports);
        var teams=VisibleTeams(actor);
        return new {generatedAt=DateTime.UtcNow,rows=teams.Select(t=>new {teamId=t.Id,teamName=t.Name,projectTitle=Ideas.GetIdeaById(t.ProjectIdeaId??"")?.Title??"لم تعتمد الفكرة بعد",supervisor=Teams.GetSupervisor(t.AssignedSupervisorId??"")?.Name??"لم يسند مشرف",members=t.Members.Count,
            timeline=Tracking.GetProjectTimeline(t.Id),t.IsArchived,grade=Ideas.ExportArchive().FirstOrDefault(a=>a.TeamId==t.Id)?.FinalGrade}),
            workload=Teams.GetAllSupervisors().Where(s=>Auth.GetUserById(s.UserId)?.Role==UserRoleType.Supervisor && (actor.Role==UserRoleType.Admin || s.UserId==actor.Id)),
            similarity=Ideas.ExportIdeas().Where(i=>teams.Any(t=>t.Id==i.TeamId)).Select(i=>
            {
                var work=PrepareSimilarity(actor,new IdeaRequest{Title=i.Title,Description=i.Description,Technologies=i.Technologies,ExcludeIdeaId=i.Id});
                return new {i.Id,i.Title,teamName=Teams.GetTeamById(i.TeamId).Name,matches=TextSimilarityAnalysis.Compare(work.Query,work.Documents)};
            })};
    }
}
