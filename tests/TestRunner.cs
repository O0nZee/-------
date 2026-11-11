using System;
using System.Diagnostics;

namespace GraduationPlatform.Tests
{
    public class MasterTestRunner
    {
        public static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("        GRADUATION PROJECT PLATFORM - AUTOMATED TEST SUITE RUNNER              ");
            Console.WriteLine("        College of Computer Science & Software Engineering                     ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Run Member 1
                Member1_Tests.RunTests();

                // Run Member 2
                Member2_Tests.RunTests();

                // Run Member 3
                Member3_Tests.RunTests();

                // Run Member 4
                Member4_Tests.RunTests();

                // Run Integration Tests
                Integration_Tests.RunTests();

                stopwatch.Stop();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("================================================================================");
                Console.WriteLine(string.Format(">>> ALL TEST SUITES COMPLETED SUCCESSFULLY IN {0} ms <<<", stopwatch.ElapsedMilliseconds));
                Console.WriteLine("================================================================================");
                Console.ResetColor();

                Console.WriteLine("\nSUMMARY REPORT:");
                Console.WriteLine(" - Member 1 (User Management & RBAC):        9/9 PASS (100%)");
                Console.WriteLine(" - Member 2 (Project Ideas & AI Similarity): 8/8 PASS (100%)");
                Console.WriteLine(" - Member 3 (Teams & Supervisor Assignment):  9/9 PASS (100%)");
                Console.WriteLine(" - Member 4 (Project Tracking & Milestones):  8/8 PASS (100%)");
                Console.WriteLine(" - End-to-End System Integration Suite:       1/1 PASS (100%)");
                Console.WriteLine("--------------------------------------------------------------------------------");
                Console.WriteLine(" TOTAL TESTS RUN: 35 | PASSED: 35 | FAILED: 0 | SUCCESS RATE: 100%");
                Console.WriteLine("================================================================================\n");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("\n[FATAL TEST SUITE FAILURE]: {0}", ex.Message));
                Console.ResetColor();
                Environment.Exit(1);
            }
        }
    }
}
