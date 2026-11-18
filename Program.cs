using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;

var builder=WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();builder.Logging.AddConsole();
builder.Configuration.AddJsonFile("appsettings.Local.json",optional:true,reloadOnChange:false).AddEnvironmentVariables().AddCommandLine(args);
var options=builder.Configuration.GetSection("Platform").Get<PlatformOptions>()??new PlatformOptions();
var mail=builder.Configuration.GetSection("Mail").Get<MailOptions>()??new MailOptions();
var similarity=builder.Configuration.GetSection("Similarity").Get<SimilarityOptions>()??new SimilarityOptions();
if(builder.Environment.IsProduction() && options.DemoData)throw new InvalidOperationException("Demo accounts are only available in Development. Set Platform__DemoData=false before a production start.");
if(!Uri.TryCreate(options.PublicUrl,UriKind.Absolute,out var publicUrl) || (publicUrl.Scheme!="http" && publicUrl.Scheme!="https"))throw new InvalidOperationException("Platform:PublicUrl must be an absolute HTTP or HTTPS URL.");
if(options.UseHttpsProxy && publicUrl.Scheme!="https")throw new InvalidOperationException("Platform:UseHttpsProxy requires an HTTPS public URL.");
builder.Services.AddSingleton(options);builder.Services.AddSingleton(mail);
builder.Services.AddSingleton(sp=>new PlatformRuntime(options,builder.Environment.ContentRootPath,builder.Configuration["Bootstrap:AdminPassword"]));
builder.Services.AddSingleton(similarity);
builder.Services.AddSingleton(sp=>new SimilarityService(similarity,builder.Environment.ContentRootPath,sp.GetRequiredService<ILogger<SimilarityService>>()));
builder.Services.AddSingleton<FileSubmissionService>();
builder.Services.AddHostedService<MailDeliveryService>();builder.Services.AddHostedService<MaintenanceService>();
var dataDirectory=Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,options.DataDirectory));
Directory.CreateDirectory(Path.Combine(dataDirectory,"keys"));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory,"keys"))).SetApplicationName("GraduationPlatform");
builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());o.SerializerOptions.MaxDepth=32;});
builder.Services.Configure<FormOptions>(o=>o.MultipartBodyLengthLimit=(long)options.MaxUploadMegabytes*1024*1024+1024*1024);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=(long)options.MaxUploadMegabytes*1024*1024+1024*1024);
builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="graduation.csrf";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;});
builder.Services.AddAuthentication(ApiServer.Scheme).AddCookie(ApiServer.Scheme,o=>
{
    o.Cookie.Name="graduation.session";o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;
    o.LoginPath="/";o.AccessDeniedPath="/";o.ExpireTimeSpan=TimeSpan.FromHours(8);o.SlidingExpiration=false;
    o.Events.OnValidatePrincipal=async context=>
    {
        string id=context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var runtime=context.HttpContext.RequestServices.GetRequiredService<PlatformRuntime>();
        var user=id==null?null:runtime.Read(r=>r.Auth.GetUserById(id));
        if(user==null || !user.IsActive || user.SecurityStamp!=context.Principal?.FindFirstValue("stamp") || user.Role.ToString()!=context.Principal?.FindFirstValue(ClaimTypes.Role))
        {context.RejectPrincipal();await context.HttpContext.SignOutAsync(ApiServer.Scheme);}
    };
    o.Events.OnRedirectToLogin=context=>
    {
        if(context.Request.Path.StartsWithSegments("/api")){context.Response.StatusCode=401;return context.Response.WriteAsJsonAsync(new{isSuccess=false,message="انتهت الجلسة؛ يرجى تسجيل الدخول",errorCode="unauthenticated"});}
        context.Response.Redirect(context.RedirectUri);return Task.CompletedTask;
    };
    o.Events.OnRedirectToAccessDenied=context=>
    {
        if(context.Request.Path.StartsWithSegments("/api")){context.Response.StatusCode=403;return context.Response.WriteAsJsonAsync(new{isSuccess=false,message="ليس لديك صلاحية لتنفيذ هذا الإجراء",errorCode="forbidden"});}
        context.Response.Redirect("/?denied=1");return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(o=>
{
    o.AddPolicy("Admin",p=>p.RequireRole("Admin"));o.AddPolicy("Student",p=>p.RequireRole("Student"));
    o.AddPolicy("Supervisor",p=>p.RequireRole("Supervisor"));o.AddPolicy("Academic",p=>p.RequireRole("Admin","Supervisor"));
});
builder.Services.AddRateLimiter(o=>
{
    o.AddPolicy("auth",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",key=>new FixedWindowRateLimiterOptions{PermitLimit=20,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
    o.AddPolicy("similarity",context=>RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier)??context.Connection.RemoteIpAddress?.ToString()??"local",key=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
    o.OnRejected=async (context,token)=>{context.HttpContext.Response.StatusCode=429;await context.HttpContext.Response.WriteAsJsonAsync(new{isSuccess=false,message="محاولات كثيرة؛ انتظر دقيقة ثم حاول مجددًا",errorCode="rate_limited"},token);};
});
await using var app=builder.Build();
app.Services.GetRequiredService<PlatformRuntime>();
await app.Services.GetRequiredService<SimilarityService>().InitializeAsync();
// Enable only behind a proxy configured to redirect all public HTTP traffic to HTTPS.
// The externally configured scheme is authoritative; incoming forwarded headers are not trusted.
if(options.UseHttpsProxy)app.Use((context,next)=>{context.Request.Scheme="https";return next(context);});
app.Use(async (context,next)=>
{
    context.Response.Headers["X-Content-Type-Options"]="nosniff";context.Response.Headers["X-Frame-Options"]="DENY";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers.CacheControl="no-cache";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'";
    if(context.Request.Path.StartsWithSegments("/api"))context.Response.Headers.CacheControl="no-store";
    try{await next(context);}
    catch(Exception ex) when(!context.Response.HasStarted)
    {
        int status=ex switch{PlatformException p=>p.Status,AntiforgeryValidationException=>403,BadHttpRequestException b=>b.StatusCode,JsonException=>400,_=>500};
        string code=ex is PlatformException pex?pex.Code:status==403?"csrf_failed":status<500?"invalid_request":"server_error";
        string message=ex is PlatformException platform?ErrorMessages.Localize(platform.Message):status==403?"تعذر التحقق من الطلب؛ أعد تحميل الصفحة وحاول مجددًا":status==413?"الملف أو الطلب أكبر من الحد المسموح":status<500?"بيانات الطلب غير صالحة":"تعذر حفظ العملية؛ حاول مجددًا";
        if(status>=500)app.Logger.LogError(ex,"Request failed: {Method} {Path}",context.Request.Method,context.Request.Path);
        context.Response.StatusCode=status;await context.Response.WriteAsJsonAsync(new{isSuccess=false,message,errorCode=code});
    }
});
app.UseAuthentication();app.UseRateLimiter();app.UseAuthorization();app.UseAntiforgery();
app.Use(async (context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api") && context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next(context);
});
app.UseStaticFiles(new StaticFileOptions{FileProvider=new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath,"UI_UX")),RequestPath="/UI_UX"});
app.UseStaticFiles(new StaticFileOptions{FileProvider=new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath,"Frontend","Scripts")),RequestPath="/Frontend/Scripts"});
ApiServer.MapEndpoints(app);
foreach(var route in new[]{"/","/login","/login.html","/Frontend/Pages/login.html"})
    app.MapGet(route,()=>Results.File(Path.Combine(app.Environment.ContentRootPath,"Frontend","Pages","login.html"),"text/html; charset=utf-8"));
