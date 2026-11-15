using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class CollegeAdministrationApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        var admin=secured.MapGroup("/admin").RequireAuthorization("Admin");
        admin.MapGet("/users",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Auth.ExportUsers().Select(PlatformRuntime.UserView)));
        admin.MapPost("/users",(RegisterRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"user.create",null,(rt,u)=>PlatformRuntime.UserView(rt.RegisterUser(request,Input.EnumValue<UserRoleType>(request.Role,"الدور"))),"تم إنشاء الحساب"));
        admin.MapPatch("/users/{id}",(string id,UserUpdateRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"user.update",id,(rt,u)=>rt.UpdateUser(u,id,request)));
        admin.MapGet("/audit",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Audit.Where(a=>!a.Action.StartsWith("mail.")).OrderByDescending(a=>a.CreatedAt).Take(200).Select(a=>new{a.Id,a.Action,a.EntityId,a.CreatedAt,a.Details,actorName=rt.Auth.GetUserById(a.ActorId??"")?.Profile.FullName??"النظام"})));
        admin.MapGet("/mail",(HttpContext c,PlatformRuntime r,MailOptions mail,IWebHostEnvironment environment)=>Query(c,r,(rt,u)=>new{mode=mail.Mode,localPreview=environment.IsDevelopment() && mail.Mode=="Local",messages=rt.Mail.OrderByDescending(m=>m.CreatedAt).Take(100).Select(m=>new{m.Id,m.To,m.Subject,m.Status,m.Attempts,m.CreatedAt,m.SentAt,m.LastError,body=environment.IsDevelopment() && mail.Mode=="Local"?m.Body:null})}));
        admin.MapPost("/mail/{id}/retry",(string id,HttpContext c,PlatformRuntime r)=>Change(c,r,"mail.retry",id,(rt,u)=>
        {
            var message=rt.Mail.FirstOrDefault(m=>m.Id==id)??throw new PlatformException("الرسالة غير موجودة",404,"not_found");
            if(message.Status!="Failed")throw new PlatformException("إعادة الإرسال متاحة للرسائل التي فشلت بعد جميع المحاولات");message.Status="Pending";message.Attempts=0;message.NextAttemptAt=DateTime.UtcNow;return null;
        }));
        admin.MapPost("/maintenance",(PlatformRuntime r)=>Results.Json(new{isSuccess=true,message="تم فحص المواعيد",data=new{newNotifications=r.GenerateDeadlineNotifications()}}));
        admin.MapGet("/backups",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>
        {
            string directory=Path.Combine(rt.Store.DataDirectory,"backups");
            return Directory.Exists(directory)?Directory.EnumerateFiles(directory,"graduation-*.zip").Select(path=>new FileInfo(path)).OrderByDescending(f=>f.CreationTimeUtc).Select(f=>new{name=f.Name,sizeBytes=f.Length,createdAt=f.CreationTimeUtc}).ToArray():Array.Empty<object>();
        }));
        admin.MapPost("/backups",(PlatformRuntime r)=>Results.Json(new{isSuccess=true,message="تم إنشاء نسخة احتياطية للبيانات والملفات",data=new{name=Path.GetFileName(r.CreateBackup())}}));
        admin.MapGet("/backups/{name}/download",(string name,PlatformRuntime r)=>
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^graduation-\d{8}-\d{6}-[a-f0-9]{6}\.zip$"))throw new PlatformException("اسم النسخة غير صالح");
            string path=Path.Combine(r.Store.DataDirectory,"backups",name);if(!File.Exists(path))throw new PlatformException("النسخة غير موجودة",404,"not_found");return Results.File(path,"application/zip",name);
        });
    }
}
