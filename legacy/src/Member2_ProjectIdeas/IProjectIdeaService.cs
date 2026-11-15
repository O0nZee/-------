using System.Collections.Generic;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public interface IProjectIdeaService
    {
        Result<ProjectIdea> SubmitIdea(string studentId, string title, string description, 
            string objectives, List<string> technologies, string track, string teamId = null);

        List<SimilarityResult> CheckSimilarity(string title, string description, List<string> technologies, string excludeIdeaId = null);

        Result<IdeaReview> ReviewIdea(string supervisorId, string ideaId, IdeaStatus decision, string feedback);

        Result<ProjectArchive> AddArchiveProject(string title, string abstractText, string academicYear, 
            string department, string track, List<string> technologies, string supervisorName, string grade);

        List<ProjectArchive> SearchArchive(string keyword = null, string track = null, string year = null);

        List<ProjectIdea> SearchIdeas(string keyword = null, string track = null, IdeaStatus? status = null);

        ProjectIdea GetIdeaById(string ideaId);

        List<IdeaReview> GetReviewsForIdea(string ideaId);

        List<SimilarityResult> GetSimilarPastProjects(string ideaId, double minThreshold = 0.30);
    }
}
