public class PhotoUploadDescriptor
{
    public required string UploadUrl { get; init; }
    public IReadOnlyDictionary<string, string> UploadFields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public required string FileUrl { get; init; }
    public required string StorageKey { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
