public class CreatePhotoUploadUrlResponse
{
    public string UploadUrl { get; set; } = string.Empty;
    public Dictionary<string, string> UploadFields { get; set; } = new(StringComparer.Ordinal);
    public string PhotoUrl { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
