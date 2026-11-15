using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using static GraduationPlatform.Backend.ApiControllers.ApiServer;

namespace GraduationPlatform.Backend.ApiControllers;

internal static class IdeaApi
{
    public static void Map(RouteGroupBuilder secured)
    {
        secured.MapGet("/ideas",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>
        {
            var ids=rt.VisibleTeams(u).Select(t=>t.Id).ToHashSet();return rt.Ideas.ExportIdeas().Where(i=>ids.Contains(i.TeamId??"")).OrderByDescending(i=>i.CreatedAt).Select(i=>rt.IdeaView(i));
        }));
        secured.MapPost("/ideas",(IdeaRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"idea.submit",null,(rt,u)=>rt.SubmitIdea(u,request),"تم تقديم الفكرة للمراجعة"));
        secured.MapPut("/ideas/{id}",(string id,IdeaRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"idea.update",id,(rt,u)=>rt.SubmitIdea(u,request,id),"تم تحديث الفكرة وإعادة تقديمها للمراجعة"));
        secured.MapPost("/ideas/{id}/review",(string id,ReviewRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"idea.review",id,(rt,u)=>rt.ReviewIdea(u,id,request),"تم حفظ قرار المراجعة وإخطار الفريق"));
        secured.MapPost("/ideas/similarity",async (IdeaRequest request,HttpContext c,PlatformRuntime r,SimilarityService analysis)=>
        {
            var work=r.Read(rt=>rt.PrepareSimilarity(rt.Actor(ActorId(c)),request));
            var matches=await analysis.AnalyzeAsync(work,c.RequestAborted);
            return Results.Json(new{isSuccess=true,data=matches});
        }).RequireRateLimiting("similarity");
        secured.MapPost("/ideas/classify",async (IdeaRequest request,HttpContext c,SimilarityService analysis)=>
        {
            request=ValidateAnalysis(request);
            var classification=await analysis.ClassifyTrackAsync(request.Title,request.Description,request.Technologies,c.RequestAborted);
            return Results.Json(new{isSuccess=true,data=classification});
        }).RequireRateLimiting("similarity");
        secured.MapPost("/ideas/evaluate",async (IdeaRequest request,HttpContext c,SimilarityService analysis)=>
        {
            request=ValidateAnalysis(request);
            var evaluation=await analysis.EvaluateQualityAsync(request.Title,request.Description,request.Objectives,request.Technologies,c.RequestAborted);
            return Results.Json(new{isSuccess=true,data=evaluation});
        }).RequireRateLimiting("similarity");
        secured.MapGet("/archive",(HttpContext c,PlatformRuntime r)=>Query(c,r,(rt,u)=>rt.Ideas.SearchArchive(c.Request.Query["keyword"],c.Request.Query["track"],c.Request.Query["year"])));
        secured.MapPost("/archive",(HistoricalProjectRequest request,HttpContext c,PlatformRuntime r)=>Change(c,r,"archive.add",null,(rt,u)=>
        {
            if(u.Role!=UserRoleType.Admin)throw new PlatformException("إضافة مشروعات الأرشيف متاحة للإدارة",403,"forbidden");
            return PlatformRuntime.Require(rt.Ideas.AddArchiveProject(Input.Text(request.Title,"العنوان",200),Input.Text(request.Abstract,"الملخص",10000),Input.AcademicYear(request.AcademicYear),Input.Text(request.Department,"القسم",100),Input.Text(request.Track,"المسار",100),Input.Technologies(request.Technologies),Input.Text(request.SupervisorName,"اسم المشرف",100),Input.Text(request.Grade,"الدرجة",30)));
        }));
    }

    private static IdeaRequest ValidateAnalysis(IdeaRequest request) => new()
    {
        Title=Input.Text(request.Title,"العنوان",200),
        Description=Input.Text(request.Description,"الوصف",10000,false),
        Objectives=Input.Text(request.Objectives,"الأهداف",5000,false),
        Technologies=Input.Technologies(request.Technologies)
    };
}
