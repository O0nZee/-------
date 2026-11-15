using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member3_TeamManagement
{
    public class TeamService : ITeamService
    {
        private readonly Dictionary<string, Team> _teams = new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TeamInvitation> _invitations = new Dictionary<string, TeamInvitation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Supervisor> _supervisors = new Dictionary<string, Supervisor>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SupervisorAssignment> _assignments = new List<SupervisorAssignment>();
        
        // Lookup to quickly track which team a student is enrolled in
        private readonly Dictionary<string, string> _studentToTeamId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        public Result<Team> CreateTeam(string leaderStudentId, string leaderName, string teamName, int maxMembers = 5)
        {
            if (string.IsNullOrWhiteSpace(leaderStudentId)) return Result<Team>.Fail("Leader student ID is required");
            if (string.IsNullOrWhiteSpace(teamName)) return Result<Team>.Fail("Team name is required");
            if (maxMembers < 2 || maxMembers > 10) return Result<Team>.Fail("Team size must be between 2 and 10 students");

            lock (_lock)
            {
                if (_studentToTeamId.ContainsKey(leaderStudentId))
                    return Result<Team>.Fail("Student is already registered in a graduation team");

                var team = new Team
                {
                    Name = teamName.Trim(),
                    LeaderStudentId = leaderStudentId.Trim(),
                    MaxMembers = maxMembers,
                    CreatedAt = DateTime.UtcNow
                };

                var leaderMember = new TeamMember
                {
                    TeamId = team.Id,
                    StudentId = leaderStudentId.Trim(),
                    MemberName = !string.IsNullOrWhiteSpace(leaderName) ? leaderName.Trim() : "Leader",
                    RoleInTeam = "Team Leader",
                    JoinedAt = DateTime.UtcNow
                };
                team.Members.Add(leaderMember);

                _teams[team.Id] = team;
                _studentToTeamId[leaderStudentId.Trim()] = team.Id;

                return Result<Team>.Ok(team, "Graduation team created successfully");
            }
        }

        public Result<TeamInvitation> SendInvitation(string inviterStudentId, string teamId, string inviteeStudentId, string proposedRole = "Member")
        {
            if (string.IsNullOrWhiteSpace(inviterStudentId)) return Result<TeamInvitation>.Fail("Inviter ID is required");
            if (string.IsNullOrWhiteSpace(teamId)) return Result<TeamInvitation>.Fail("Team ID is required");
            if (string.IsNullOrWhiteSpace(inviteeStudentId)) return Result<TeamInvitation>.Fail("Invitee student ID is required");
            if (inviterStudentId.Equals(inviteeStudentId, StringComparison.OrdinalIgnoreCase))
                return Result<TeamInvitation>.Fail("You cannot invite yourself to your team");

            lock (_lock)
            {
                if (!_teams.ContainsKey(teamId))
                    return Result<TeamInvitation>.Fail("Team not found");

                var team = _teams[teamId];
                if (!team.LeaderStudentId.Equals(inviterStudentId, StringComparison.OrdinalIgnoreCase))
                    return Result<TeamInvitation>.Fail("Only the team leader can send team invitations");

                if (team.Members.Count >= team.MaxMembers)
                    return Result<TeamInvitation>.Fail("Team has reached its maximum member limit");

                if (_studentToTeamId.ContainsKey(inviteeStudentId))
                    return Result<TeamInvitation>.Fail("Invited student is already enrolled in a team");

                // Check pending invitation already sent
                bool alreadyInvited = _invitations.Values.Any(inv =>
                    inv.TeamId.Equals(teamId, StringComparison.OrdinalIgnoreCase) &&
                    inv.InviteeStudentId.Equals(inviteeStudentId, StringComparison.OrdinalIgnoreCase) &&
                    inv.Status == InvitationStatus.Pending);

                if (alreadyInvited)
                    return Result<TeamInvitation>.Fail("A pending invitation has already been sent to this student");

                var invitation = new TeamInvitation
                {
                    TeamId = teamId,
                    InviterStudentId = inviterStudentId.Trim(),
                    InviteeStudentId = inviteeStudentId.Trim(),
                    ProposedRole = proposedRole ?? "Member",
                    Status = InvitationStatus.Pending,
                    SentAt = DateTime.UtcNow
                };

                _invitations[invitation.Id] = invitation;
                return Result<TeamInvitation>.Ok(invitation, "Team invitation sent successfully");
            }
        }

        public Result RespondToInvitation(string invitationId, string studentId, string studentName, bool accept)
        {
            if (string.IsNullOrWhiteSpace(invitationId)) return Result.Fail("Invitation ID is required");

            lock (_lock)
            {
                if (!_invitations.ContainsKey(invitationId))
                    return Result.Fail("Invitation not found");

                var inv = _invitations[invitationId];
                if (!inv.InviteeStudentId.Equals(studentId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Result.Fail("You are not authorized to respond to this invitation");

                if (inv.Status != InvitationStatus.Pending)
                    return Result.Fail("Invitation is no longer pending");

                if (!accept)
                {
                    inv.Status = InvitationStatus.Rejected;
                    inv.RespondedAt = DateTime.UtcNow;
                    return Result.Ok("Invitation rejected");
                }

                // Student accepted
                if (!_teams.ContainsKey(inv.TeamId))
                    return Result.Fail("The inviting team no longer exists");

                var team = _teams[inv.TeamId];
                if (team.Members.Count >= team.MaxMembers)
                {
                    inv.Status = InvitationStatus.Cancelled;
                    return Result.Fail("Cannot join: Team has reached its maximum member limit");
                }

                if (_studentToTeamId.ContainsKey(studentId.Trim()))
                    return Result.Fail("You are already enrolled in another graduation team");

                var member = new TeamMember
                {
                    TeamId = team.Id,
                    StudentId = studentId.Trim(),
                    MemberName = !string.IsNullOrWhiteSpace(studentName) ? studentName.Trim() : "Member",
                    RoleInTeam = inv.ProposedRole,
                    JoinedAt = DateTime.UtcNow
                };

                team.Members.Add(member);
                _studentToTeamId[studentId.Trim()] = team.Id;

                inv.Status = InvitationStatus.Accepted;
                inv.RespondedAt = DateTime.UtcNow;

                return Result.Ok(string.Format("Successfully joined team '{0}'", team.Name));
            }
        }

        public Result LinkProjectIdea(string teamId, string projectIdeaId)
        {
            if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(projectIdeaId))
                return Result.Fail("Team ID and Project Idea ID are required");

            lock (_lock)
            {
                if (!_teams.ContainsKey(teamId)) return Result.Fail("Team not found");
                var team = _teams[teamId];
                team.ProjectIdeaId = projectIdeaId.Trim();
                return Result.Ok("Project idea successfully linked to team");
            }
        }

        public Result<Supervisor> RegisterSupervisor(string userId, string name, string title, string department,
            string specialization, int maxTeams = 3)
        {
            if (string.IsNullOrWhiteSpace(userId)) return Result<Supervisor>.Fail("User ID is required");
            if (string.IsNullOrWhiteSpace(name)) return Result<Supervisor>.Fail("Supervisor name is required");

            lock (_lock)
            {
                var supervisor = new Supervisor
                {
                    UserId = userId.Trim(),
                    Name = name.Trim(),
                    AcademicTitle = title ?? "Dr.",
                    Department = department ?? "Computer Science",
                    Specialization = specialization ?? "Software Engineering",
                    MaxSupervisedTeams = maxTeams > 0 ? maxTeams : 3,
                    ActiveTeamsCount = 0
                };

                _supervisors[supervisor.Id] = supervisor;
                return Result<Supervisor>.Ok(supervisor, "Supervisor profile registered");
            }
        }

        public Result<SupervisorAssignment> AssignSupervisorToTeam(string adminId, string supervisorId, string teamId)
        {
            if (string.IsNullOrWhiteSpace(adminId)) return Result<SupervisorAssignment>.Fail("Admin ID is required");
            if (string.IsNullOrWhiteSpace(supervisorId)) return Result<SupervisorAssignment>.Fail("Supervisor ID is required");
            if (string.IsNullOrWhiteSpace(teamId)) return Result<SupervisorAssignment>.Fail("Team ID is required");

            lock (_lock)
            {
                if (!_teams.ContainsKey(teamId)) return Result<SupervisorAssignment>.Fail("Team not found");
                if (!_supervisors.ContainsKey(supervisorId)) return Result<SupervisorAssignment>.Fail("Supervisor not found");

                var supervisor = _supervisors[supervisorId];
                var team = _teams[teamId];

                // Check supervisor workload quota!
                if (supervisor.ActiveTeamsCount >= supervisor.MaxSupervisedTeams)
                {
                    return Result<SupervisorAssignment>.Fail(string.Format(
                        "Supervisor quota exceeded! '{0}' has reached maximum supervision capacity ({1}/{2} teams).",
                        supervisor.Name, supervisor.ActiveTeamsCount, supervisor.MaxSupervisedTeams));
                }

                // If team already had another supervisor, decrement old supervisor's active count
                if (!string.IsNullOrEmpty(team.AssignedSupervisorId) && _supervisors.ContainsKey(team.AssignedSupervisorId))
                {
                    _supervisors[team.AssignedSupervisorId].ActiveTeamsCount = Math.Max(0, _supervisors[team.AssignedSupervisorId].ActiveTeamsCount - 1);
                }

                team.AssignedSupervisorId = supervisor.Id;
                supervisor.ActiveTeamsCount++;

                var assignment = new SupervisorAssignment
                {
                    TeamId = team.Id,
                    SupervisorId = supervisor.Id,
                    AssignedByAdminId = adminId.Trim(),
                    AssignedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _assignments.Add(assignment);
                return Result<SupervisorAssignment>.Ok(assignment, string.Format("Supervisor '{0}' successfully assigned to team '{1}'", supervisor.Name, team.Name));
            }
        }

        public Team GetTeamById(string teamId)
        {
            lock (_lock)
            {
                Team team;
                _teams.TryGetValue(teamId, out team);
                return team;
            }
        }

        public Team GetTeamByStudentId(string studentId)
        {
            lock (_lock)
            {
                string teamId;
                if (_studentToTeamId.TryGetValue(studentId.Trim(), out teamId))
                {
                    return GetTeamById(teamId);
                }
                return null;
            }
        }

        public List<Team> GetTeamsBySupervisor(string supervisorId)
        {
            lock (_lock)
            {
                return _teams.Values
                    .Where(t => !string.IsNullOrEmpty(t.AssignedSupervisorId) && 
                                t.AssignedSupervisorId.Equals(supervisorId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        public List<TeamInvitation> GetPendingInvitationsForStudent(string studentId)
        {
            lock (_lock)
            {
                return _invitations.Values
                    .Where(i => i.InviteeStudentId.Equals(studentId.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                i.Status == InvitationStatus.Pending)
                    .OrderByDescending(i => i.SentAt)
                    .ToList();
            }
        }

        public List<Supervisor> GetAllSupervisors()
        {
            lock (_lock)
            {
                return _supervisors.Values.OrderBy(s => s.Name).ToList();
            }
        }

        public List<Team> GetAllTeams()
        {
            lock (_lock)
            {
                return _teams.Values.OrderBy(t => t.Name).ToList();
            }
        }
    }
}
