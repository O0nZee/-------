using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Backend.ApiControllers;

namespace GraduationPlatform.Infrastructure;

public sealed partial class PlatformRuntime
{
    public static void RequireCollegeAdmin(User actor)
    {
        if (!actor.IsActive || actor.Role != UserRoleType.Admin)
            throw new PlatformException("هذا الإجراء متاح لإدارة الكلية فقط", 403, "forbidden");
    }

    public object UpdateUser(User actor,string id,UserUpdateRequest request)
    {
        RequireCollegeAdmin(actor);
        var user=Auth.GetUserById(id)??throw new PlatformException("المستخدم غير موجود",404,"not_found");
        var role=request.Role==null?user.Role:Input.EnumValue<UserRoleType>(request.Role,"الدور");
        bool active=request.IsActive??user.IsActive;
        if(id==actor.Id && (!active || role!=UserRoleType.Admin)) throw new PlatformException("لا يمكنك تعطيل حسابك الإداري أو تغيير دوره");
        var team=Teams.GetTeamByStudentId(id);
        var supervisor=Teams.GetSupervisorByUser(id);
        if(role!=user.Role && team!=null) throw new PlatformException("انقل عضوية الطالب من الفريق قبل تغيير دوره");
        if((!active || role!=UserRoleType.Supervisor) && supervisor?.ActiveTeamsCount>0) throw new PlatformException("انقل فرق المشرف قبل تعطيل حسابه أو تغيير دوره");
        if(!active && team?.LeaderStudentId==id && !team.IsArchived) throw new PlatformException("انقل قيادة الفريق قبل تعطيل حساب قائده");
        string department = request.Department == null ? user.Profile.Department :
            role == UserRoleType.Student ? ValidateStudyDepartment(request.Department) : Input.Text(request.Department,"القسم",100);
        int? studyYear = role == UserRoleType.Student ? request.StudyYear ?? user.Profile.StudyYear : null;
        if (studyYear != null) ValidateStudyYear(studyYear);
        if (role == UserRoleType.Student && role != user.Role) { department = ValidateStudyDepartment(department); studyYear = ValidateStudyYear(studyYear); }
        Require(Auth.SetAccountState(id,active,role));
        Require(Auth.UpdateProfile(id,request.FullName==null?null:Input.Text(request.FullName,"الاسم الكامل",100),department,null,null));
        user.Profile.StudyYear = studyYear;
        if(role==UserRoleType.Supervisor && supervisor==null) Require(Teams.RegisterSupervisor(id,user.Profile.FullName,"د.",user.Profile.Department,"",Options.MaxSupervisorTeams));
        SynchronizeProfile(user);
        return UserView(user);
    }

}
