using System;
using System.Collections.Generic;
using GraduationPlatform.Common;
using GraduationPlatform.Member2_ProjectIdeas;

namespace GraduationPlatform.Tests
{
    public static class Member2_Tests
    {
        public static void RunTests()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(">>> RUNNING TESTS: Member 2 - Ideas & AI Search <<<");
            Console.WriteLine("==================================================");

            int passed = 0;
            int total = 0;

            Test("Submit valid project idea succeeds", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();
                var result = ideaService.SubmitIdea(
                    "student_101",
                    "AI Automated Medical Diagnostics Assistant",
                    "A deep learning web portal that analyzes clinical laboratory test results and flags abnormal medical markers using neural networks.",
                    "1. Train classifier on 50k lab tests. 2. Provide interactive dashboard.",
                    new List<string> { "Python", "FastAPI", "PyTorch", "React" },
                    "Healthcare & AI",
                    "team_alpha");

                Assert(result.IsSuccess, "Submission should succeed");
                Assert(result.Data != null, "Idea data should exist");
                Assert(result.Data.Status == IdeaStatus.PendingReview, "Initial status must be PendingReview");
                Assert(result.Data.Title.Contains("Medical Diagnostics"), "Title matches");
            });

            Test("Submission fails with invalid or brief description", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();
                var shortDesc = ideaService.SubmitIdea("student_101", "Short Title", "Too brief", "Obj", new List<string>(), "AI");
                Assert(!shortDesc.IsSuccess, "Short description should fail validation");

                var missingTitle = ideaService.SubmitIdea("student_101", "", "A valid long description that exceeds twenty characters easily.", "Obj", new List<string>(), "AI");
                Assert(!missingTitle.IsSuccess, "Empty title should fail validation");
            });

            Test("Supervisor reviews idea and approves it", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();
                var submit = ideaService.SubmitIdea(
                    "student_202",
                    "Autonomous Delivery Drone for Campus",
                    "A drone system that uses computer vision to deliver documents between campus buildings safely.",
                    "Autonomous navigation on college campus",
                    new List<string> { "C++", "ROS2", "OpenCV" },
                    "Robotics");

                string ideaId = submit.Data.Id;
                var review = ideaService.ReviewIdea("prof_ahmed", ideaId, IdeaStatus.Approved, "Excellent proposal and feasible timeline.");

                Assert(review.IsSuccess, "Review should succeed");
                Assert(review.Data.Decision == IdeaStatus.Approved, "Decision is Approved");

                var updatedIdea = ideaService.GetIdeaById(ideaId);
                Assert(updatedIdea.Status == IdeaStatus.Approved, "Idea status updated in database");

                var reviews = ideaService.GetReviewsForIdea(ideaId);
                Assert(reviews.Count == 1, "Review history recorded");
                Assert(reviews[0].FeedbackComments.Contains("Excellent proposal"), "Feedback text stored");
            });

            Test("Supervisor requests modification with required edits", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();
                var submit = ideaService.SubmitIdea(
                    "student_303",
                    "E-Commerce Microservices Platform",
                    "Building a multi-tenant shop with distributed databases and high availability using Docker.",
                    "Scalable store backend",
                    new List<string> { "Go", "Docker", "PostgreSQL" },
                    "Web Architecture");

                string ideaId = submit.Data.Id;
                var review = ideaService.ReviewIdea("prof_sami", ideaId, IdeaStatus.ModificationRequested, "Please add cybersecurity and payment gateway details.");

                Assert(review.IsSuccess, "Review should succeed");
                Assert(review.Data.Decision == IdeaStatus.ModificationRequested, "Decision matches");

                var updatedIdea = ideaService.GetIdeaById(ideaId);
                Assert(updatedIdea.Status == IdeaStatus.ModificationRequested, "Idea status changed to ModificationRequested");
            });

            Test("Smart Search across project archive by keyword and track", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();

                // Search by keyword "facial" (from seeded Smart Attendance archive)
                var resultsFace = ideaService.SearchArchive("facial");
                Assert(resultsFace.Count >= 1, "Should find attendance project");
                Assert(resultsFace[0].Title.Contains("Smart Attendance"), "Project title verified");

                // Search by track "Blockchain & Security"
                var resultsBlock = ideaService.SearchArchive(null, "Blockchain & Security");
                Assert(resultsBlock.Count >= 1, "Should find blockchain project");
                Assert(resultsBlock[0].Technologies.Contains("Solidity"), "Technologies match");
            });

            Test("AI Similarity Engine detects duplicate idea (Plagiarism Alert)", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();

                // Submit an idea very similar to the archived: "Smart Attendance System using Facial Recognition"
                string copiedTitle = "Automated Smart Attendance Platform with Facial Recognition";
                string copiedDesc = "Automated classroom student attendance monitoring using deep learning CNN and live camera streams for universities.";
                var copiedTech = new List<string> { "Python", "OpenCV", "TensorFlow", "React" };

                var simResults = ideaService.CheckSimilarity(copiedTitle, copiedDesc, copiedTech);

                Assert(simResults.Count > 0, "Similarity engine must detect match");
                var topMatch = simResults[0];

                Console.WriteLine(string.Format("   [AI Similarity Engine Alert]: Matched '{0}' with score {1:P1} (Risk: {2})",
                    topMatch.MatchedTitle, topMatch.SimilarityScore, topMatch.RiskLevel));

                Assert(topMatch.SimilarityScore >= 0.70, "Similarity score must be >= 70% for duplicate idea");
                Assert(topMatch.RiskLevel == SimilarityRiskLevel.High, "Risk level must be High");
                Assert(topMatch.OverlappingKeywords.Count > 0, "Overlapping keywords must be captured");
            });

            Test("AI Similarity Engine approves completely novel idea (Low Risk)", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();

                string novelTitle = "Quantum Computing Simulation for Satellite Orbital Trajectories";
                string novelDesc = "Simulating gravitational perturbation matrices using Qiskit quantum circuit algorithms for aerospace orbits.";
                var novelTech = new List<string> { "Qiskit", "Python", "NumPy" };

                var simResults = ideaService.CheckSimilarity(novelTitle, novelDesc, novelTech);

                if (simResults.Count > 0)
                {
                    Assert(simResults[0].SimilarityScore < 0.40, "Novel idea should have low similarity score (<40%)");
                    Assert(simResults[0].RiskLevel == SimilarityRiskLevel.Low, "Novel idea risk must be Low");
                }
            });

            Test("Suggest similar past projects when idea is submitted", ref passed, ref total, () =>
            {
                var ideaService = new ProjectIdeaService();
                var sub = ideaService.SubmitIdea(
                    "st_404",
                    "Telehealth Video Consultation for Remote Clinics",
                    "A secure telehealth consultation platform allowing doctors to review patient history and prescribe medication over video calls.",
                    "Connect doctors with remote clinics",
                    new List<string> { "C#", "ASP.NET Core", "WebRTC" },
                    "Healthcare");

                var pastSimilar = ideaService.GetSimilarPastProjects(sub.Data.Id, 0.35);
                Assert(pastSimilar.Count >= 1, "Should find the archived Telemedicine project as recommendation");
                Assert(pastSimilar[0].MatchedTitle.Contains("Telemedicine"), "Recommended project matches");
            });

            Console.WriteLine(string.Format("\n[Member 2 Results]: {0}/{1} tests PASSED ({2:P0})\n", passed, total, (double)passed / total));
            if (passed != total) throw new Exception("Member 2 Unit Tests Failed!");
        }

        private static void Test(string testName, ref int passed, ref int total, Action action)
        {
            total++;
            try
            {
                action();
                passed++;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format(" [PASS] {0}", testName));
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format(" [FAIL] {0}: {1}", testName, ex.Message));
                Console.ResetColor();
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion Failed: " + message);
        }

        public static void Main(string[] args)
        {
            RunTests();
        }
    }
}
