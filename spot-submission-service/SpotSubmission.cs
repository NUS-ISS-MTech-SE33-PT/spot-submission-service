using System.Collections.Generic;

public class SpotSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string FoodType { get; set; } = string.Empty;
    public string PlaceType { get; set; } = string.Empty;
    public string OpeningHours { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string District { get; set; } = string.Empty;
    public List<string> PhotoUrls { get; set; } = new();
    public List<string> PhotoStorageKeys { get; set; } = new();
    public bool IsCenter { get; set; }
    public ParentCenterSubmission? ParentCenter { get; set; }
    public string Status { get; set; } = "pending";
    public string SubmittedBy { get; set; } = string.Empty;
    public bool Open { get; set; } = true;

    public string ThumbnailUrl =>
        PhotoUrls.Count > 0 ? PhotoUrls[0] : string.Empty;

    public string ThumbnailStorageKey =>
        PhotoStorageKeys.Count > 0 ? PhotoStorageKeys[0] : string.Empty;
}

public class ParentCenterSubmission
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
}
