using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member1_UserManagement
{
    public class AuthService : IAuthService
    {
        private readonly Dictionary<string, User> _usersById = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _userIdByUsername = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _userIdByEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _userIdByResetToken = new Dictionary<string, string>();
        
        private readonly Dictionary<UserRoleType, Role> _roles = new Dictionary<UserRoleType, Role>();
        private readonly Dictionary<string, Permission> _permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();

        public AuthService()
        {
            InitializePermissionsAndRoles();
        }

        private void InitializePermissionsAndRoles()
        {
            // Seed Permissions
            AddPermission(new Permission("idea.submit", "Submit Project Idea", "Member2", "Allow student to submit new project ideas"));
            AddPermission(new Permission("idea.view", "View Project Ideas", "Member2", "Allow user to browse ideas and archive"));
            AddPermission(new Permission("idea.review", "Review Project Ideas", "Member2", "Allow supervisor to accept, reject, or request edits"));
            
            AddPermission(new Permission("team.create", "Create Team", "Member3", "Allow student to create graduation project team"));
            AddPermission(new Permission("team.join", "Join Team", "Member3", "Allow student to accept invite and join team"));
            AddPermission(new Permission("team.view", "View Teams", "Member3", "Allow viewing teams and member rosters"));
            AddPermission(new Permission("supervisor.assign", "Assign Supervisors", "Member3", "Allow admin to allocate supervisors to teams"));

            AddPermission(new Permission("task.manage", "Manage Tasks", "Member4", "Create, update, and assign milestone tasks"));
            AddPermission(new Permission("task.view", "View Tasks & Timeline", "Member4", "View project taskboard and milestone timeline"));
            AddPermission(new Permission("progress.track", "Track Team Progress", "Member4", "Supervisor and admin tracking of team progress"));

            AddPermission(new Permission("user.manage", "Manage Users", "Member1", "Admin full control over user profiles and accounts"));
            AddPermission(new Permission("report.view", "View College Reports", "Member8", "Comprehensive admin reports and statistics"));

            // Seed Roles with their permission assignments
            var studentRole = new Role(UserRoleType.Student, "Student", "College Student pursuing graduation project");
            studentRole.PermissionIds.AddRange(new[] { "idea.submit", "idea.view", "team.create", "team.join", "task.view", "task.manage" });
            _roles[UserRoleType.Student] = studentRole;

            var supervisorRole = new Role(UserRoleType.Supervisor, "Supervisor", "Academic Doctor / Supervisor supervising graduation teams");
            supervisorRole.PermissionIds.AddRange(new[] { "idea.view", "idea.review", "team.view", "task.view", "progress.track" });
            _roles[UserRoleType.Supervisor] = supervisorRole;

            var adminRole = new Role(UserRoleType.Admin, "Admin", "College Administration managing the graduation system");
            adminRole.PermissionIds.AddRange(_permissions.Keys); // Admin gets all permissions
            _roles[UserRoleType.Admin] = adminRole;
        }

        private void AddPermission(Permission perm)
        {
            _permissions[perm.Id] = perm;
        }

        public Result<User> Register(string username, string email, string password, UserRoleType role,
            string fullName, string department, string academicId)
        {
            if (string.IsNullOrWhiteSpace(username)) return Result<User>.Fail("Username is required");
            if (string.IsNullOrWhiteSpace(email) || !email.Contains("@")) return Result<User>.Fail("A valid email address is required");
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6) return Result<User>.Fail("Password must be at least 6 characters long");
            if (string.IsNullOrWhiteSpace(fullName)) return Result<User>.Fail("Full name is required");

            lock (_lock)
            {
                if (_userIdByUsername.ContainsKey(username.Trim()))
                    return Result<User>.Fail("Username is already taken");

                if (_userIdByEmail.ContainsKey(email.Trim()))
                    return Result<User>.Fail("Email is already registered");

                string salt = GenerateSalt();
                string hash = HashPassword(password, salt);

                var user = new User
                {
                    Username = username.Trim(),
                    Email = email.Trim().ToLowerInvariant(),
                    PasswordSalt = salt,
                    PasswordHash = hash,
                    Role = role,
                    IsActive = true,
                    Profile = new UserProfile
                    {
                        FullName = fullName.Trim(),
                        Department = department ?? "Computer Science",
                        AcademicIdentifier = academicId ?? "",
                        Bio = string.Empty,
                        UpdatedAt = DateTime.UtcNow
                    }
                };
                user.Profile.UserId = user.Id;

                _usersById[user.Id] = user;
                _userIdByUsername[user.Username] = user.Id;
                _userIdByEmail[user.Email] = user.Id;

                return Result<User>.Ok(user, "User registered successfully");
            }
        }

        public Result<User> Login(string usernameOrEmail, string password)
        {
            if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrWhiteSpace(password))
                return Result<User>.Fail("Username/Email and Password are required");

            lock (_lock)
            {
                string userId = null;
                if (_userIdByUsername.TryGetValue(usernameOrEmail.Trim(), out userId) ||
                    _userIdByEmail.TryGetValue(usernameOrEmail.Trim().ToLowerInvariant(), out userId))
                {
                    var user = _usersById[userId];
                    if (!user.IsActive)
                        return Result<User>.Fail("Account is deactivated. Please contact college administration");

                    string computedHash = HashPassword(password, user.PasswordSalt);
                    if (computedHash == user.PasswordHash)
                    {
                        return Result<User>.Ok(user, "Login successful");
                    }
                }

                return Result<User>.Fail("Invalid credentials");
            }
        }

        public Result UpdateProfile(string userId, string fullName, string department, string phone, string bio)
        {
            lock (_lock)
            {
                if (!_usersById.ContainsKey(userId))
                    return Result.Fail("User not found");

                var user = _usersById[userId];
                if (!string.IsNullOrWhiteSpace(fullName)) user.Profile.FullName = fullName.Trim();
                if (!string.IsNullOrWhiteSpace(department)) user.Profile.Department = department.Trim();
                if (phone != null) user.Profile.Phone = phone.Trim();
                if (bio != null) user.Profile.Bio = bio.Trim();
                user.Profile.UpdatedAt = DateTime.UtcNow;

                return Result.Ok("Profile updated successfully");
            }
        }

        public Result<string> RequestPasswordReset(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return Result<string>.Fail("Email is required");

            lock (_lock)
            {
                string userId;
                if (!_userIdByEmail.TryGetValue(email.Trim().ToLowerInvariant(), out userId))
                    return Result<string>.Fail("No account found with this email");

                var user = _usersById[userId];
                string token = Guid.NewGuid().ToString("N");
                user.PasswordResetToken = token;
                user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(2);

                _userIdByResetToken[token] = user.Id;
                return Result<string>.Ok(token, "Password reset token generated successfully");
            }
        }

        public Result ResetPassword(string token, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(token)) return Result.Fail("Reset token is required");
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return Result.Fail("New password must be at least 6 characters long");

            lock (_lock)
            {
                string userId;
                if (!_userIdByResetToken.TryGetValue(token, out userId))
                    return Result.Fail("Invalid or expired reset token");

                var user = _usersById[userId];
                if (user.PasswordResetTokenExpiry == null || user.PasswordResetTokenExpiry < DateTime.UtcNow)
                {
                    _userIdByResetToken.Remove(token);
                    return Result.Fail("Password reset token has expired");
                }

                string newSalt = GenerateSalt();
                user.PasswordSalt = newSalt;
                user.PasswordHash = HashPassword(newPassword, newSalt);
                user.PasswordResetToken = null;
                user.PasswordResetTokenExpiry = null;
                _userIdByResetToken.Remove(token);

                return Result.Ok("Password reset successfully. You can now login with your new password");
            }
        }

        public bool HasPermission(string userId, string permissionId)
        {
            lock (_lock)
            {
                if (!_usersById.ContainsKey(userId)) return false;
                var user = _usersById[userId];
                if (!_roles.ContainsKey(user.Role)) return false;

                var role = _roles[user.Role];
                return role.PermissionIds.Contains(permissionId, StringComparer.OrdinalIgnoreCase);
            }
        }

        public User GetUserById(string userId)
        {
            lock (_lock)
            {
                User user;
                _usersById.TryGetValue(userId, out user);
                return user;
            }
        }

        public List<User> GetUsersByRole(UserRoleType role)
        {
            lock (_lock)
            {
                return _usersById.Values.Where(u => u.Role == role).ToList();
            }
        }

        public List<Permission> GetRolePermissions(UserRoleType role)
        {
            lock (_lock)
            {
                if (!_roles.ContainsKey(role)) return new List<Permission>();
                var permIds = _roles[role].PermissionIds;
                return permIds.Where(id => _permissions.ContainsKey(id)).Select(id => _permissions[id]).ToList();
            }
        }

        private static string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(saltBytes);
            }
            return Convert.ToBase64String(saltBytes);
        }

        private static string HashPassword(string password, string salt)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] combinedBytes = Encoding.UTF8.GetBytes(password + salt);
                byte[] hash = sha256.ComputeHash(combinedBytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
