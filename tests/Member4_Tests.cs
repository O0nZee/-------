using System;
using System.Collections.Generic;
using GraduationPlatform.Common;
using GraduationPlatform.Member4_ProjectTracking;

namespace GraduationPlatform.Tests
{
    public static class Member4_Tests
    {
        public static void RunTests()
        {
            Console.WriteLine("======================================================");
            Console.WriteLine(">>> RUNNING TESTS: Member 4 - Tracking & Milestones <<<");
            Console.WriteLine("======================================================");

            int passed = 0;
            int total = 0;

            Test("Create graduation project milestone succeeds", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                var mRes = tracking.CreateMilestone("team_10", "Phase 1: Requirements Spec", "Gather user stories", 1, DateTime.UtcNow.AddDays(14));

                Assert(mRes.IsSuccess, "Milestone creation should succeed");
                Assert(mRes.Data.Title.Contains("Requirements"), "Title match");
                Assert(mRes.Data.SequenceOrder == 1, "Order match");
                Assert(!mRes.Data.IsCompleted, "New milestone is not completed");
            });

            Test("Initialize standard graduation milestones creates 4 phases", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                var initRes = tracking.InitializeDefaultMilestones("team_20");

                Assert(initRes.IsSuccess, "Initialization succeeds");
                var milestones = tracking.GetMilestones("team_20");
                Assert(milestones.Count == 4, "Must have exactly 4 standard phases");
                Assert(milestones[0].Title.Contains("Proposal & SRS"), "Phase 1 match");
                Assert(milestones[1].Title.Contains("Architecture & Database"), "Phase 2 match");
                Assert(milestones[2].Title.Contains("Core Implementation"), "Phase 3 match");
                Assert(milestones[3].Title.Contains("Testing, Deployment"), "Phase 4 match");
            });

