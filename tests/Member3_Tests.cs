using System;
using System.Collections.Generic;
using GraduationPlatform.Common;
using GraduationPlatform.Member3_TeamManagement;

namespace GraduationPlatform.Tests
{
    public static class Member3_Tests
    {
        public static void RunTests()
        {
            Console.WriteLine("====================================================");
            Console.WriteLine(">>> RUNNING TESTS: Member 3 - Teams & Supervisors <<<");
            Console.WriteLine("====================================================");

            int passed = 0;
            int total = 0;

            Test("Student creates new team with leader membership", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var result = teamService.CreateTeam("student_1", "Ali Hassan", "CyberGuard Team", 4);

                Assert(result.IsSuccess, "Team creation should succeed");
                Assert(result.Data != null, "Team should not be null");
                Assert(result.Data.Name == "CyberGuard Team", "Team name match");
                Assert(result.Data.Members.Count == 1, "Leader must be added as first member");
                Assert(result.Data.Members[0].RoleInTeam == "Team Leader", "Role is Team Leader");
            });

            Test("Student cannot create multiple teams", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                teamService.CreateTeam("student_2", "Bassem Adel", "Team Beta", 4);

                var duplicateTeam = teamService.CreateTeam("student_2", "Bassem Adel", "Team Beta 2", 4);
                Assert(!duplicateTeam.IsSuccess, "Student already in a team cannot create another");
            });

            Test("Team leader sends invitation to another student", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var team = teamService.CreateTeam("leader_1", "Ziad Tarek", "AI Innovators", 4).Data;

                var inviteRes = teamService.SendInvitation("leader_1", team.Id, "student_peer", "Backend Developer");
                Assert(inviteRes.IsSuccess, "Sending invitation should succeed");
                Assert(inviteRes.Data.Status == InvitationStatus.Pending, "Invitation status is Pending");
                Assert(inviteRes.Data.ProposedRole == "Backend Developer", "Role match");

                var pending = teamService.GetPendingInvitationsForStudent("student_peer");
                Assert(pending.Count == 1, "Invitee should have 1 pending invitation");
            });

            Test("Non-leader cannot send team invitations", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var team = teamService.CreateTeam("leader_real", "Real Leader", "RoboTeam", 4).Data;

                var unauthorizedInvite = teamService.SendInvitation("stranger_id", team.Id, "student_x", "Member");
                Assert(!unauthorizedInvite.IsSuccess, "Only leader can send invitations");
            });

            Test("Invitee accepts invitation and joins team successfully", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var team = teamService.CreateTeam("lead_2", "Nour Hany", "SmartHealth", 3).Data;

                var invite = teamService.SendInvitation("lead_2", team.Id, "student_accepted", "Data Scientist").Data;
                var acceptRes = teamService.RespondToInvitation(invite.Id, "student_accepted", "Hesham Salem", true);

                Assert(acceptRes.IsSuccess, "Accepting invitation should succeed");

                var updatedTeam = teamService.GetTeamById(team.Id);
                Assert(updatedTeam.Members.Count == 2, "Team member count should now be 2");
                Assert(updatedTeam.Members[1].MemberName == "Hesham Salem", "Member name stored");
                Assert(updatedTeam.Members[1].RoleInTeam == "Data Scientist", "Role matches invitation");

                // Verify student is now recognized as enrolled in this team
                var enrolledTeam = teamService.GetTeamByStudentId("student_accepted");
                Assert(enrolledTeam != null && enrolledTeam.Id == team.Id, "Student is enrolled in team");
            });

            Test("Invitee rejects invitation", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var team = teamService.CreateTeam("lead_3", "Salma", "Team Gamma", 3).Data;

                var invite = teamService.SendInvitation("lead_3", team.Id, "student_rejected", "Designer").Data;
                var rejectRes = teamService.RespondToInvitation(invite.Id, "student_rejected", "Rejected Student", false);

                Assert(rejectRes.IsSuccess, "Rejection processed");
                var updatedTeam = teamService.GetTeamById(team.Id);
                Assert(updatedTeam.Members.Count == 1, "Team member count remains 1");
            });

            Test("Team maximum capacity limit is strictly enforced", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                // Team with max 2 members
                var team = teamService.CreateTeam("leader_cap", "Leader Cap", "Small Team", 2).Data;

                var inv1 = teamService.SendInvitation("leader_cap", team.Id, "member_2", "Member").Data;
                teamService.RespondToInvitation(inv1.Id, "member_2", "Member Two", true);

                // Now team has 2/2 members!
                var invOver = teamService.SendInvitation("leader_cap", team.Id, "member_3", "Member");
                Assert(!invOver.IsSuccess, "Cannot send invitation when team is at full capacity");
            });

            Test("Link approved project idea to team", ref passed, ref total, () =>
            {
                var teamService = new TeamService();
                var team = teamService.CreateTeam("lead_idea", "Idea Leader", "Idea Team", 4).Data;

                var linkRes = teamService.LinkProjectIdea(team.Id, "IDEA-9999");
                Assert(linkRes.IsSuccess, "Linking idea succeeds");

                var updated = teamService.GetTeamById(team.Id);
                Assert(updated.ProjectIdeaId == "IDEA-9999", "Idea ID matches");
            });

            Test("Supervisor registration and quota-enforced assignment", ref passed, ref total, () =>
            {
                var teamService = new TeamService();

                // Register supervisor with max 2 teams quota
                var sup = teamService.RegisterSupervisor("doc_user_1", "Dr. Yasser Fathy", "Assoc. Prof.", "Computer Science", "Cloud & Security", 2).Data;
                Assert(sup.MaxSupervisedTeams == 2, "Quota is 2 teams");
                Assert(sup.ActiveTeamsCount == 0, "Initial active teams is 0");

                // Create 3 teams
                var teamA = teamService.CreateTeam("lead_a", "Lead A", "Team Alpha", 4).Data;
                var teamB = teamService.CreateTeam("lead_b", "Lead B", "Team Bravo", 4).Data;
                var teamC = teamService.CreateTeam("lead_c", "Lead C", "Team Charlie", 4).Data;

                // Assign Team A -> Success (1/2)
                var assignA = teamService.AssignSupervisorToTeam("admin_001", sup.Id, teamA.Id);
                Assert(assignA.IsSuccess, "First assignment succeeds");

                // Assign Team B -> Success (2/2)
                var assignB = teamService.AssignSupervisorToTeam("admin_001", sup.Id, teamB.Id);
                Assert(assignB.IsSuccess, "Second assignment succeeds");

                // Assign Team C -> MUST FAIL! Quota exceeded
                var assignC = teamService.AssignSupervisorToTeam("admin_001", sup.Id, teamC.Id);
                Assert(!assignC.IsSuccess, "Third assignment MUST fail due to quota limit");
                Assert(assignC.Message.Contains("quota exceeded"), "Error message specifies quota exceeded");

                // Verify supervisor sees his 2 teams
                var supervisedTeams = teamService.GetTeamsBySupervisor(sup.Id);
                Assert(supervisedTeams.Count == 2, "Supervisor has exactly 2 supervised teams");
            });

            Console.WriteLine(string.Format("\n[Member 3 Results]: {0}/{1} tests PASSED ({2:P0})\n", passed, total, (double)passed / total));
            if (passed != total) throw new Exception("Member 3 Unit Tests Failed!");
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
