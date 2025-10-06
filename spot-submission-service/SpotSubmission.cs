public class SpotSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
}