using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member4_ProjectTracking
{
    public class TrackingService : ITrackingService
    {
        private readonly Dictionary<string, Milestone> _milestones = new Dictionary<string, Milestone>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TaskItem> _tasks = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public Result<Milestone> CreateMilestone(string teamId, string title, string description, int sequenceOrder, DateTime dueDate)
        {
            if (string.IsNullOrWhiteSpace(teamId)) return Result<Milestone>.Fail("Team ID is required");
            if (string.IsNullOrWhiteSpace(title)) return Result<Milestone>.Fail("Milestone title is required");

            lock (_lock)
            {
                var milestone = new Milestone
                {
                    TeamId = teamId.Trim(),
                    Title = title.Trim(),
                    Description = description != null ? description.Trim() : string.Empty,
                    SequenceOrder = sequenceOrder,
                    DueDate = dueDate,
                    IsCompleted = false
                };

                _milestones[milestone.Id] = milestone;
                return Result<Milestone>.Ok(milestone, "Milestone created successfully");
            }
        }

        public Result<List<Milestone>> InitializeDefaultMilestones(string teamId)
        {
            if (string.IsNullOrWhiteSpace(teamId)) return Result<List<Milestone>>.Fail("Team ID is required");

            lock (_lock)
            {
                DateTime now = DateTime.UtcNow;
                var created = new List<Milestone>
                {
                    CreateMilestone(teamId, "Phase 1: Project Proposal & SRS", "Requirements elicitation, problem formulation and supervisor approval", 1, now.AddDays(21)).Data,
                    CreateMilestone(teamId, "Phase 2: Architecture & Database Design", "ERD, class diagrams, API specs, and UI wireframes", 2, now.AddDays(50)).Data,
                    CreateMilestone(teamId, "Phase 3: Core Implementation & Integration", "Backend services, frontend modules, and database setup", 3, now.AddDays(100)).Data,
                    CreateMilestone(teamId, "Phase 4: Testing, Deployment & Thesis Defense", "Unit/integration testing, cloud deployment, and final book", 4, now.AddDays(140)).Data
                };

                return Result<List<Milestone>>.Ok(created, "Default graduation milestones initialized");
            }
        }

        public Result<TaskItem> CreateTask(string teamId, string milestoneId, string title, string description,
            TaskPriority priority, DateTime startDate, DateTime dueDate)
        {
            if (string.IsNullOrWhiteSpace(teamId)) return Result<TaskItem>.Fail("Team ID is required");
            if (string.IsNullOrWhiteSpace(title)) return Result<TaskItem>.Fail("Task title is required");
            if (dueDate < startDate) return Result<TaskItem>.Fail("Due date cannot be earlier than start date");

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(milestoneId) && !_milestones.ContainsKey(milestoneId))
                    return Result<TaskItem>.Fail("Milestone not found");

                var task = new TaskItem
                {
                    TeamId = teamId.Trim(),
                    MilestoneId = milestoneId,
                    Title = title.Trim(),
                    Description = description != null ? description.Trim() : string.Empty,
                    Priority = priority,
                    StartDate = startDate,
                    DueDate = dueDate,
                    Status = TaskItemStatus.ToDo,
                    CreatedAt = DateTime.UtcNow
                };

                _tasks[task.Id] = task;
                return Result<TaskItem>.Ok(task, "Task created successfully");
            }
        }

        public Result AssignTaskToMember(string taskId, string studentId, string studentName)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return Result.Fail("Task ID is required");
            if (string.IsNullOrWhiteSpace(studentId)) return Result.Fail("Student ID is required");

            lock (_lock)
            {
                if (!_tasks.ContainsKey(taskId)) return Result.Fail("Task not found");
                var task = _tasks[taskId];

                bool alreadyAssigned = task.Assignments.Any(a => a.AssignedStudentId.Equals(studentId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (alreadyAssigned) return Result.Fail("Task is already assigned to this student");

                var assignment = new TaskAssignment
                {
                    TaskId = task.Id,
                    AssignedStudentId = studentId.Trim(),
                    StudentName = !string.IsNullOrWhiteSpace(studentName) ? studentName.Trim() : "Student",
                    AssignedAt = DateTime.UtcNow
                };

                task.Assignments.Add(assignment);
                return Result.Ok("Task assigned to team member");
            }
        }

        public Result UpdateTaskStatus(string taskId, TaskItemStatus newStatus)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return Result.Fail("Task ID is required");

            lock (_lock)
            {
                if (!_tasks.ContainsKey(taskId)) return Result.Fail("Task not found");
                var task = _tasks[taskId];
                task.Status = newStatus;

                if (newStatus == TaskItemStatus.Completed)
                {
                    task.CompletedAt = DateTime.UtcNow;
                }
                else
                {
                    task.CompletedAt = null;
                }

                // If task belongs to milestone, check if all tasks in milestone are completed
                if (!string.IsNullOrEmpty(task.MilestoneId) && _milestones.ContainsKey(task.MilestoneId))
                {
                    var milestone = _milestones[task.MilestoneId];
                    var milestoneTasks = _tasks.Values.Where(t => t.MilestoneId == milestone.Id).ToList();
                    if (milestoneTasks.Count > 0 && milestoneTasks.All(t => t.Status == TaskItemStatus.Completed))
                    {
                        milestone.IsCompleted = true;
                    }
                    else
                    {
                        milestone.IsCompleted = false;
                    }
                }

                return Result.Ok(string.Format("Task status updated to {0}", newStatus));
            }
        }

        public KanbanBoard GetKanbanBoard(string teamId)
        {
            lock (_lock)
            {
                var teamTasks = _tasks.Values.Where(t => t.TeamId.Equals(teamId.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

                var board = new KanbanBoard { TeamId = teamId };
                foreach (var task in teamTasks)
                {
                    if (task.Status == TaskItemStatus.ToDo) board.ToDo.Add(task);
                    else if (task.Status == TaskItemStatus.InProgress) board.InProgress.Add(task);
                    else if (task.Status == TaskItemStatus.Completed) board.Completed.Add(task);
                }

                return board;
            }
        }

        public List<Milestone> GetMilestones(string teamId)
        {
            lock (_lock)
            {
                return _milestones.Values
                    .Where(m => m.TeamId.Equals(teamId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.SequenceOrder)
                    .ToList();
            }
        }

        public List<TaskItem> GetTasksByTeam(string teamId)
        {
            lock (_lock)
            {
                return _tasks.Values
                    .Where(t => t.TeamId.Equals(teamId.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t.DueDate)
                    .ToList();
            }
        }

        public List<TaskItem> GetTasksByStudent(string studentId)
        {
            lock (_lock)
            {
                return _tasks.Values
                    .Where(t => t.Assignments.Any(a => a.AssignedStudentId.Equals(studentId.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(t => t.DueDate)
                    .ToList();
            }
        }

        public ProjectTimeline GetProjectTimeline(string teamId)
        {
            lock (_lock)
            {
                var teamTasks = GetTasksByTeam(teamId);
                var milestones = GetMilestones(teamId);

                int totalTasks = teamTasks.Count;
                int completedTasks = teamTasks.Count(t => t.Status == TaskItemStatus.Completed);
                int inProgressTasks = teamTasks.Count(t => t.Status == TaskItemStatus.InProgress);
                int toDoTasks = teamTasks.Count(t => t.Status == TaskItemStatus.ToDo);

                double percentage = totalTasks > 0 ? Math.Round(((double)completedTasks / totalTasks) * 100.0, 1) : 0.0;

                var activeMilestone = milestones.FirstOrDefault(m => !m.IsCompleted);
                string currentMilestone = activeMilestone != null ? activeMilestone.Title : (milestones.Count > 0 ? "All Milestones Completed" : "No Milestones");

                var alerts = CheckDeadlineAlerts(teamId);

                return new ProjectTimeline
                {
                    TeamId = teamId,
                    TotalTasks = totalTasks,
                    CompletedTasks = completedTasks,
                    InProgressTasks = inProgressTasks,
                    ToDoTasks = toDoTasks,
                    CompletionPercentage = percentage,
                    TotalMilestones = milestones.Count,
                    CompletedMilestones = milestones.Count(m => m.IsCompleted),
                    CurrentMilestoneTitle = currentMilestone,
                    ActiveAlerts = alerts
                };
            }
        }

        public List<DeadlineAlert> CheckDeadlineAlerts(string teamId, int upcomingDaysThreshold = 3)
        {
            var alerts = new List<DeadlineAlert>();
            DateTime now = DateTime.UtcNow;

            lock (_lock)
            {
                var nonCompletedTasks = _tasks.Values
                    .Where(t => t.TeamId.Equals(teamId.Trim(), StringComparison.OrdinalIgnoreCase) && t.Status != TaskItemStatus.Completed);

                foreach (var task in nonCompletedTasks)
                {
                    TimeSpan diff = task.DueDate - now;
                    string assignedStudent = task.Assignments.Count > 0 ? task.Assignments[0].AssignedStudentId : "Unassigned";

                    if (diff.TotalSeconds < 0)
                    {
                        // Overdue!
                        int daysOverdue = (int)Math.Abs(diff.TotalDays) + 1;
                        alerts.Add(new DeadlineAlert
                        {
                            TaskId = task.Id,
                            TaskTitle = task.Title,
                            AssignedStudentId = assignedStudent,
                            DueDate = task.DueDate,
                            IsOverdue = true,
                            DaysRemainingOrOverdue = daysOverdue,
                            AlertMessage = string.Format("CRITICAL: Task '{0}' is OVERDUE by {1} day(s)!", task.Title, daysOverdue)
                        });
                    }
                    else if (diff.TotalDays <= upcomingDaysThreshold)
                    {
                        // Approaching deadline
                        int daysRemaining = (int)Math.Ceiling(diff.TotalDays);
                        alerts.Add(new DeadlineAlert
                        {
                            TaskId = task.Id,
                            TaskTitle = task.Title,
                            AssignedStudentId = assignedStudent,
                            DueDate = task.DueDate,
                            IsOverdue = false,
                            DaysRemainingOrOverdue = daysRemaining,
                            AlertMessage = string.Format("WARNING: Task '{0}' due in {1} day(s)!", task.Title, daysRemaining)
                        });
                    }
                }
            }

            return alerts.OrderBy(a => a.IsOverdue ? 0 : 1).ThenBy(a => a.DueDate).ToList();
        }
    }
}
