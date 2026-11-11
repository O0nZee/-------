using System;
using System.Collections.Generic;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member4_ProjectTracking
{
    public interface ITrackingService
    {
        Result<Milestone> CreateMilestone(string teamId, string title, string description, int sequenceOrder, DateTime dueDate);

        Result<List<Milestone>> InitializeDefaultMilestones(string teamId);

        Result<TaskItem> CreateTask(string teamId, string milestoneId, string title, string description, 
            TaskPriority priority, DateTime startDate, DateTime dueDate);

        Result AssignTaskToMember(string taskId, string studentId, string studentName);

        Result UpdateTaskStatus(string taskId, TaskItemStatus newStatus);

        KanbanBoard GetKanbanBoard(string teamId);

        List<Milestone> GetMilestones(string teamId);

        List<TaskItem> GetTasksByTeam(string teamId);

        List<TaskItem> GetTasksByStudent(string studentId);

        ProjectTimeline GetProjectTimeline(string teamId);

        List<DeadlineAlert> CheckDeadlineAlerts(string teamId, int upcomingDaysThreshold = 3);
    }
}
