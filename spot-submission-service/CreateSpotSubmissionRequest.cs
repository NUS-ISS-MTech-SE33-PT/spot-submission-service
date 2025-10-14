public class CreateSpotSubmissionRequest
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? PhotoStorageKey { get; set; }
}
