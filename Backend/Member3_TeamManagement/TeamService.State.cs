using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member3_TeamManagement
{
    public partial class TeamService
    {
        public List<TeamInvitation> ExportInvitations() { lock (_lock) return _invitations.Values.ToList(); }
        public List<SupervisorAssignment> ExportAssignments() { lock (_lock) return _assignments.ToList(); }
        public void ImportState(IEnumerable<Team> teams, IEnumerable<TeamInvitation> invitations, IEnumerable<Supervisor> supervisors, IEnumerable<SupervisorAssignment> assignments)
        {
            lock (_lock)
            {
                _teams.Clear(); _invitations.Clear(); _supervisors.Clear(); _assignments.Clear(); _studentToTeamId.Clear();
                foreach (var team in teams)
                {
                    _teams.Add(team.Id, team);
                    foreach (var member in team.Members) _studentToTeamId.Add(member.StudentId, team.Id);
                }
                foreach (var invitation in invitations) _invitations.Add(invitation.Id, invitation);
                foreach (var supervisor in supervisors) _supervisors.Add(supervisor.Id, supervisor);
                _assignments.AddRange(assignments);
            }
        }
        public TeamInvitation GetInvitation(string id) { lock (_lock) { TeamInvitation invitation; _invitations.TryGetValue(id, out invitation); return invitation; } }
        public Supervisor GetSupervisor(string id) { lock (_lock) { Supervisor supervisor; _supervisors.TryGetValue(id, out supervisor); return supervisor; } }
        public Supervisor GetSupervisorByUser(string userId) { lock (_lock) return _supervisors.Values.FirstOrDefault(s => s.UserId == userId); }
        public Result CancelInvitation(string id)
        {
            lock (_lock)
            {
                var invitation = GetInvitation(id);
                if (invitation == null) return Result.Fail("Invitation not found");
                if (invitation.Status != InvitationStatus.Pending) return Result.Fail("Invitation is no longer pending");
                invitation.Status = InvitationStatus.Cancelled; invitation.RespondedAt = DateTime.UtcNow;
                return Result.Ok("Invitation cancelled");
            }
        }
        public Result RemoveMember(string teamId, string studentId)
        {
            lock (_lock)
            {
                var team = GetTeamById(teamId);
                if (team == null) return Result.Fail("Team not found");
                if (team.IsArchived) return Result.Fail("Completed teams cannot be changed");
                if (team.LeaderStudentId == studentId) return Result.Fail("Transfer leadership before removing the team leader");
                var member = team.Members.FirstOrDefault(m => m.StudentId == studentId);
                if (member == null) return Result.Fail("Student is not a member of this team");
                team.Members.Remove(member); _studentToTeamId.Remove(studentId);
                return Result.Ok("Member removed");
            }
        }
        public Result UpdateTeam(string teamId, string name, string leaderId)
        {
            lock (_lock)
            {
                var team = GetTeamById(teamId);
                if (team == null) return Result.Fail("Team not found");
                if (team.IsArchived) return Result.Fail("Completed teams cannot be changed");
                if (!string.IsNullOrWhiteSpace(name)) team.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(leaderId) && leaderId != team.LeaderStudentId)
                {
                    var newLeader = team.Members.FirstOrDefault(m => m.StudentId == leaderId);
                    if (newLeader == null) return Result.Fail("New leader must be a member of this team");
                    var oldLeader = team.Members.First(m => m.StudentId == team.LeaderStudentId);
                    oldLeader.RoleInTeam = "Member"; newLeader.RoleInTeam = "Team Leader"; team.LeaderStudentId = leaderId;
                }
                return Result.Ok("Team updated");
            }
        }
        public Result UpdateSupervisor(string supervisorId, int maxTeams, string specialization)
        {
            lock (_lock)
            {
                var supervisor = GetSupervisor(supervisorId);
                if (supervisor == null) return Result.Fail("Supervisor not found");
                if (maxTeams < 1 || maxTeams > 20 || maxTeams < supervisor.ActiveTeamsCount) return Result.Fail("Quota cannot be below the current workload");
                supervisor.MaxSupervisedTeams = maxTeams;
                if (specialization != null) supervisor.Specialization = specialization.Trim();
                return Result.Ok("Supervisor updated");
            }
        }
        public Result UnassignSupervisor(string teamId)
        {
            lock (_lock)
            {
                var team = GetTeamById(teamId);
                if (team == null) return Result.Fail("Team not found");
                if (team.IsArchived) return Result.Fail("Completed teams cannot be changed");
                ReleaseSupervisor(team);
                team.AssignedSupervisorId = null;
                return Result.Ok("Supervisor unassigned");
            }
        }
        public Result ArchiveTeam(string teamId)
        {
            lock (_lock)
            {
                var team = GetTeamById(teamId);
                if (team == null) return Result.Fail("Team not found");
                if (team.IsArchived) return Result.Fail("Team is already archived");
                ReleaseSupervisor(team);
                team.IsArchived = true; team.CompletedAt = DateTime.UtcNow;
                foreach (var invitation in _invitations.Values.Where(i => i.TeamId == teamId && i.Status == InvitationStatus.Pending))
                { invitation.Status = InvitationStatus.Cancelled; invitation.RespondedAt = DateTime.UtcNow; }
                return Result.Ok("Team archived");
            }
        }
        private void ReleaseSupervisor(Team team)
        {
            if (team.AssignedSupervisorId != null && _supervisors.ContainsKey(team.AssignedSupervisorId))
                _supervisors[team.AssignedSupervisorId].ActiveTeamsCount = Math.Max(0, _supervisors[team.AssignedSupervisorId].ActiveTeamsCount - 1);
            foreach (var assignment in _assignments.Where(a => a.TeamId == team.Id && a.IsActive)) assignment.IsActive = false;
        }
    }
}
