using GraduationPlatform.Infrastructure;
using GraduationPlatform.SupervisorManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class SupervisorApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        var admin = secured.MapGroup("/admin").RequireAuthorization("Admin");
        admin.MapGet("/supervisor-permissions", (HttpContext c, PlatformRuntime r) => Query(c, r, (rt, u) =>
        {
            PlatformRuntime.RequireCollegeAdmin(u);
            return SupervisorPermissions.Catalog;
        }));
        admin.MapGet("/supervisors/{userId}/permissions", (string userId, HttpContext c, PlatformRuntime r) =>
            Query(c, r, (rt, u) => rt.SupervisorAccess(u, userId)));
        admin.MapPut("/supervisors/{userId}/permissions", (string userId, SupervisorPermissionsRequest request, HttpContext c, PlatformRuntime r) =>
            Change(c, r, "supervisor.permissions.update", userId, (rt, u) => rt.SetSupervisorPermissions(u, userId, request), "تم تحديث صلاحيات المشرف"));
    }
}
