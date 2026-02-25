public class CreateSpotSubmissionResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Status { get; set; } = "pending";
    public string SubmittedBy { get; set; } = string.Empty;
}
