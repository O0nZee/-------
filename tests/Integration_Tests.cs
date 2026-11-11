using System;
using System.Collections.Generic;
using GraduationPlatform.Common;
using GraduationPlatform.Member1_UserManagement;
using GraduationPlatform.Member2_ProjectIdeas;
using GraduationPlatform.Member3_TeamManagement;
using GraduationPlatform.Member4_ProjectTracking;

namespace GraduationPlatform.Tests
{
    public static class Integration_Tests
    {
        public static void RunTests()
        {
            Console.WriteLine("======================================================================");
            Console.WriteLine(">>> RUNNING FULL INTEGRATION TESTS: All 4 Modules Connected (E2E) <<<");
            Console.WriteLine("======================================================================");

            int passed = 0;
            int total = 0;

            Test("Full Graduation Project Lifecycle across Members 1, 2, 3, and 4", ref passed, ref total, () =>
            {
                // Instantiate the 4 core module services
                var authService = new AuthService();
                var ideaService = new ProjectIdeaService();
                var teamService = new TeamService();
                var trackingService = new TrackingService();

                Console.WriteLine("\n[STAGE 1]: Member 1 - User Management & Authentication");
                // 1. Register Admin
                var admin = authService.Register("admin_dean", "dean@alexu.edu.eg", "DeanPass2026", UserRoleType.Admin,
                    "Prof. Hassan Dean", "Faculty of Engineering", "ADM-01").Data;
                Assert(admin != null, "Admin registered");

                // 2. Register Supervisor
                var doctor = authService.Register("dr_mostafa", "mostafa@alexu.edu.eg", "ProfDocPass1", UserRoleType.Supervisor,
                    "Dr. Mostafa Kamel", "Computer Science", "FAC-014").Data;
                Assert(doctor != null, "Supervisor registered");

                // Register supervisor in Member 3 system
                var supervisorProfile = teamService.RegisterSupervisor(doctor.Id, doctor.Profile.FullName,
                    "Assoc. Prof.", doctor.Profile.Department, "IoT & Smart Systems", 3).Data;
                Assert(supervisorProfile != null, "Supervisor registered in Member 3");

                // 3. Register Students
                var studentAhmed = authService.Register("ahmed_teamlead", "ahmed.lead@alexu.edu.eg", "AhmedPass1", UserRoleType.Student,
                    "Ahmed Mansour", "Computer Science", "ST-2026-101").Data;
                var studentKarim = authService.Register("karim_dev", "karim.dev@alexu.edu.eg", "KarimPass2", UserRoleType.Student,
                    "Karim Nabil", "Computer Science", "ST-2026-102").Data;
                var studentMona = authService.Register("mona_ai", "mona.ai@alexu.edu.eg", "MonaPass3", UserRoleType.Student,
                    "Mona Shawky", "Computer Science", "ST-2026-103").Data;

                Assert(studentAhmed != null && studentKarim != null && studentMona != null, "All 3 students registered");

                // Verify permissions
                Assert(authService.HasPermission(studentAhmed.Id, "idea.submit"), "Ahmed can submit ideas");
                Assert(authService.HasPermission(doctor.Id, "idea.review"), "Dr. Mostafa can review ideas");
                Assert(authService.HasPermission(admin.Id, "supervisor.assign"), "Admin can assign supervisors");
                Console.WriteLine(" -> Auth & RBAC setup complete for all 3 roles.");

                Console.WriteLine("\n[STAGE 2]: Member 2 - Project Idea Proposal & AI Similarity Check");
                string ideaTitle = "Smart IoT Microgrid Energy Optimization for Campus";
                string ideaDesc = "Deploying LoRaWAN power metering sensors with predictive deep learning models to dynamically balance energy loads across campus buildings.";
                var ideaTech = new List<string> { "IoT", "Python", "LoRaWAN", "PyTorch", "FastAPI", "React" };

                // AI similarity check
                var simCheck = ideaService.CheckSimilarity(ideaTitle, ideaDesc, ideaTech);
                bool hasHighRisk = simCheck.Exists(s => s.RiskLevel == SimilarityRiskLevel.High);
                Assert(!hasHighRisk, "Idea must not trigger high-risk plagiarism against past projects");

                var submitIdeaRes = ideaService.SubmitIdea(studentAhmed.Id, ideaTitle, ideaDesc,
                    "1. Real-time smart sensor readings. 2. AI peak-shaving forecasts.",
                    ideaTech, "IoT & Smart Systems");
                Assert(submitIdeaRes.IsSuccess, "Idea submission succeeds");
                var projectIdea = submitIdeaRes.Data;
                Assert(projectIdea.Status == IdeaStatus.PendingReview, "Idea is PendingReview");

                // Supervisor reviews and approves
                var reviewRes = ideaService.ReviewIdea(doctor.Id, projectIdea.Id, IdeaStatus.Approved,
                    "Impressive scope and high novelty for campus sustainability. Approved!");
                Assert(reviewRes.IsSuccess, "Supervisor review succeeds");
                Assert(ideaService.GetIdeaById(projectIdea.Id).Status == IdeaStatus.Approved, "Idea is officially Approved");
                Console.WriteLine(" -> Idea submitted, AI checked, and approved by supervisor.");

                Console.WriteLine("\n[STAGE 3]: Member 3 - Team Formation & Supervisor Assignment");
                // Ahmed creates team
                var teamRes = teamService.CreateTeam(studentAhmed.Id, studentAhmed.Profile.FullName, "EnergyGrid Pioneers", 4);
                Assert(teamRes.IsSuccess, "Team creation succeeds");
                var team = teamRes.Data;

                // Ahmed invites Karim and Mona
                var inv1 = teamService.SendInvitation(studentAhmed.Id, team.Id, studentKarim.Id, "Embedded & IoT Engineer").Data;
                var inv2 = teamService.SendInvitation(studentAhmed.Id, team.Id, studentMona.Id, "AI & Machine Learning Engineer").Data;

                // Karim and Mona accept
                teamService.RespondToInvitation(inv1.Id, studentKarim.Id, studentKarim.Profile.FullName, true);
                teamService.RespondToInvitation(inv2.Id, studentMona.Id, studentMona.Profile.FullName, true);

                var teamWithMembers = teamService.GetTeamById(team.Id);
                Assert(teamWithMembers.Members.Count == 3, "Team has 3 active members");
                Assert(teamWithMembers.Members.Exists(m => m.StudentId == studentAhmed.Id && m.RoleInTeam == "Team Leader"), "Ahmed is Leader");
                Assert(teamWithMembers.Members.Exists(m => m.StudentId == studentKarim.Id && m.RoleInTeam == "Embedded & IoT Engineer"), "Karim is IoT Engineer");
                Assert(teamWithMembers.Members.Exists(m => m.StudentId == studentMona.Id && m.RoleInTeam == "AI & Machine Learning Engineer"), "Mona is AI Engineer");

                // Link approved idea to team
                teamService.LinkProjectIdea(team.Id, projectIdea.Id);
                Assert(teamService.GetTeamById(team.Id).ProjectIdeaId == projectIdea.Id, "Team linked to approved idea");

                // Admin assigns Dr. Mostafa as supervisor
                var assignRes = teamService.AssignSupervisorToTeam(admin.Id, supervisorProfile.Id, team.Id);
                Assert(assignRes.IsSuccess, "Admin successfully assigned supervisor to team");
                Assert(supervisorProfile.ActiveTeamsCount == 1, "Supervisor active workload incremented to 1");
                Console.WriteLine(" -> Team formed (3 members), linked to idea, and supervisor assigned.");

                Console.WriteLine("\n[STAGE 4]: Member 4 - Project Milestones, Tasks & Progress Tracking");
                // Initialize default 4 milestones
                var initMilestones = trackingService.InitializeDefaultMilestones(team.Id);
                Assert(initMilestones.IsSuccess, "Default milestones initialized");
                var milestones = trackingService.GetMilestones(team.Id);
                Assert(milestones.Count == 4, "4 milestones exist");

                // Create Tasks for Phase 1 & Phase 2
                var task1 = trackingService.CreateTask(team.Id, milestones[0].Id, "Write SRS Document",
                    "Complete software requirements specification", TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(7)).Data;
                var task2 = trackingService.CreateTask(team.Id, milestones[0].Id, "Review Academic Literature",
                    "Survey papers on microgrid optimization", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(10)).Data;
                var task3 = trackingService.CreateTask(team.Id, milestones[1].Id, "Design Sensor Circuit Hardware",
                    "Schematic for ESP32 and current sensors", TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(20)).Data;
                var task4 = trackingService.CreateTask(team.Id, milestones[1].Id, "Train Neural Load Forecaster",
                    "Train baseline LSTM model on historical dataset", TaskPriority.Urgent, DateTime.UtcNow, DateTime.UtcNow.AddDays(25)).Data;

                // Assign tasks to respective team members
                trackingService.AssignTaskToMember(task1.Id, studentAhmed.Id, studentAhmed.Profile.FullName);
                trackingService.AssignTaskToMember(task2.Id, studentAhmed.Id, studentAhmed.Profile.FullName);
                trackingService.AssignTaskToMember(task3.Id, studentKarim.Id, studentKarim.Profile.FullName);
                trackingService.AssignTaskToMember(task4.Id, studentMona.Id, studentMona.Profile.FullName);

                // Team members work and advance tasks
                trackingService.UpdateTaskStatus(task1.Id, TaskItemStatus.Completed);
                trackingService.UpdateTaskStatus(task2.Id, TaskItemStatus.Completed);
                trackingService.UpdateTaskStatus(task3.Id, TaskItemStatus.InProgress);

                // Kanban board inspection
                var kanban = trackingService.GetKanbanBoard(team.Id);
                Assert(kanban.Completed.Count == 2, "2 tasks in Completed column");
                Assert(kanban.InProgress.Count == 1, "1 task in InProgress column");
                Assert(kanban.ToDo.Count == 1, "1 task in ToDo column");

                // Timeline inspection (2 completed out of 4 = 50.0%)
                var timeline = trackingService.GetProjectTimeline(team.Id);
                Assert(timeline.TotalTasks == 4, "Total tasks is 4");
                Assert(timeline.CompletedTasks == 2, "Completed tasks is 2");
                Assert(Math.Abs(timeline.CompletionPercentage - 50.0) < 0.1, "Completion is exactly 50.0%");

                // Phase 1 milestone auto-completed because task1 & task2 both completed!
                var updatedMilestones = trackingService.GetMilestones(team.Id);
                Assert(updatedMilestones[0].IsCompleted, "Phase 1 milestone is marked Completed");
                Assert(!updatedMilestones[1].IsCompleted, "Phase 2 is still In Progress");

                Console.WriteLine(" -> Milestones generated, tasks assigned to members, Kanban board verified, progress at 50.0%.");
                Console.WriteLine(" -> Full integration workflow passed seamlessly with 0 errors!");
            });

            Console.WriteLine(string.Format("\n[Integration Results]: {0}/{1} tests PASSED ({2:P0})\n", passed, total, (double)passed / total));
            if (passed != total) throw new Exception("Integration Tests Failed!");
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
