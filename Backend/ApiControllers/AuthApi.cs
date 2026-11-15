using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class AuthApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/auth/session",(HttpContext c,PlatformRuntime r,IAntiforgery antiforgery)=>
        {
            object user=null;
            if(c.User.Identity?.IsAuthenticated==true)user=r.Read(rt=>PlatformRuntime.UserView(rt.Actor(ActorId(c))));
            return Results.Json(new{isSuccess=true,data=new{user,csrfToken=antiforgery.GetAndStoreTokens(c).RequestToken}});
        });
        api.MapPost("/auth/login",async (LoginRequest request,HttpContext c,PlatformRuntime r,IAntiforgery antiforgery)=>
        {
            var user=r.Authenticate(Input.Text(request.UsernameOrEmail,"اسم المستخدم أو البريد",254),request.Password);
            await SignIn(c,user);return SessionResult(c,user,antiforgery,"تم تسجيل الدخول");
        }).RequireRateLimiting("auth");
        api.MapPost("/auth/register",async (RegisterRequest request,HttpContext c,PlatformRuntime r,IAntiforgery antiforgery)=>
        {
            r.Mutate(null,"user.register",null,rt=>PlatformRuntime.UserView(rt.RegisterUser(request,UserRoleType.Student)),"تم إنشاء الحساب");
            var user=r.Authenticate(request.Username,request.Password);await SignIn(c,user);return SessionResult(c,user,antiforgery,"تم إنشاء حساب الطالب وتسجيل الدخول");
        }).RequireRateLimiting("auth");
        api.MapPost("/auth/logout",async (HttpContext c,IAntiforgery antiforgery)=>
        {
            await c.SignOutAsync(Scheme);c.User=new ClaimsPrincipal(new ClaimsIdentity());return SessionResult(c,null,antiforgery,"تم تسجيل الخروج");
        });
        api.MapPost("/auth/forgot-password",(ForgotRequest request,PlatformRuntime r)=>
        {
            string email=Input.Text(request.Email,"البريد الإلكتروني",254);
            return Json(r.Mutate(null,"password.reset.request",null,rt=>
            {
                var result=rt.Auth.RequestPasswordReset(email);
                if(result.IsSuccess)rt.QueueMail(email,"استعادة كلمة المرور","افتح الرابط التالي لإنشاء كلمة مرور جديدة. الرابط صالح لمدة ساعتين ويُستخدم مرة واحدة.\n\n"+rt.Options.PublicUrl.TrimEnd('/')+"/?reset="+Uri.EscapeDataString(result.Data));
                return null;
            },"إذا كان البريد مسجلًا ونشطًا، سيتم إرسال رابط استعادة كلمة المرور"));
        }).RequireRateLimiting("auth");
        api.MapPost("/auth/reset-password",(ResetRequest request,PlatformRuntime r)=>Json(r.Mutate(null,"password.reset",null,rt=>
        {
            PlatformRuntime.Require(rt.Auth.ResetPassword(Input.Text(request.Token,"رابط الاستعادة",256),Input.Password(request.NewPassword)));return null;
        },"تم تغيير كلمة المرور. سجّل الدخول بكلمة المرور الجديدة"))).RequireRateLimiting("auth");
    }
}
