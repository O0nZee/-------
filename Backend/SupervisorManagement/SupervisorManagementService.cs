using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.SupervisorManagement;

namespace GraduationPlatform.Infrastructure;

public sealed partial class PlatformRuntime
{
    public static void RequireSupervisorPermission(User actor, string permissionId)
    {
        if (!SupervisorPermissions.Allows(actor, permissionId))
            throw new PlatformException("لم تمنحك إدارة الكلية صلاحية تنفيذ هذا الإجراء", 403, "forbidden");
    }

    public object SupervisorAccess(User actor, string userId)
    {
        RequireCollegeAdmin(actor);
        var user = Auth.GetUserById(userId) ?? throw new PlatformException("المشرف غير موجود", 404, "not_found");
        if (user.Role != UserRoleType.Supervisor)
            throw new PlatformException("الحساب ليس حساب مشرف");
        return new
        {
            userId = user.Id,
            permissionIds = user.SupervisorPermissionIds ?? new(),
            updatedBy = user.SupervisorPermissionsUpdatedBy,
            updatedByName = Auth.GetUserById(user.SupervisorPermissionsUpdatedBy ?? "")?.Profile.FullName,
            updatedAt = user.SupervisorPermissionsUpdatedAt
        };
    }

    public object SetSupervisorPermissions(User actor, string userId, SupervisorPermissionsRequest request)
    {
        RequireCollegeAdmin(actor);
        var user = Auth.GetUserById(userId) ?? throw new PlatformException("المشرف غير موجود", 404, "not_found");
        if (user.Role != UserRoleType.Supervisor)
            throw new PlatformException("تحديد صلاحيات الإشراف متاح لحسابات المشرفين فقط");
        if (request.PermissionIds == null || request.PermissionIds.Count > SupervisorPermissions.Catalog.Count)
            throw new PlatformException("أرسل قائمة الصلاحيات؛ القائمة الفارغة تسحب جميع الصلاحيات");
        var ids = request.PermissionIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => !SupervisorPermissions.Catalog.Any(p => p.Id == id)))
            throw new PlatformException("توجد صلاحية غير مسموح بتفويضها للمشرف");
        if (ids.Count > 0 && !ids.Contains(SupervisorPermissions.ViewTeams))
            throw new PlatformException("صلاحيات الإشراف تحتاج صلاحية متابعة الفرق المسندة");
        var previous = user.SupervisorPermissionIds?.ToArray() ?? Array.Empty<string>();
        user.SupervisorPermissionIds = SupervisorPermissions.Catalog.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToList();
        user.SupervisorPermissionsUpdatedBy = actor.Id;
        user.SupervisorPermissionsUpdatedAt = DateTime.UtcNow;
        Audit.Add(new AuditRecord
        {
            ActorId = actor.Id, Action = "supervisor.permissions.details", EntityId = user.Id,
            Details = PlatformJson.Serialize(new { before = previous, after = user.SupervisorPermissionIds })
        });
        return SupervisorAccess(actor, user.Id);
    }
}
