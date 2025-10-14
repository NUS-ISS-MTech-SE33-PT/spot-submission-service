public class SpotSubmissionStorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = "submissions/";
    public int UrlExpiryMinutes { get; set; } = 15;
    public string? PublicBaseUrl { get; set; }
}
