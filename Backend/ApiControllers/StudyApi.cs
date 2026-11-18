using GraduationPlatform.Infrastructure;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class StudyApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/study/library", (int? studyYear, HttpContext c, PlatformRuntime r) =>
            Query(c, r, (rt, u) => rt.StudyLibrary(u, c.Request.Query["departmentId"].ToString(), studyYear)));
        var admin = secured.MapGroup("/admin/study").RequireAuthorization("Admin");
        admin.MapPost("/courses", (StudyCourseRequest request, HttpContext c, PlatformRuntime r) =>
            Change(c, r, "study.course.create", null, (rt, u) => rt.SaveStudyCourse(u, request), "تم إضافة المادة"));
        admin.MapPut("/courses/{id}", (string id, StudyCourseRequest request, HttpContext c, PlatformRuntime r) =>
            Change(c, r, "study.course.update", id, (rt, u) => rt.SaveStudyCourse(u, request, id), "تم تحديث المادة"));
        admin.MapDelete("/courses/{id}", (string id, HttpContext c, PlatformRuntime r) => Change(c, r, "study.course.delete", id, (rt, u) =>
        {
            PlatformRuntime.RequireCollegeAdmin(u);
            var course = rt.AccessStudyCourse(u, id);
            rt.StudyResources.RemoveAll(s => s.CourseId == id); rt.StudyCourses.Remove(course);
            return null;
        }, "تم حذف المادة ومحتواها"));
        admin.MapPost("/resources", async (HttpContext c, FileSubmissionService files) =>
        {
            if (!c.Request.HasFormContentType) throw new PlatformException("يرجى اختيار ملف المادة");
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            return Json(await files.SaveStudyResourceAsync(ActorId(c), form["courseId"], form["kind"], form["title"],
                form["examYear"], form.Files.GetFile("file"), c.RequestAborted));
        });
        admin.MapDelete("/resources/{id}", (string id, HttpContext c, PlatformRuntime r) => Change(c, r, "study.resource.delete", id, (rt, u) =>
        {
            PlatformRuntime.RequireCollegeAdmin(u);
            var item = rt.StudyResources.FirstOrDefault(s => s.Id == id) ?? throw new PlatformException("الملف غير موجود", 404, "not_found");
            rt.StudyResources.Remove(item); return null;
        }, "تم حذف الملف من المادة"));
        secured.MapGet("/study/resources/{id}/download", (string id, HttpContext c, PlatformRuntime r) =>
        {
            var item = r.Read(rt =>
            {
                var resource = rt.StudyResources.FirstOrDefault(s => s.Id == id) ?? throw new PlatformException("الملف غير موجود", 404, "not_found");
                rt.AccessStudyCourse(rt.Actor(ActorId(c)), resource.CourseId); return resource;
            });
            if (!Guid.TryParseExact(item.Id, "N", out _)) throw new PlatformException("معرف الملف غير صالح");
            string path = Path.Combine(r.UploadDirectory, item.Id + ".bin");
            if (!File.Exists(path)) throw new PlatformException("الملف غير موجود؛ راجع النسخة الاحتياطية", 404, "file_missing");
            return Results.File(path, item.ContentType, item.OriginalFileName);
        });
    }
}
