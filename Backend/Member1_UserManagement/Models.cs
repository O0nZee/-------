using System;
using System.Collections.Generic;

namespace GraduationPlatform.Member1_UserManagement
{
    public enum UserRoleType
    {
        Student = 1,
        Supervisor = 2,
        Admin = 3
    }

    public class Permission
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Module { get; set; }
        public string Description { get; set; }

        public Permission(string id, string name, string module, string description)
        {
            Id = id;
            Name = name;
            Module = module;
            Description = description;
        }
    }

    public class Role
    {
        public UserRoleType RoleType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> PermissionIds { get; set; }

        public Role()
        {
            PermissionIds = new List<string>();
        }

        public Role(UserRoleType roleType, string name, string description)
        {
            RoleType = roleType;
            Name = name;
            Description = description;
            PermissionIds = new List<string>();
        }
    }

    public class UserProfile
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public int? StudyYear { get; set; }
        public string Phone { get; set; }
        public string AcademicIdentifier { get; set; } // Student ID or Academic Title (Dr., Prof.)
        public string Bio { get; set; }
        public DateTime UpdatedAt { get; set; }

        public UserProfile()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public class User
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
        public UserRoleType Role { get; set; }
        public List<string> SupervisorPermissionIds { get; set; } = new();
        public string SupervisorPermissionsUpdatedBy { get; set; }
        public DateTime? SupervisorPermissionsUpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; set; }
        public UserProfile Profile { get; set; }
        public string PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpiry { get; set; }

        public User()
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.UtcNow;
            IsActive = true;
            Profile = new UserProfile { UserId = Id };
        }
    }
}
