using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.SupervisorManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class TrackingApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/tasks",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>
        {
            string id=Input.Text(c.Request.Query["teamId"],"الفريق",64);rt.AccessTeam(u,id);return rt.Tracking.GetTasksByTeam(id);
        }));
        secured.MapPost("/tasks",(TaskRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"task.create",null,(rt,u)=>rt.SaveTask(u,request),"تم إنشاء المهمة"));
        secured.MapPut("/tasks/{id}",(string id,TaskRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"task.update",id,(rt,u)=>rt.SaveTask(u,request,id)));
        secured.MapPost("/tasks/{id}/status",(string id,TaskStatusRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"task.status",id,(rt,u)=>rt.ChangeTaskStatus(u,id,request.Status)));
        secured.MapDelete("/tasks/{id}",(string id,HttpContext c,PlatformRuntime r)=>Change(c,r,"task.delete",id,(rt,u)=>
        {
            var task=rt.Tracking.GetTask(id)??throw new PlatformException("المهمة غير موجودة",404,"not_found");rt.LeadTeam(u,task.TeamId);PlatformRuntime.Require(rt.Tracking.DeleteTask(id));return null;
        }));
        secured.MapPost("/submissions",async (HttpContext c,FileSubmissionService files)=>
        {
            if(!c.Request.HasFormContentType)throw new PlatformException("يرجى إرسال ملف التسليم");
            var form=await c.Request.ReadFormAsync(c.RequestAborted);
            return Json(await files.SaveAsync(ActorId(c),form["teamId"],form["milestoneId"],form["title"],form.Files.GetFile("file"),c.RequestAborted));
        });
        secured.MapGet("/submissions/{id}/download",(string id,HttpContext c,PlatformRuntime r)=>
        {
            var submission=r.Read(rt=>
            {
                var actor=rt.Actor(ActorId(c));
                if(actor.Role==UserRoleType.Supervisor)PlatformRuntime.RequireSupervisorPermission(actor,SupervisorPermissions.DownloadSubmissions);
                var item=rt.Submissions.FirstOrDefault(s=>s.Id==id)??throw new PlatformException("التسليم غير موجود",404,"not_found");rt.AccessTeam(actor,item.TeamId);return item;
            });
            string path=Path.Combine(r.UploadDirectory,submission.Id+".bin");if(!File.Exists(path))throw new PlatformException("ملف التسليم غير موجود؛ راجع النسخة الاحتياطية",404,"file_missing");
            return Results.File(path,submission.ContentType,submission.OriginalFileName);
        });
        secured.MapPost("/submissions/{id}/review",(string id,ReviewRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"submission.review",id,(rt,u)=>rt.ReviewSubmission(u,id,request),"تم حفظ مراجعة التسليم وإخطار الفريق"));
    }
}
