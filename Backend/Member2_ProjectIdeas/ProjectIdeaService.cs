using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public partial class ProjectIdeaService : IProjectIdeaService
    {
        private readonly Dictionary<string, ProjectIdea> _ideas = new Dictionary<string, ProjectIdea>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProjectArchive> _archive = new Dictionary<string, ProjectArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IdeaReview> _reviews = new List<IdeaReview>();
        private readonly object _lock = new object();

        public ProjectIdeaService()
        {
            SeedArchiveData();
        }

        private void SeedArchiveData()
        {
            AddArchiveProject(
                "Smart Attendance System using Facial Recognition",
                "Automated classroom attendance monitoring using deep learning CNNs and live IP camera streams for universities.",
                "2023-2024", "Computer Science", "AI & Computer Vision",
                new List<string> { "Python", "OpenCV", "TensorFlow", "FastAPI", "React" },
                "Prof. Mohamed Abdel-Rahman", "A+");

            AddArchiveProject(
                "Telemedicine Consultation and Electronic Health Records",
                "A secure HIPAA-compliant telehealth platform connecting doctors with rural patients with video calls and prescription history.",
                "2022-2023", "Information Systems", "Healthcare Informatics",
                new List<string> { "C#", "ASP.NET Core", "SQL Server", "WebRTC", "Angular" },
                "Dr. Rania Mahmoud", "Excellent");

            AddArchiveProject(
                "Autonomous Mobile Delivery Robot for Campus",
                "Indoor localization and obstacle avoidance robot navigating campus corridors using LiDAR and SLAM algorithms.",
                "2023-2024", "Computer Engineering", "Robotics & IoT",
                new List<string> { "ROS2", "Python", "C++", "Raspberry Pi", "LiDAR" },
                "Dr. Khaled Sami", "A");

            AddArchiveProject(
                "Blockchain-Based Academic Degree Verification System",
                "Decentralized ledger for tamper-proof storage and instant verification of university diplomas and certificates.",
                "2022-2023", "Cybersecurity", "Blockchain & Security",
                new List<string> { "Solidity", "Ethereum", "Node.js", "Web3.js", "React" },
                "Prof. Essam Hamdi", "A");
        }

        public Result<ProjectIdea> SubmitIdea(string studentId, string title, string description,
            string objectives, List<string> technologies, string track, string teamId = null)
        {
            if (string.IsNullOrWhiteSpace(studentId)) return Result<ProjectIdea>.Fail("Submitter student ID is required");
            if (string.IsNullOrWhiteSpace(title)) return Result<ProjectIdea>.Fail("Project title is required");
            if (title.Trim().Length > 200) return Result<ProjectIdea>.Fail("Project title is too long");
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 20)
                return Result<ProjectIdea>.Fail("Description must be detailed (at least 20 characters)");
            if (description.Trim().Length > 10000 || (objectives?.Trim().Length ?? 0) > 10000 || (track?.Trim().Length ?? 0) > 100)
                return Result<ProjectIdea>.Fail("Project text is too long");
            if (!ValidTechnologies(technologies)) return Result<ProjectIdea>.Fail("Invalid project technologies");

            lock (_lock)
            {
                var idea = new ProjectIdea
                {
                    Title = title.Trim(),
                    Description = description.Trim(),
                    Objectives = objectives != null ? objectives.Trim() : string.Empty,
                    Technologies = CopyTechnologies(technologies),
                    Track = track != null ? track.Trim() : "General",
                    SubmitterStudentId = studentId.Trim(),
                    TeamId = teamId,
                    Status = IdeaStatus.PendingReview,
                    CreatedAt = DateTime.UtcNow
                };

                _ideas[idea.Id] = idea;
                return Result<ProjectIdea>.Ok(idea, "Project idea submitted successfully for supervisor review");
            }
        }

        public List<SimilarityResult> CheckSimilarity(string title, string description, List<string> technologies, string excludeIdeaId = null)
        {
            var query = new SimilarityQuery { Title = title, Description = description, Technologies = technologies, ExcludeIdeaId = excludeIdeaId };
            return TextSimilarityAnalysis.Compare(query, GetSimilarityDocuments(excludeIdeaId));
        }

        public List<SimilarityDocument> GetSimilarityDocuments(string excludeIdeaId = null)
        {
            lock (_lock)
            {
                string sourceTeamId = excludeIdeaId != null && _ideas.TryGetValue(excludeIdeaId, out var sourceIdea)
                    ? sourceIdea.TeamId : null;
                var documents = new List<SimilarityDocument>();
                foreach (var idea in _ideas.Values.Where(i => !string.Equals(i.Id, excludeIdeaId, StringComparison.OrdinalIgnoreCase)))
                {
                    documents.Add(new SimilarityDocument
                    {
                        Id = idea.Id, Title = idea.Title, Description = idea.Description,
                        Technologies = CopyTechnologies(idea.Technologies), SourceType = "CurrentIdea", TeamId = idea.TeamId
                    });
                }
                foreach (var arch in _archive.Values)
                {
                    if (sourceTeamId != null && arch.TeamId == sourceTeamId) continue;
                    documents.Add(new SimilarityDocument
                    {
                        Id = arch.Id, Title = arch.Title, Description = arch.Abstract,
                        Technologies = CopyTechnologies(arch.Technologies), SourceType = "Archive", TeamId = arch.TeamId
                    });
                }
                return documents;
            }
        }

        internal static SimilarityResult BuildSimilarityResult(string sourceId, string matchId, string matchTitle,
            string sourceType, double score, List<string> overlapping)
        {
            SimilarityRiskLevel risk;
            string recommendation;

            if (score >= 0.70)
            {
                risk = SimilarityRiskLevel.High;
                recommendation = "تشابه مرتفع؛ راجع الفروق في أهداف المشروع ونطاقه مع المشرف قبل الاعتماد.";
            }
            else if (score >= 0.40)
            {
                risk = SimilarityRiskLevel.Moderate;
                recommendation = "يوجد تشابه جزئي؛ وضّح إضافة مشروعك وما يميّزه.";
            }
            else
            {
                risk = SimilarityRiskLevel.Low;
                recommendation = "تشابه محدود في بعض المصطلحات أو التقنيات.";
            }

            return new SimilarityResult
            {
                SourceIdeaId = sourceId,
                MatchedItemId = matchId,
                MatchedTitle = matchTitle,
                MatchedSourceType = sourceType,
                SimilarityScore = score,
                RiskLevel = risk,
                OverlappingKeywords = overlapping,
                Recommendation = recommendation
            };
        }

        private static bool ValidTechnologies(List<string> values) => values == null
            || (values.Count <= 20 && values.All(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length <= 60));

        private static List<string> CopyTechnologies(List<string> values) => (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public Result<IdeaReview> ReviewIdea(string supervisorId, string ideaId, IdeaStatus decision, string feedback)
        {
            if (string.IsNullOrWhiteSpace(supervisorId)) return Result<IdeaReview>.Fail("Supervisor ID is required");
            if (string.IsNullOrWhiteSpace(ideaId)) return Result<IdeaReview>.Fail("Idea ID is required");
            if (decision != IdeaStatus.Approved && decision != IdeaStatus.Rejected && decision != IdeaStatus.ModificationRequested)
                return Result<IdeaReview>.Fail("Decision must be Approved, Rejected, or ModificationRequested");

            lock (_lock)
            {
                if (!_ideas.ContainsKey(ideaId))
                    return Result<IdeaReview>.Fail("Project idea not found");

                var idea = _ideas[ideaId];
                idea.Status = decision;
                idea.UpdatedAt = DateTime.UtcNow;

                var review = new IdeaReview
                {
                    ProjectIdeaId = ideaId,
                    SupervisorId = supervisorId,
                    Decision = decision,
                    FeedbackComments = feedback != null ? feedback.Trim() : string.Empty,
                    ReviewedAt = DateTime.UtcNow
                };

                _reviews.Add(review);
                return Result<IdeaReview>.Ok(review, string.Format("Idea review submitted. Status changed to {0}", decision));
            }
        }

        public Result<ProjectArchive> AddArchiveProject(string title, string abstractText, string academicYear,
            string department, string track, List<string> technologies, string supervisorName, string grade)
        {
            if (string.IsNullOrWhiteSpace(title)) return Result<ProjectArchive>.Fail("Archive project title is required");
            if (title.Trim().Length > 200 || (abstractText?.Trim().Length ?? 0) > 10000)
                return Result<ProjectArchive>.Fail("Archive project text is too long");
            if (!ValidTechnologies(technologies)) return Result<ProjectArchive>.Fail("Invalid project technologies");

            lock (_lock)
            {
                var arch = new ProjectArchive
                {
                    Title = title.Trim(),
                    Abstract = abstractText != null ? abstractText.Trim() : string.Empty,
                    AcademicYear = academicYear ?? "2023-2024",
                    Department = department ?? "Computer Science",
                    Track = track ?? "General",
                    Technologies = CopyTechnologies(technologies),
                    SupervisorName = supervisorName ?? "Academic Staff",
                    FinalGrade = grade ?? "A"
                };

                _archive[arch.Id] = arch;
                return Result<ProjectArchive>.Ok(arch, "Archive project added successfully");
            }
        }

        public List<ProjectArchive> SearchArchive(string keyword = null, string track = null, string year = null)
        {
            lock (_lock)
            {
                var query = _archive.Values.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    string kw = keyword.Trim().ToLowerInvariant();
                    query = query.Where(a => 
                        a.Title.ToLowerInvariant().Contains(kw) || 
                        a.Abstract.ToLowerInvariant().Contains(kw) ||
                        a.Technologies.Any(t => t.ToLowerInvariant().Contains(kw)));
                }

                if (!string.IsNullOrWhiteSpace(track))
                {
                    string trk = track.Trim().ToLowerInvariant();
                    query = query.Where(a => a.Track.ToLowerInvariant().Contains(trk));
                }

                if (!string.IsNullOrWhiteSpace(year))
                {
                    query = query.Where(a => a.AcademicYear.Equals(year.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                return query.ToList();
            }
        }

        public List<ProjectIdea> SearchIdeas(string keyword = null, string track = null, IdeaStatus? status = null)
        {
            lock (_lock)
            {
                var query = _ideas.Values.AsEnumerable();

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    string kw = keyword.Trim().ToLowerInvariant();
                    query = query.Where(i => 
                        i.Title.ToLowerInvariant().Contains(kw) || 
                        i.Description.ToLowerInvariant().Contains(kw) ||
                        i.Technologies.Any(t => t.ToLowerInvariant().Contains(kw)));
                }

                if (!string.IsNullOrWhiteSpace(track))
                {
                    query = query.Where(i => i.Track.IndexOf(track.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (status.HasValue)
                {
                    query = query.Where(i => i.Status == status.Value);
                }

                return query.OrderByDescending(i => i.CreatedAt).ToList();
            }
        }

        public ProjectIdea GetIdeaById(string ideaId)
        {
            lock (_lock)
            {
                ProjectIdea idea;
                _ideas.TryGetValue(ideaId, out idea);
                return idea;
            }
        }

        public List<IdeaReview> GetReviewsForIdea(string ideaId)
        {
            lock (_lock)
            {
                return _reviews.Where(r => r.ProjectIdeaId.Equals(ideaId, StringComparison.OrdinalIgnoreCase))
                               .OrderByDescending(r => r.ReviewedAt)
                               .ToList();
            }
        }

        public List<SimilarityResult> GetSimilarPastProjects(string ideaId, double minThreshold = 0.30)
        {
            var idea = GetIdeaById(ideaId);
            if (idea == null) return new List<SimilarityResult>();

            return CheckSimilarity(idea.Title, idea.Description, idea.Technologies, idea.Id)
                .Where(r => r.MatchedSourceType == "Archive" && r.SimilarityScore >= minThreshold)
                .ToList();
        }
    }
}
