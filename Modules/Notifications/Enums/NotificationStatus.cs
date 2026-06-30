namespace Kuvox.Api.Modules.Notifications.Enums;

public enum NotificationStatus
{
    Unread = 0,
    Read = 1,
    Archived = 2,
    Deleted = 3,

    // Legacy names kept so old string enum rows remain readable.
    PENDING = Unread,
    SENT = Read,
    FAILED = Archived
}
