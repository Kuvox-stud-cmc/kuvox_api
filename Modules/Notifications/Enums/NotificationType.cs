namespace Kuvox.Api.Modules.Notifications.Enums;

public enum NotificationType
{
    Welcome = 0,
    MediaUploaded = 1,
    MediaUploadFailed = 2,
    MediaIngested = 3,
    MediaIngestFailed = 4,
    InvitationReceived = 5,
    InvitationAccepted = 6,
    InvitationRevoked = 7,
    RoleChanged = 8,
    MemberJoined = 9,
    MemberRemoved = 10,
    StudioSettingsChanged = 11,
    ProjectAccessChanged = 12,
    MediaAccessChanged = 13,
    QuotaWarning = 14,
    QuotaExceeded = 15,
    TaskAssigned = 16,
    ReviewStatusChanged = 17,

    WELCOME_EMAIL = Welcome,
    MEDIA_UPLOADED = MediaUploaded,
    MEDIA_UPLOADING_FAILED = MediaUploadFailed,
    MEDIA_INGESTED = MediaIngested,
    MEDIA_INGESTING_FAILED = MediaIngestFailed,
    USER_INVITED = InvitationReceived,
    QUOTA_WARNING = QuotaWarning,
    QUOTA_EXCEEDED = QuotaExceeded
}
