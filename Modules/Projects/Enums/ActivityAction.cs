namespace Kuvox.Api.Modules.Projects.Enums;

public enum ActivityAction
{
    MediaUploaded,
    ProjectCreated,
    ExportStarted,
    ExportCompleted,
    CommandIssued,      // Natural language command used
    TimelineUpdated     // Batch manual edits
}