            Test("Create task and assign to team member", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                var m = tracking.CreateMilestone("team_30", "Phase 1", "SRS", 1, DateTime.UtcNow.AddDays(10)).Data;

                var taskRes = tracking.CreateTask("team_30", m.Id, "Design Database ERD", "Create schema with Users, Teams, Ideas tables",
                    TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(5));

                Assert(taskRes.IsSuccess, "Task creation should succeed");
                var task = taskRes.Data;
                Assert(task.Status == TaskItemStatus.ToDo, "Default status is ToDo");
                Assert(task.Priority == TaskPriority.High, "Priority is High");

                var assignRes = tracking.AssignTaskToMember(task.Id, "student_omar", "Omar Khalid");
                Assert(assignRes.IsSuccess, "Task assignment should succeed");

                var assignedTasks = tracking.GetTasksByStudent("student_omar");
                Assert(assignedTasks.Count == 1, "Student has 1 assigned task");
                Assert(assignedTasks[0].Title == "Design Database ERD", "Task title matches");
            });

            Test("Task creation fails when due date is before start date", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                var invalidDates = tracking.CreateTask("team_40", null, "Bad Date Task", "Desc",
                    TaskPriority.Medium, DateTime.UtcNow.AddDays(5), DateTime.UtcNow.AddDays(1)); // Due before start!

                Assert(!invalidDates.IsSuccess, "Due date before start date must fail");
            });

            Test("Update task status through workflow (ToDo -> InProgress -> Completed)", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                var task = tracking.CreateTask("team_50", null, "Write Auth Service", "Desc",
                    TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(4)).Data;

                Assert(task.Status == TaskItemStatus.ToDo, "Initial status is ToDo");

                tracking.UpdateTaskStatus(task.Id, TaskItemStatus.InProgress);
                var inProgTask = tracking.GetTasksByTeam("team_50")[0];
                Assert(inProgTask.Status == TaskItemStatus.InProgress, "Status updated to InProgress");

                tracking.UpdateTaskStatus(task.Id, TaskItemStatus.Completed);
                var doneTask = tracking.GetTasksByTeam("team_50")[0];
                Assert(doneTask.Status == TaskItemStatus.Completed, "Status updated to Completed");
                Assert(doneTask.CompletedAt.HasValue, "Completed timestamp recorded");
            });

            Test("Kanban Board accurately groups tasks by status", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                string teamId = "team_kanban";

                var t1 = tracking.CreateTask(teamId, null, "Task 1", "Desc", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;
                var t2 = tracking.CreateTask(teamId, null, "Task 2", "Desc", TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;
                var t3 = tracking.CreateTask(teamId, null, "Task 3", "Desc", TaskPriority.Low, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;

                tracking.UpdateTaskStatus(t2.Id, TaskItemStatus.InProgress);
                tracking.UpdateTaskStatus(t3.Id, TaskItemStatus.Completed);

                var board = tracking.GetKanbanBoard(teamId);
                Assert(board.ToDo.Count == 1 && board.ToDo[0].Id == t1.Id, "1 task in ToDo");
                Assert(board.InProgress.Count == 1 && board.InProgress[0].Id == t2.Id, "1 task in InProgress");
                Assert(board.Completed.Count == 1 && board.Completed[0].Id == t3.Id, "1 task in Completed");
            });

            Test("Project Timeline calculates progress percentage and milestone tracking", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                string teamId = "team_progress";
                tracking.InitializeDefaultMilestones(teamId);

                // Create 4 tasks
                var t1 = tracking.CreateTask(teamId, null, "Task 1", "D", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;
                var t2 = tracking.CreateTask(teamId, null, "Task 2", "D", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;
                var t3 = tracking.CreateTask(teamId, null, "Task 3", "D", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;
                var t4 = tracking.CreateTask(teamId, null, "Task 4", "D", TaskPriority.Medium, DateTime.UtcNow, DateTime.UtcNow.AddDays(3)).Data;

                // Complete 3 out of 4 tasks = 75%
                tracking.UpdateTaskStatus(t1.Id, TaskItemStatus.Completed);
                tracking.UpdateTaskStatus(t2.Id, TaskItemStatus.Completed);
                tracking.UpdateTaskStatus(t3.Id, TaskItemStatus.Completed);

                var timeline = tracking.GetProjectTimeline(teamId);
                Assert(timeline.TotalTasks == 4, "Total tasks is 4");
                Assert(timeline.CompletedTasks == 3, "Completed tasks is 3");
                Assert(timeline.ToDoTasks == 1, "ToDo tasks is 1");
                Assert(Math.Abs(timeline.CompletionPercentage - 75.0) < 0.1, "Completion percentage should be 75.0%");
            });

            Test("Deadline Alerts detects Overdue tasks and Approaching Deadlines", ref passed, ref total, () =>
            {
                var tracking = new TrackingService();
                string teamId = "team_alerts";

                // Overdue task (due 3 days ago)
                var overdueTask = tracking.CreateTask(teamId, null, "Submit Midterm Report", "Overdue",
                    TaskPriority.Urgent, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-3)).Data;

                // Upcoming task (due in 2 days)
                var upcomingTask = tracking.CreateTask(teamId, null, "Prepare Presentation Slides", "Soon",
                    TaskPriority.High, DateTime.UtcNow, DateTime.UtcNow.AddDays(2)).Data;

                // Far task (due in 25 days - no alert)
                var farTask = tracking.CreateTask(teamId, null, "Final Defense Book", "Far away",
                    TaskPriority.Low, DateTime.UtcNow, DateTime.UtcNow.AddDays(25)).Data;

                var alerts = tracking.CheckDeadlineAlerts(teamId, 3);
                Assert(alerts.Count == 2, "Should trigger exactly 2 alerts (1 overdue + 1 upcoming)");

                var overdueAlert = alerts.Find(a => a.TaskId == overdueTask.Id);
                Assert(overdueAlert != null, "Overdue alert generated");
                Assert(overdueAlert.IsOverdue, "Flagged as Overdue");
                Console.WriteLine(string.Format("   [Tracking Alert]: {0}", overdueAlert.AlertMessage));

                var upcomingAlert = alerts.Find(a => a.TaskId == upcomingTask.Id);
                Assert(upcomingAlert != null, "Upcoming alert generated");
                Assert(!upcomingAlert.IsOverdue, "Flagged as upcoming warning");
                Console.WriteLine(string.Format("   [Tracking Alert]: {0}", upcomingAlert.AlertMessage));
            });

            Console.WriteLine(string.Format("\n[Member 4 Results]: {0}/{1} tests PASSED ({2:P0})\n", passed, total, (double)passed / total));
            if (passed != total) throw new Exception("Member 4 Unit Tests Failed!");
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
