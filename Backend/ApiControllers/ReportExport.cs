using System.Globalization;
using System.Text;
using GraduationPlatform.Infrastructure;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.SupervisorManagement;

namespace GraduationPlatform.Backend.ApiControllers;

public static class ReportExport
{
    private static string Cell(object value)
    {
        string text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        if(text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@'))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    public static string Csv(PlatformRuntime runtime,User actor)
    {
        PlatformRuntime.RequireSupervisorPermission(actor,SupervisorPermissions.ViewReports);
        var result=new StringBuilder();
        result.AppendLine(string.Join(',',new[]{"الفريق","المشروع","المشرف","الأعضاء","إجمالي المهام","المكتمل","نسبة الإنجاز","المهام المتأخرة","الحالة","الدرجة"}.Select(Cell)));
        foreach(var team in runtime.VisibleTeams(actor))
        {
            var timeline=runtime.Tracking.GetProjectTimeline(team.Id);
            var fields=new object[]{team.Name,runtime.Ideas.GetIdeaById(team.ProjectIdeaId??"")?.Title??"",runtime.Teams.GetSupervisor(team.AssignedSupervisorId??"")?.Name??"",team.Members.Count,timeline.TotalTasks,timeline.CompletedTasks,timeline.CompletionPercentage,
                timeline.ActiveAlerts.Count(a=>a.IsOverdue),team.IsArchived?"مؤرشف":"جارٍ",runtime.Ideas.ExportArchive().FirstOrDefault(a=>a.TeamId==team.Id)?.FinalGrade??""};
            result.AppendLine(string.Join(',',fields.Select(Cell)));
        }
        return result.ToString();
    }
}
