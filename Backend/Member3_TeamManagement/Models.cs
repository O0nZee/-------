using System;
using System.Collections.Generic;

namespace GraduationPlatform.Member3_TeamManagement
{
    public enum InvitationStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Cancelled = 3
    }

    public class TeamMember
    {
        public string Id { get; set; }
        public string TeamId { get; set; }
        public string StudentId { get; set; }
        public string MemberName { get; set; }
        public string RoleInTeam { get; set; } // "Leader", "Frontend", "Backend", "AI Engineer", etc.
        public DateTime JoinedAt { get; set; }

        public TeamMember()
        {
            Id = Guid.NewGuid().ToString("N");
            JoinedAt = DateTime.UtcNow;
            RoleInTeam = "Member";
        }
    }

    public class Team
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LeaderStudentId { get; set; }
        public string ProjectIdeaId { get; set; }
        public string AssignedSupervisorId { get; set; }
        public int MaxMembers { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<TeamMember> Members { get; set; }

        public Team()
        {
            Id = Guid.NewGuid().ToString("N");
            MaxMembers = 5; // Standard team size limit
            CreatedAt = DateTime.UtcNow;
            Members = new List<TeamMember>();
        }
    }

    public class TeamInvitation
    {
        public string Id { get; set; }
        public string TeamId { get; set; }
        public string InviterStudentId { get; set; }
        public string InviteeStudentId { get; set; }
        public string ProposedRole { get; set; }
        public InvitationStatus Status { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? RespondedAt { get; set; }

        public TeamInvitation()
        {
            Id = Guid.NewGuid().ToString("N");
            Status = InvitationStatus.Pending;
            SentAt = DateTime.UtcNow;
            ProposedRole = "Member";
        }
    }

    public class Supervisor
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Name { get; set; }
        public string AcademicTitle { get; set; } // e.g. "Prof.", "Assoc. Prof.", "Dr."
        public string Department { get; set; }
        public string Specialization { get; set; }
        public int MaxSupervisedTeams { get; set; }
        public int ActiveTeamsCount { get; set; }

        public Supervisor()
        {
            Id = Guid.NewGuid().ToString("N");
            MaxSupervisedTeams = 3; // Standard university quota: max 3 teams per supervisor
            ActiveTeamsCount = 0;
        }
    }

    public class SupervisorAssignment
    {
        public string Id { get; set; }
        public string TeamId { get; set; }
        public string SupervisorId { get; set; }
        public string AssignedByAdminId { get; set; }
        public DateTime AssignedAt { get; set; }
        public bool IsActive { get; set; }

        public SupervisorAssignment()
        {
            Id = Guid.NewGuid().ToString("N");
            AssignedAt = DateTime.UtcNow;
            IsActive = true;
        }
    }
}
