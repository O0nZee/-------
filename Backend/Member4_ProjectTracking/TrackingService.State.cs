using System;
using System.Collections.Generic;
using System.Linq;
using GraduationPlatform.Common;

namespace GraduationPlatform.Member4_ProjectTracking
{
    public partial class TrackingService
    {
        public List<Milestone> ExportMilestones() { lock (_lock) return _milestones.Values.ToList(); }
        public List<TaskItem> ExportTasks() { lock (_lock) return _tasks.Values.ToList(); }
        public void ImportState(IEnumerable<Milestone> milestones, IEnumerable<TaskItem> tasks)
        {
            lock (_lock)
            {
                _milestones.Clear(); _tasks.Clear();
                foreach (var milestone in milestones) _milestones.Add(milestone.Id, milestone);
                foreach (var task in tasks) _tasks.Add(task.Id, task);
            }
        }
        public TaskItem GetTask(string id) { lock (_lock) { TaskItem task; _tasks.TryGetValue(id, out task); return task; } }
        public Milestone GetMilestone(string id) { lock (_lock) { Milestone milestone; _milestones.TryGetValue(id, out milestone); return milestone; } }
        public Result UpdateTask(string id, string title, string description, TaskPriority priority, DateTime start, DateTime due, string milestoneId)
        {
            if (string.IsNullOrWhiteSpace(title) || due < start || !Enum.IsDefined(typeof(TaskPriority), priority)) return Result.Fail("Invalid task details");
            lock (_lock)
            {
                var task = GetTask(id);
                if (task == null) return Result.Fail("Task not found");
                if (milestoneId != null && (!_milestones.ContainsKey(milestoneId) || _milestones[milestoneId].TeamId != task.TeamId)) return Result.Fail("Invalid milestone for this team");
                string oldMilestone = task.MilestoneId;
                task.Title = title.Trim(); task.Description = description ?? ""; task.Priority = priority;
                task.StartDate = start; task.DueDate = due; task.MilestoneId = milestoneId;
                RecalculateMilestone(oldMilestone); RecalculateMilestone(milestoneId);
                return Result.Ok("Task updated");
            }
        }
        public Result DeleteTask(string id)
        {
            lock (_lock)
            {
                var task = GetTask(id);
                if (task == null) return Result.Fail("Task not found");
                _tasks.Remove(id); RecalculateMilestone(task.MilestoneId);
                return Result.Ok("Task removed");
            }
        }
        public Result UnassignTask(string taskId, string studentId)
        {
            lock (_lock)
            {
                var task = GetTask(taskId);
                if (task == null) return Result.Fail("Task not found");
                task.Assignments.RemoveAll(a => a.AssignedStudentId == studentId);
                return Result.Ok("Assignment removed");
            }
        }
        public void RemoveStudentAssignments(string teamId, string studentId)
        {
            lock (_lock) foreach (var task in _tasks.Values.Where(t => t.TeamId == teamId)) task.Assignments.RemoveAll(a => a.AssignedStudentId == studentId);
        }
        private void RecalculateMilestone(string id)
        {
            if (id == null || !_milestones.ContainsKey(id)) return;
            var tasks = _tasks.Values.Where(t => t.MilestoneId == id).ToList();
            _milestones[id].IsCompleted = tasks.Count > 0 && tasks.All(t => t.Status == TaskItemStatus.Completed);
        }
    }
}
