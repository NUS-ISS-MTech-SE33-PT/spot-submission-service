public class CreatePhotoUploadUrlResponse
{
    public string UploadUrl { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
