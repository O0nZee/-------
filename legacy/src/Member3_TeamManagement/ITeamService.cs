using System.Collections.Generic;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member3_TeamManagement
{
    public interface ITeamService
    {
        Result<Team> CreateTeam(string leaderStudentId, string leaderName, string teamName, int maxMembers = 5);

        Result<TeamInvitation> SendInvitation(string inviterStudentId, string teamId, string inviteeStudentId, string proposedRole = "Member");

        Result RespondToInvitation(string invitationId, string studentId, string studentName, bool accept);

        Result LinkProjectIdea(string teamId, string projectIdeaId);

        Result<Supervisor> RegisterSupervisor(string userId, string name, string title, string department, 
            string specialization, int maxTeams = 3);

        Result<SupervisorAssignment> AssignSupervisorToTeam(string adminId, string supervisorId, string teamId);

        Team GetTeamById(string teamId);

        Team GetTeamByStudentId(string studentId);

        List<Team> GetTeamsBySupervisor(string supervisorId);

        List<TeamInvitation> GetPendingInvitationsForStudent(string studentId);

        List<Supervisor> GetAllSupervisors();

        List<Team> GetAllTeams();
    }
}
