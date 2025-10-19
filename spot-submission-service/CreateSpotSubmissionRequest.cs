using System.Collections.Generic;

public class CreateSpotSubmissionRequest
{
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
}
