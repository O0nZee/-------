using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.SupervisorManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class TeamApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/teams",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.VisibleTeams(u).Select(t=>rt.TeamView(t))));
        secured.MapGet("/teams/{id}",(string id,HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.TeamDetails(u,id)));
        secured.MapPost("/teams",(TeamRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"team.create",null,(rt,u)=>rt.CreateTeam(u,request),"تم إنشاء الفريق ومراحل المشروع"));
        secured.MapPatch("/teams/{id}",(string id,TeamRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"team.update",id,(rt,u)=>
        {
            var team=rt.LeadTeam(u,id);
            if(request.LeaderStudentId!=null && request.LeaderStudentId!=team.LeaderStudentId)
            {
                var leader=rt.Auth.GetUserById(request.LeaderStudentId);
                if(leader==null || !leader.IsActive || leader.Role!=UserRoleType.Student)
                    throw new PlatformException("يجب أن يكون قائد الفريق طالبًا بحساب نشط");
            }
            PlatformRuntime.Require(rt.Teams.UpdateTeam(id,Input.Text(request.Name,"اسم الفريق",100),request.LeaderStudentId));return rt.TeamView(team);
        }));
        secured.MapDelete("/teams/{id}/members/{studentId}",(string id,string studentId,HttpContext c,PlatformRuntime r)=>Change(c,r,"team.member.remove",id,(rt,u)=>
        {
            var team=rt.AccessTeam(u,id,true);if(u.Id!=studentId)rt.LeadTeam(u,id);
            PlatformRuntime.Require(rt.Teams.RemoveMember(id,studentId));rt.Tracking.RemoveStudentAssignments(id,studentId);
            rt.Notify(studentId,"تحديث عضوية الفريق",$"انتهت عضويتك بفريق «{team.Name}».","MemberRemoved",id);return rt.TeamView(team);
        }));
        secured.MapPost("/teams/{id}/invitations",(string id,InvitationRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"invitation.send",id,(rt,u)=>rt.Invite(u,id,request),"تم إرسال الدعوة"));
        secured.MapGet("/invitations",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Teams.ExportInvitations().Where(i=>i.InviteeStudentId==u.Id || rt.Teams.GetTeamById(i.TeamId)?.LeaderStudentId==u.Id).OrderByDescending(i=>i.SentAt).Select(i=>rt.InvitationView(i))));
        secured.MapPost("/invitations/{id}/respond",(string id,InvitationResponseRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"invitation.respond",id,(rt,u)=>rt.RespondInvitation(u,id,request.Accept??throw new PlatformException("يرجى اختيار قبول الدعوة أو رفضها")),"تم حفظ الرد على الدعوة"));
        secured.MapPost("/invitations/{id}/cancel",(string id,HttpContext c,PlatformRuntime r)=>Change(c,r,"invitation.cancel",id,(rt,u)=>
        {
            var invitation=rt.Teams.GetInvitation(id)??throw new PlatformException("الدعوة غير موجودة",404,"not_found");rt.LeadTeam(u,invitation.TeamId);PlatformRuntime.Require(rt.Teams.CancelInvitation(id));return rt.InvitationView(invitation);
        }));
        secured.MapGet("/supervisors",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Teams.GetAllSupervisors().Where(s=>rt.Auth.GetUserById(s.UserId)?.Role==UserRoleType.Supervisor).Select(s=>new{s.Id,s.UserId,s.Name,s.Department,s.Specialization,s.MaxSupervisedTeams,s.ActiveTeamsCount,isActive=rt.Auth.GetUserById(s.UserId)?.IsActive??false})));
        secured.MapPost("/teams/{id}/supervisor",(string id,AssignmentRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"supervisor.assign",id,(rt,u)=>rt.AssignSupervisor(u,id,request.SupervisorId)));
        secured.MapDelete("/teams/{id}/supervisor",(string id,HttpContext c,PlatformRuntime r)=>Change(c,r,"supervisor.unassign",id,(rt,u)=>
        {
            if(u.Role!=UserRoleType.Admin)throw new PlatformException("متاح للإدارة",403,"forbidden");rt.AccessTeam(u,id,true);PlatformRuntime.Require(rt.Teams.UnassignSupervisor(id));return rt.TeamView(rt.Teams.GetTeamById(id));
        }));
        secured.MapPatch("/supervisors/{id}",(string id,SupervisorRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"supervisor.update",id,(rt,u)=>
        {
            if(u.Role!=UserRoleType.Admin)throw new PlatformException("متاح للإدارة",403,"forbidden");PlatformRuntime.Require(rt.Teams.UpdateSupervisor(id,request.MaxTeams,Input.Text(request.Specialization,"التخصص",200,false)));return rt.Teams.GetSupervisor(id);
        }));
        secured.MapPost("/teams/{id}/archive",(string id,ArchiveRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"project.archive",id,(rt,u)=>rt.ArchiveTeam(u,id,request),"تم تقييم المشروع وأرشفته"));
        secured.MapPost("/teams/{id}/reminder",(string id,ReminderRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"team.reminder",id,(rt,u)=>
        {
            PlatformRuntime.RequireSupervisorPermission(u,SupervisorPermissions.SendReminders);
            var team=rt.ReviewTeam(u,id);rt.NotifyTeam(team,"رسالة من المشرف",Input.Text(request.Message,"الرسالة",3000),"Reminder");return null;
        },"تم إخطار أعضاء الفريق"));
    }
}
