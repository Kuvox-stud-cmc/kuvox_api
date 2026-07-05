using Kuvox.Api.Modules.Tasks.Models;
using Microsoft.EntityFrameworkCore;

namespace Kuvox.Api.Modules.Tasks.Repositories;

internal sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public const string Schema = "tasks";

    public DbSet<TaskIssue> Issues => Set<TaskIssue>();

    public DbSet<TaskComment> Comments => Set<TaskComment>();

    public DbSet<TaskActivity> Activities => Set<TaskActivity>();

    public DbSet<TaskAssignee> Assignees => Set<TaskAssignee>();

    public DbSet<TaskMilestone> Milestones => Set<TaskMilestone>();

    public DbSet<TaskLabel> Labels => Set<TaskLabel>();

    public DbSet<TaskIssueLabel> IssueLabels => Set<TaskIssueLabel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TaskIssue>(entity =>
        {
            entity.ToTable("task_issues");
            entity.HasKey(issue => issue.Id);
            entity.Property(issue => issue.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(issue => issue.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(issue => issue.Title).HasMaxLength(240).IsRequired();
            entity.Property(issue => issue.Description).HasMaxLength(4000);
            entity.HasIndex(issue => issue.StudioId);
            entity.HasIndex(issue => issue.ProjectId);
            entity.HasIndex(issue => issue.ParentTaskIssueId);
            entity.HasIndex(issue => issue.MilestoneId);
            entity.HasIndex(issue => new { issue.StudioId, issue.Status });

            entity.HasOne(issue => issue.ParentTaskIssue)
                .WithMany(issue => issue.Children)
                .HasForeignKey(issue => issue.ParentTaskIssueId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(issue => issue.Milestone)
                .WithMany()
                .HasForeignKey(issue => issue.MilestoneId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.ToTable("task_comments");
            entity.HasKey(comment => comment.Id);
            entity.Property(comment => comment.Body).HasMaxLength(4000).IsRequired();
            entity.HasIndex(comment => comment.StudioId);
            entity.HasIndex(comment => comment.TaskIssueId);
            entity.HasIndex(comment => comment.AuthorUserId);
            entity.HasOne(comment => comment.TaskIssue)
                .WithMany(issue => issue.Comments)
                .HasForeignKey(comment => comment.TaskIssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskActivity>(entity =>
        {
            entity.ToTable("task_activities");
            entity.HasKey(activity => activity.Id);
            entity.Property(activity => activity.Action).HasMaxLength(80).IsRequired();
            entity.Property(activity => activity.Summary).HasMaxLength(500).IsRequired();
            entity.Property(activity => activity.MetadataJson).HasMaxLength(4000);
            entity.HasIndex(activity => activity.StudioId);
            entity.HasIndex(activity => activity.TaskIssueId);
            entity.HasIndex(activity => activity.ActorUserId);
            entity.HasOne(activity => activity.TaskIssue)
                .WithMany(issue => issue.Activities)
                .HasForeignKey(activity => activity.TaskIssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskAssignee>(entity =>
        {
            entity.ToTable("task_assignees");
            entity.HasKey(assignee => new { assignee.TaskIssueId, assignee.UserId });
            entity.HasIndex(assignee => assignee.UserId);
            entity.HasOne(assignee => assignee.TaskIssue)
                .WithMany(issue => issue.Assignees)
                .HasForeignKey(assignee => assignee.TaskIssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskMilestone>(entity =>
        {
            entity.ToTable("task_milestones");
            entity.HasKey(milestone => milestone.Id);
            entity.Property(milestone => milestone.Title).HasMaxLength(200).IsRequired();
            entity.Property(milestone => milestone.Description).HasMaxLength(2000);
            entity.Property(milestone => milestone.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(milestone => milestone.StudioId);
            entity.HasIndex(milestone => new { milestone.StudioId, milestone.Title }).IsUnique();
        });

        modelBuilder.Entity<TaskLabel>(entity =>
        {
            entity.ToTable("task_labels");
            entity.HasKey(label => label.Id);
            entity.Property(label => label.Name).HasMaxLength(80).IsRequired();
            entity.Property(label => label.Color).HasMaxLength(32).IsRequired();
            entity.HasIndex(label => label.StudioId);
            entity.HasIndex(label => new { label.StudioId, label.Name }).IsUnique();
        });

        modelBuilder.Entity<TaskIssueLabel>(entity =>
        {
            entity.ToTable("task_issue_labels");
            entity.HasKey(issueLabel => new { issueLabel.TaskIssueId, issueLabel.TaskLabelId });
            entity.HasIndex(issueLabel => issueLabel.TaskLabelId);
            entity.HasOne(issueLabel => issueLabel.TaskIssue)
                .WithMany(issue => issue.Labels)
                .HasForeignKey(issueLabel => issueLabel.TaskIssueId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(issueLabel => issueLabel.TaskLabel)
                .WithMany()
                .HasForeignKey(issueLabel => issueLabel.TaskLabelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
