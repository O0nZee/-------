using System;
using System.Collections.Generic;

namespace GraduationPlatform.Member4_ProjectTracking
{
    public enum TaskItemStatus
    {
        ToDo = 0,
        InProgress = 1,
        Completed = 2
    }

    public enum TaskPriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Urgent = 4
    }

    public class Milestone
    {
        public string Id { get; set; }
        public string TeamId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int SequenceOrder { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsCompleted { get; set; }

        public Milestone()
        {
            Id = Guid.NewGuid().ToString("N");
            IsCompleted = false;
        }
    }

    public class TaskAssignment
    {
        public string Id { get; set; }
        public string TaskId { get; set; }
        public string AssignedStudentId { get; set; }
        public string StudentName { get; set; }
        public DateTime AssignedAt { get; set; }

        public TaskAssignment()
        {
            Id = Guid.NewGuid().ToString("N");
            AssignedAt = DateTime.UtcNow;
        }
    }

    public class TaskItem
    {
        public string Id { get; set; }
        public string MilestoneId { get; set; }
        public string TeamId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public TaskItemStatus Status { get; set; }
        public TaskPriority Priority { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<TaskAssignment> Assignments { get; set; }

        public TaskItem()
        {
            Id = Guid.NewGuid().ToString("N");
            Status = TaskItemStatus.ToDo;
            Priority = TaskPriority.Medium;
            CreatedAt = DateTime.UtcNow;
            StartDate = DateTime.UtcNow;
            DueDate = DateTime.UtcNow.AddDays(7);
            Assignments = new List<TaskAssignment>();
        }
    }

    public class DeadlineAlert
    {
        public string TaskId { get; set; }
        public string TaskTitle { get; set; }
        public string AssignedStudentId { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsOverdue { get; set; }
        public int DaysRemainingOrOverdue { get; set; }
        public string AlertMessage { get; set; }
    }

    public class KanbanBoard
    {
        public string TeamId { get; set; }
        public List<TaskItem> ToDo { get; set; }
        public List<TaskItem> InProgress { get; set; }
        public List<TaskItem> Completed { get; set; }

        public KanbanBoard()
        {
            ToDo = new List<TaskItem>();
            InProgress = new List<TaskItem>();
            Completed = new List<TaskItem>();
        }
    }

    public class ProjectTimeline
    {
        public string TeamId { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int ToDoTasks { get; set; }
        public double CompletionPercentage { get; set; } // 0.0% to 100.0%
        public int TotalMilestones { get; set; }
        public int CompletedMilestones { get; set; }
        public string CurrentMilestoneTitle { get; set; }
        public List<DeadlineAlert> ActiveAlerts { get; set; }

        public ProjectTimeline()
        {
            ActiveAlerts = new List<DeadlineAlert>();
        }
    }
}