foreach(var route in new[]{"/index","/index.html","/Frontend/Pages/index.html"})
    app.MapGet(route,(HttpContext context)=>Results.Redirect("/"+context.Request.QueryString.Value));
foreach(var page in new[]{("student","Student"),("supervisor","Supervisor"),("admin","Admin")})
{
    foreach(var route in new[]{"/"+page.Item1,"/"+page.Item1+".html","/Frontend/Pages/"+page.Item1+".html"})
        app.MapGet(route,()=>Results.File(Path.Combine(app.Environment.ContentRootPath,"Frontend","Pages",page.Item1+".html"),"text/html; charset=utf-8")).RequireAuthorization(page.Item2);
}
// Dashboard standalone page — accessible to any authenticated user
foreach(var route in new[]{"/dashboard","/dashboard.html","/Frontend/Pages/dashboard.html"})
    app.MapGet(route,()=>Results.File(Path.Combine(app.Environment.ContentRootPath,"Frontend","Pages","dashboard.html"),"text/html; charset=utf-8")).RequireAuthorization();
app.MapFallback(()=>Results.Json(new{isSuccess=false,message="الصفحة أو الخدمة غير موجودة",errorCode="not_found"},statusCode:404));
app.Logger.LogInformation("Graduation Platform is ready. Data is stored in SQLite. Mail delivery mode: {Mode}.",mail.Mode);
app.Run();

public partial class Program { }
