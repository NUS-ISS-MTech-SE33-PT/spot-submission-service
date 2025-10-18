public class SpotSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string? PhotoStorageKey { get; set; }
    public string Status { get; set; } = "pending";
    public string SubmittedBy { get; set; } = string.Empty;
}
