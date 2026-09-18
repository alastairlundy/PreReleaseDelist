using System.Text.Json.Serialization;

namespace PreReleaseDelistLib.Models;

public class NuGetServiceRegistrationModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}