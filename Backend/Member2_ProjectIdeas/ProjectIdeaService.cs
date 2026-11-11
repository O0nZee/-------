using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public class ProjectIdeaService : IProjectIdeaService
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
            if (string.IsNullOrWhiteSpace(description) || description.Length < 20)
                return Result<ProjectIdea>.Fail("Description must be detailed (at least 20 characters)");

            lock (_lock)
            {
                var idea = new ProjectIdea
                {
                    Title = title.Trim(),
                    Description = description.Trim(),
                    Objectives = objectives != null ? objectives.Trim() : string.Empty,
                    Technologies = technologies ?? new List<string>(),
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
            var results = new List<SimilarityResult>();
            technologies = technologies ?? new List<string>();

            lock (_lock)
            {
                // 1. Compare with current submitted ideas
                foreach (var idea in _ideas.Values)
                {
                    if (excludeIdeaId != null && idea.Id.Equals(excludeIdeaId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    List<string> overlapping;
                    double score = SimilarityEngine.CalculateWeightedSimilarity(
                        title, description, technologies,
                        idea.Title, idea.Description, idea.Technologies,
                        out overlapping);

                    if (score >= 0.25)
                    {
                        results.Add(BuildSimilarityResult(excludeIdeaId, idea.Id, idea.Title, "CurrentIdea", score, overlapping));
                    }
                }

                // 2. Compare with past project archive
                foreach (var arch in _archive.Values)
                {
                    List<string> overlapping;
                    double score = SimilarityEngine.CalculateWeightedSimilarity(
                        title, description, technologies,
                        arch.Title, arch.Abstract, arch.Technologies,
                        out overlapping);

                    if (score >= 0.25)
                    {
                        results.Add(BuildSimilarityResult(excludeIdeaId, arch.Id, arch.Title, "Archive", score, overlapping));
                    }
                }
            }

            return results.OrderByDescending(r => r.SimilarityScore).ToList();
        }

        private static SimilarityResult BuildSimilarityResult(string sourceId, string matchId, string matchTitle, 
            string sourceType, double score, List<string> overlapping)
        {
            SimilarityRiskLevel risk;
            string recommendation;

            if (score >= 0.70)
            {
                risk = SimilarityRiskLevel.High;
                recommendation = "High similarity detected! Idea closely resembles existing project. Rejection or major scope change strongly advised.";
            }
            else if (score >= 0.40)
            {
                risk = SimilarityRiskLevel.Moderate;
                recommendation = "Moderate overlap detected. Students should clearly outline their unique contribution compared to this project.";
            }
            else
            {
                risk = SimilarityRiskLevel.Low;
                recommendation = "Low overlap. Acceptable novelty with minor common terminology.";
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

        public Result<IdeaReview> ReviewIdea(string supervisorId, string ideaId, IdeaStatus decision, string feedback)
        {
            if (string.IsNullOrWhiteSpace(supervisorId)) return Result<IdeaReview>.Fail("Supervisor ID is required");
            if (string.IsNullOrWhiteSpace(ideaId)) return Result<IdeaReview>.Fail("Idea ID is required");
            if (decision == IdeaStatus.PendingReview || decision == IdeaStatus.Draft)
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

            lock (_lock)
            {
                var arch = new ProjectArchive
                {
                    Title = title.Trim(),
                    Abstract = abstractText != null ? abstractText.Trim() : string.Empty,
                    AcademicYear = academicYear ?? "2023-2024",
                    Department = department ?? "Computer Science",
                    Track = track ?? "General",
                    Technologies = technologies ?? new List<string>(),
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
