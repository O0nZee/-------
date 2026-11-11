using System.Collections.Generic;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member1_UserManagement
{
    public interface IAuthService
    {
        Result<User> Register(string username, string email, string password, UserRoleType role, 
            string fullName, string department, string academicId);
        
        Result<User> Login(string usernameOrEmail, string password);

        Result UpdateProfile(string userId, string fullName, string department, string phone, string bio);

        Result<string> RequestPasswordReset(string email);

        Result ResetPassword(string token, string newPassword);

        bool HasPermission(string userId, string permissionId);

        User GetUserById(string userId);

        List<User> GetUsersByRole(UserRoleType role);

        List<Permission> GetRolePermissions(UserRoleType role);
    }
}
