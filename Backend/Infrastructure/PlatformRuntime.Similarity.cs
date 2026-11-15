using GraduationPlatform.Backend.ApiControllers;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;

namespace GraduationPlatform.Infrastructure;

public sealed partial class PlatformRuntime
{
    public SimilarityWorkItem PrepareSimilarity(User actor, IdeaRequest request)
    {
        string excluded = Input.Text(request.ExcludeIdeaId, "معرف الفكرة المستبعدة", 64, false);
        if (excluded.Length == 0) excluded = null;
        if (!string.IsNullOrWhiteSpace(request.TeamId)) AccessTeam(actor, request.TeamId);
        if (excluded != null)
        {
            var idea = Ideas.GetIdeaById(excluded) ?? throw new PlatformException("الفكرة غير موجودة", 404, "not_found");
            AccessTeam(actor, idea.TeamId);
        }
        var query = new SimilarityQuery
        {
            Title = Input.Text(request.Title, "العنوان", 200),
            Description = Input.Text(request.Description, "الوصف", 10000, false),
            Technologies = Input.Technologies(request.Technologies), ExcludeIdeaId = excluded
        };
        var documents = Ideas.GetSimilarityDocuments(excluded);
        var visible = VisibleTeams(actor).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
            document.RevealDetails = actor.Role == UserRoleType.Admin || document.SourceType == "Archive"
                || (document.TeamId != null && visible.Contains(document.TeamId));
        return new SimilarityWorkItem { Query = query, Documents = documents };
    }
}
