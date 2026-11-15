using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member1_UserManagement
{
    public partial class AuthService
    {
        public List<User> ExportUsers() { lock (_lock) return _usersById.Values.ToList(); }

        public void ImportUsers(IEnumerable<User> users)
        {
            lock (_lock)
            {
                _usersById.Clear(); _userIdByUsername.Clear(); _userIdByEmail.Clear(); _userIdByResetToken.Clear();
                foreach (var user in users)
                {
                    _usersById.Add(user.Id, user);
                    _userIdByUsername.Add(user.Username, user.Id);
                    _userIdByEmail.Add(user.Email, user.Id);
                    if (user.PasswordResetToken != null) _userIdByResetToken[user.PasswordResetToken] = user.Id;
                }
            }
        }

        public Result SetAccountState(string userId, bool active, UserRoleType role)
        {
            lock (_lock)
            {
                User user;
                if (!_usersById.TryGetValue(userId, out user)) return Result.Fail("User not found");
                if (!Enum.IsDefined(typeof(UserRoleType), role)) return Result.Fail("Invalid role");
                if (user.Role == UserRoleType.Admin && user.IsActive && (!active || role != UserRoleType.Admin)
                    && !_usersById.Values.Any(u => u.Id != user.Id && u.Role == UserRoleType.Admin && u.IsActive))
                    return Result.Fail("The last active administrator cannot be removed");
                if (user.IsActive != active || user.Role != role)
                {
                    user.SecurityStamp = Guid.NewGuid().ToString("N");
                    if (user.PasswordResetToken != null) _userIdByResetToken.Remove(user.PasswordResetToken);
                    user.PasswordResetToken = null; user.PasswordResetTokenExpiry = null;
                }
                if (user.Role != role)
                {
                    user.SupervisorPermissionIds = new();
                    user.SupervisorPermissionsUpdatedBy = null;
                    user.SupervisorPermissionsUpdatedAt = null;
                }
                user.IsActive = active; user.Role = role;
                return Result.Ok("Account updated");
            }
        }

        public Result ChangePassword(string userId, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 128)
                return Result.Fail("Password must be between 8 and 128 characters");
            lock (_lock)
            {
                User user;
                if (!_usersById.TryGetValue(userId, out user) || !user.IsActive) return Result.Fail("User not found");
                if (!VerifyPassword(currentPassword, user.PasswordSalt, user.PasswordHash)) return Result.Fail("Current password is incorrect");
                user.PasswordSalt = GenerateSalt(); user.PasswordHash = HashPassword(newPassword, user.PasswordSalt);
                user.SecurityStamp = Guid.NewGuid().ToString("N");
                if (user.PasswordResetToken != null) _userIdByResetToken.Remove(user.PasswordResetToken);
                user.PasswordResetToken = null; user.PasswordResetTokenExpiry = null;
                return Result.Ok("Password changed");
            }
        }
    }
}
