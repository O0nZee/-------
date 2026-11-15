using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public partial class ProjectIdeaService
    {
        public List<ProjectIdea> ExportIdeas() { lock (_lock) return _ideas.Values.ToList(); }
        public List<ProjectArchive> ExportArchive() { lock (_lock) return _archive.Values.ToList(); }
        public List<IdeaReview> ExportReviews() { lock (_lock) return _reviews.ToList(); }
        public void ImportState(IEnumerable<ProjectIdea> ideas, IEnumerable<ProjectArchive> archive, IEnumerable<IdeaReview> reviews)
        {
            lock (_lock)
            {
                _ideas.Clear(); _archive.Clear(); _reviews.Clear();
                foreach (var idea in ideas) _ideas.Add(idea.Id, idea);
                foreach (var project in archive) _archive.Add(project.Id, project);
                _reviews.AddRange(reviews);
            }
        }
        public Result<ProjectIdea> UpdateIdea(string ideaId, string title, string description, string objectives, List<string> technologies, string track)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || description.Trim().Length < 20)
                return Result<ProjectIdea>.Fail("Title and a description of at least 20 characters are required");
            if (title.Trim().Length > 200 || description.Trim().Length > 10000
                || (objectives?.Trim().Length ?? 0) > 10000 || (track?.Trim().Length ?? 0) > 100)
                return Result<ProjectIdea>.Fail("Project text is too long");
            if (!ValidTechnologies(technologies)) return Result<ProjectIdea>.Fail("Invalid project technologies");
            lock (_lock)
            {
                ProjectIdea idea;
                if (!_ideas.TryGetValue(ideaId, out idea)) return Result<ProjectIdea>.Fail("Idea not found");
                if (idea.Status == IdeaStatus.Approved) return Result<ProjectIdea>.Fail("An approved idea must be reopened by the supervisor before editing");
                idea.Title = title.Trim(); idea.Description = description.Trim(); idea.Objectives = objectives ?? "";
                idea.Technologies = CopyTechnologies(technologies); idea.Track = track ?? "General";
                idea.Status = IdeaStatus.PendingReview; idea.UpdatedAt = DateTime.UtcNow;
                return Result<ProjectIdea>.Ok(idea, "Idea resubmitted for review");
            }
        }
    }
}
