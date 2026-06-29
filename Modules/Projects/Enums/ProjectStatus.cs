namespace Kuvox.Api.Modules.Projects.Enums;

public enum ProjectStatus
{
    /// <summary>
    /// The project was just created. It might not have any media or edits yet.
    /// </summary>
    Draft,

    /// <summary>
    /// The project is actively being edited 
    /// </summary>
    InProgress,

    /// <summary>
    /// The project is finished
    /// </summary>
    Completed,

    /// <summary>
    /// The project is hidden from the main active view and is read-only.
    /// Note: This is different from the Trash/Recycle Bin, which uses the DeletedAt property
    /// </summary>
    Archived
}