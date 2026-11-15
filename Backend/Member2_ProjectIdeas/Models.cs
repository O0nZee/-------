using System;
using System.Collections.Generic;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public enum IdeaStatus
    {
        Draft = 0,
        PendingReview = 1,
        Approved = 2,
        Rejected = 3,
        ModificationRequested = 4
    }

    public enum SimilarityRiskLevel
    {
        Low = 1,        // Score < 40%
        Moderate = 2,   // Score 40% - 69%
        High = 3        // Score >= 70% (Potential Plagiarism/Duplication)
    }

    public class ProjectIdea
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Objectives { get; set; }
        public List<string> Technologies { get; set; }
        public string Track { get; set; } // e.g. "AI", "Web", "Cybersecurity", "Mobile", "IoT"
        public string SubmitterStudentId { get; set; }
        public string TeamId { get; set; }
        public IdeaStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ProjectIdea()
        {
            Id = Guid.NewGuid().ToString("N");
            Technologies = new List<string>();
            Status = IdeaStatus.PendingReview;
            CreatedAt = DateTime.UtcNow;
        }
    }

    public class ProjectArchive
    {
        public string TeamId { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Abstract { get; set; }
        public string AcademicYear { get; set; } // e.g. "2023-2024"
        public string Department { get; set; }
        public string Track { get; set; }
        public List<string> Technologies { get; set; }
        public string SupervisorName { get; set; }
        public string FinalGrade { get; set; } // e.g. "A+", "Excellent"

        public ProjectArchive()
        {
            Id = Guid.NewGuid().ToString("N");
            Technologies = new List<string>();
        }
    }

    public class IdeaReview
    {
        public string Id { get; set; }
        public string ProjectIdeaId { get; set; }
        public string SupervisorId { get; set; }
        public IdeaStatus Decision { get; set; } // Approved, Rejected, ModificationRequested
        public string FeedbackComments { get; set; }
        public DateTime ReviewedAt { get; set; }

        public IdeaReview()
        {
            Id = Guid.NewGuid().ToString("N");
            ReviewedAt = DateTime.UtcNow;
        }
    }

    public class SimilarityResult
    {
        public string SourceIdeaId { get; set; }
        public string MatchedItemId { get; set; }
        public string MatchedTitle { get; set; }
        public string MatchedSourceType { get; set; } // "CurrentIdea" or "Archive"
        public double SimilarityScore { get; set; }   // 0.0 to 1.0 (e.g. 0.85 = 85%)
        public SimilarityRiskLevel RiskLevel { get; set; }
        public List<string> OverlappingKeywords { get; set; }
        public string Recommendation { get; set; }

        public SimilarityResult()
        {
            OverlappingKeywords = new List<string>();
        }
    }

    public class TrackRecommendation
    {
        public string Track { get; set; } = string.Empty;
        public string ArabicName { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<string> MatchedKeywords { get; set; } = new List<string>();
    }

    public class TrackClassificationResult
    {
        public string PrimaryTrack { get; set; } = string.Empty;
        public List<TrackRecommendation> RecommendedTracks { get; set; } = new List<TrackRecommendation>();
        public string Engine { get; set; } = string.Empty;
    }

    public class IdeaQualityEvaluationResult
    {
        public int Score { get; set; }
        public string Grade { get; set; } = string.Empty;
        public List<string> Strengths { get; set; } = new List<string>();
        public List<string> Recommendations { get; set; } = new List<string>();
        public string Engine { get; set; } = string.Empty;
    }
}
