using System;
using System.Collections.Generic;
using GraduationPlatform.Common;
using GraduationPlatform.Member1_UserManagement;

namespace GraduationPlatform.Tests
{
    public static class Member1_Tests
    {
        public static void RunTests()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine(">>> RUNNING TESTS: Member 1 - User Management <<<");
            Console.WriteLine("=================================================");

            int passed = 0;
            int total = 0;

            Test("Register new student succeeds", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                var result = auth.Register("ahmed2026", "ahmed@alexu.edu.eg", "P@ssword123", UserRoleType.Student, 
                    "Ahmed Mahmoud", "Computer Science", "ST-2026-001");

                Assert(result.IsSuccess, "Registration should succeed");
                Assert(result.Data != null, "User data should not be null");
                Assert(result.Data.Username == "ahmed2026", "Username match");
                Assert(result.Data.Role == UserRoleType.Student, "Role must be Student");
                Assert(result.Data.Profile.FullName == "Ahmed Mahmoud", "Profile full name match");
            });

            Test("Duplicate username registration is blocked", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                auth.Register("sarah_dr", "sarah1@alexu.edu.eg", "DocPass123", UserRoleType.Supervisor, 
                    "Dr. Sarah Hassan", "Information Systems", "PROF-042");

                var dup = auth.Register("sarah_dr", "sarah2@alexu.edu.eg", "DocPass123", UserRoleType.Supervisor, 
                    "Dr. Sarah Hassan", "Information Systems", "PROF-042");

                Assert(!dup.IsSuccess, "Duplicate username should fail");
                Assert(dup.Message.Contains("already taken"), "Message should indicate username taken");
            });

            Test("Duplicate email registration is blocked", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                auth.Register("user_one", "unique@alexu.edu.eg", "Pass12345", UserRoleType.Student, "User One", "CS", "001");
                var dup = auth.Register("user_two", "unique@alexu.edu.eg", "Pass12345", UserRoleType.Student, "User Two", "CS", "002");

                Assert(!dup.IsSuccess, "Duplicate email should fail");
                Assert(dup.Message.Contains("already registered"), "Message should indicate email registered");
            });

            Test("Login with username and password succeeds", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                auth.Register("omar_admin", "admin@alexu.edu.eg", "AdminSecret99", UserRoleType.Admin, 
                    "Admin Omar", "Dean Office", "ADM-001");

                var loginRes = auth.Login("omar_admin", "AdminSecret99");
                Assert(loginRes.IsSuccess, "Login with username should succeed");
                Assert(loginRes.Data.Email == "admin@alexu.edu.eg", "Email matches logged in user");
            });

            Test("Login with email and password succeeds", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                auth.Register("mona_student", "mona@alexu.edu.eg", "StudentPass1", UserRoleType.Student, 
                    "Mona Ali", "AI Department", "ST-2026-050");

                var loginRes = auth.Login("mona@alexu.edu.eg", "StudentPass1");
                Assert(loginRes.IsSuccess, "Login with email should succeed");
            });

            Test("Login fails with invalid password", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                auth.Register("karim", "karim@alexu.edu.eg", "CorrectPass", UserRoleType.Student, 
                    "Karim Nader", "CS", "ST-111");

                var loginRes = auth.Login("karim", "WrongPassword");
                Assert(!loginRes.IsSuccess, "Login with bad password should fail");
            });

            Test("Update user profile updates details", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                var reg = auth.Register("tarek", "tarek@alexu.edu.eg", "Pass123456", UserRoleType.Student, 
                    "Tarek Zaki", "CS", "ST-222");

                var updateRes = auth.UpdateProfile(reg.Data.Id, "Tarek Zaki Updated", "Software Engineering", "+201012345678", "Enthusiastic developer");
                Assert(updateRes.IsSuccess, "Profile update should succeed");

                var user = auth.GetUserById(reg.Data.Id);
                Assert(user.Profile.FullName == "Tarek Zaki Updated", "Full name should be updated");
                Assert(user.Profile.Department == "Software Engineering", "Department should be updated");
                Assert(user.Profile.Phone == "+201012345678", "Phone should be updated");
            });

            Test("Password reset workflow succeeds", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                var reg = auth.Register("reset_user", "reset@alexu.edu.eg", "OldPassword1", UserRoleType.Student, 
                    "Reset Test", "CS", "ST-333");

                var reqRes = auth.RequestPasswordReset("reset@alexu.edu.eg");
                Assert(reqRes.IsSuccess, "Password reset request should succeed");
                string token = reqRes.Data;
                Assert(!string.IsNullOrEmpty(token), "Token should not be empty");

                var resetRes = auth.ResetPassword(token, "BrandNewPassword2");
                Assert(resetRes.IsSuccess, "Password reset should succeed");

                // Old password must fail
                var oldLogin = auth.Login("reset_user", "OldPassword1");
                Assert(!oldLogin.IsSuccess, "Old password must fail");

                // New password must succeed
                var newLogin = auth.Login("reset_user", "BrandNewPassword2");
                Assert(newLogin.IsSuccess, "New password must succeed");
            });

            Test("Role-Based Access Control (RBAC) permissions verification", ref passed, ref total, () =>
            {
                var auth = new AuthService();
                var sReg = auth.Register("std1", "s1@alexu.edu.eg", "Pass1234", UserRoleType.Student, "Student 1", "CS", "S1");
                var dReg = auth.Register("doc1", "d1@alexu.edu.eg", "Pass1234", UserRoleType.Supervisor, "Dr. 1", "CS", "D1");
                var aReg = auth.Register("adm1", "a1@alexu.edu.eg", "Pass1234", UserRoleType.Admin, "Admin 1", "CS", "A1");

                // Student checks
                Assert(auth.HasPermission(sReg.Data.Id, "idea.submit"), "Student can submit ideas");
                Assert(auth.HasPermission(sReg.Data.Id, "team.create"), "Student can create teams");
                Assert(!auth.HasPermission(sReg.Data.Id, "supervisor.assign"), "Student CANNOT assign supervisors");
                Assert(!auth.HasPermission(sReg.Data.Id, "idea.review"), "Student CANNOT review ideas");

                // Supervisor checks
                Assert(!auth.HasPermission(dReg.Data.Id, "idea.review"), "New supervisors have no delegated review permission");
                dReg.Data.SupervisorPermissionIds.AddRange(new[] { "team.view", "idea.review" });
                Assert(auth.HasPermission(dReg.Data.Id, "idea.review"), "Granted supervisor can review ideas");
                Assert(auth.HasPermission(dReg.Data.Id, "team.view"), "Granted supervisor can view assigned teams");
                Assert(!auth.HasPermission(dReg.Data.Id, "idea.submit"), "Supervisor CANNOT submit student ideas");

                // Admin checks
                Assert(auth.HasPermission(aReg.Data.Id, "supervisor.assign"), "Admin can assign supervisors");
                Assert(auth.HasPermission(aReg.Data.Id, "user.manage"), "Admin can manage users");
                Assert(auth.HasPermission(aReg.Data.Id, "report.view"), "Admin can view reports");
            });

            Console.WriteLine(string.Format("\n[Member 1 Results]: {0}/{1} tests PASSED ({2:P0})\n", passed, total, (double)passed / total));
            if (passed != total) throw new Exception("Member 1 Unit Tests Failed!");
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
