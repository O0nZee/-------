using GraduationPlatform.Member1_UserManagement;

namespace GraduationPlatform.SupervisorManagement;

// Only these academic capabilities can be delegated by college administration.
public static class SupervisorPermissions
{
    public const string ViewTeams = "team.view";
    public const string ReviewIdeas = "idea.review";
    public const string ReviewSubmissions = "submission.review";
    public const string DownloadSubmissions = "submission.download";
    public const string SendReminders = "team.reminder";
    public const string ArchiveProjects = "project.archive";
    public const string ViewReports = "report.view";

    public static IReadOnlyList<Permission> Catalog => new[]
    {
        new Permission(ViewTeams, "متابعة الفرق المسندة", "Supervision", "عرض أعضاء الفرق وأفكارها ومهامها وتسليماتها"),
        new Permission(ReviewIdeas, "مراجعة الأفكار", "Supervision", "اعتماد الأفكار أو رفضها أو طلب تعديلها"),
        new Permission(ReviewSubmissions, "تقييم التسليمات", "Supervision", "مراجعة ملفات المراحل وتسجيل الدرجات"),
        new Permission(DownloadSubmissions, "تنزيل ملفات التسليم", "Supervision", "تنزيل ملفات الفرق المسندة"),
        new Permission(SendReminders, "إرسال رسائل للفريق", "Supervision", "إخطار أعضاء الفرق المسندة"),
        new Permission(ArchiveProjects, "التقييم النهائي والأرشفة", "Supervision", "اعتماد الدرجة النهائية وأرشفة المشروع المكتمل"),
        new Permission(ViewReports, "عرض التقارير وتنزيلها", "Supervision", "تقارير الفرق المسندة للمشرف فقط")
    };

    public static bool Allows(User user, string permissionId) => user.IsActive &&
        (user.Role == UserRoleType.Admin || user.Role == UserRoleType.Supervisor &&
         Catalog.Any(p => p.Id == permissionId) &&
         user.SupervisorPermissionIds?.Contains(ViewTeams) == true &&
         user.SupervisorPermissionIds.Contains(permissionId));
}
