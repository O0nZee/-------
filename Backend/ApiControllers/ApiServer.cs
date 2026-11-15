using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;

namespace GraduationPlatform.Backend.ApiControllers;

public static class ApiServer
{
    public const string Scheme="PlatformCookie";
    internal static IResult Json(string content)=>Results.Content(content,"application/json; charset=utf-8");
    internal static string ActorId(HttpContext context)=>context.User.FindFirstValue(ClaimTypes.NameIdentifier)??throw new PlatformException("يرجى تسجيل الدخول",401,"unauthenticated");
    internal static IResult Query(HttpContext context,PlatformRuntime runtime,Func<PlatformRuntime,User,object> action)=>Json(runtime.ReadJson(r=>action(r,r.Actor(ActorId(context)))));
    internal static IResult Change(HttpContext context,PlatformRuntime runtime,string actionName,string entityId,Func<PlatformRuntime,User,object> action,string message="تم حفظ التغييرات")
    {
        string id=ActorId(context);return Json(runtime.Mutate(id,actionName,entityId,r=>action(r,r.Actor(id)),message));
    }
    public static void MapEndpoints(WebApplication app)
    {
        var api=app.MapGroup("/api");
        api.MapGet("/health",(SimilarityService analysis)=>Results.Json(new {isSuccess=true,data=new {status="healthy",version="3.0.0",database="SQLite",similarity=analysis.Health}}));
        AuthApi.Map(api);
        var secured=api.MapGroup("").RequireAuthorization();
        TeamApi.Map(secured);IdeaApi.Map(secured);TrackingApi.Map(secured);CollegeAdministrationApi.Map(secured);SupervisorApi.Map(secured);
        secured.MapGet("/settings",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>new {rt.Options.MaxTeamMembers,rt.Options.MaxSupervisorTeams,rt.Options.MaxUploadMegabytes}));
        secured.MapGet("/profile",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>PlatformRuntime.UserView(u)));
        secured.MapPatch("/profile",(ProfileRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"profile.update",ActorId(c),(rt,u)=>
        {
            PlatformRuntime.Require(rt.Auth.UpdateProfile(u.Id,Input.Text(request.FullName,"الاسم الكامل",100),Input.Text(request.Department,"القسم",100),Input.Text(request.Phone,"الهاتف",30,false),Input.Text(request.Bio,"نبذة",2000,false)));
            rt.SynchronizeProfile(u);
            return PlatformRuntime.UserView(u);
        }));
        secured.MapPost("/profile/password",async (PasswordRequest request,HttpContext c,PlatformRuntime r)=>
        {
            string response=r.Mutate(ActorId(c),"password.change",ActorId(c),rt=>{PlatformRuntime.Require(rt.Auth.ChangePassword(ActorId(c),request.CurrentPassword,Input.Password(request.NewPassword)));return null;},"تم تغيير كلمة المرور؛ يرجى تسجيل الدخول مجددًا");
            await c.SignOutAsync(Scheme);return Json(response);
        });
        secured.MapGet("/dashboard",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Dashboard(u)));
        secured.MapGet("/notifications",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Notifications.Where(n=>n.UserId==u.Id).OrderByDescending(n=>n.CreatedAt).Take(200)));
        secured.MapPost("/notifications/read-all",(HttpContext c,PlatformRuntime r)=>Change(c,r,"notifications.read",null,(rt,u)=>
        {foreach(var n in rt.Notifications.Where(n=>n.UserId==u.Id && n.ReadAt==null))n.ReadAt=DateTime.UtcNow;return null;}));
        secured.MapPost("/notifications/{id}/read",(string id,HttpContext c,PlatformRuntime r)=>Change(c,r,"notification.read",id,(rt,u)=>
        {
            var notification=rt.Notifications.FirstOrDefault(n=>n.Id==id && n.UserId==u.Id)??throw new PlatformException("الإشعار غير موجود",404,"not_found");notification.ReadAt=DateTime.UtcNow;return notification;
        }));
        secured.MapGet("/reports",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Reports(u))).RequireAuthorization("Academic");
        secured.MapGet("/reports/export",(HttpContext c,PlatformRuntime r)=>
        {
            string text=r.Read(rt=>ReportExport.Csv(rt,rt.Actor(ActorId(c))));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),"text/csv; charset=utf-8",$"graduation-report-{DateTime.UtcNow:yyyyMMdd}.csv");
        }).RequireAuthorization("Academic");
    }
    internal static async Task SignIn(HttpContext context,User user)
    {
        var principal=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id),new Claim(ClaimTypes.Name,user.Username),new Claim(ClaimTypes.Role,user.Role.ToString()),new Claim("stamp",user.SecurityStamp)},Scheme));
        await context.SignInAsync(Scheme,principal,new AuthenticationProperties{IsPersistent=false,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(8),AllowRefresh=false});context.User=principal;
    }
    internal static IResult SessionResult(HttpContext context,User user,IAntiforgery antiforgery,string message)=>Results.Json(new {isSuccess=true,message,data=new {user=PlatformRuntime.UserView(user),csrfToken=antiforgery.GetAndStoreTokens(context).RequestToken}});
}
