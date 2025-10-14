public class PhotoUploadDescriptor
{
    public required string UploadUrl { get; init; }
    public required string FileUrl { get; init; }
    public required string StorageKey { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
