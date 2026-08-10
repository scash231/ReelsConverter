using System.Text.Json.Serialization;

namespace ReelsConverterUI.Models;

public sealed class MetadataResponse
{
    [JsonPropertyName("title")]        public string Title        { get; set; } = "";
    [JsonPropertyName("description")]  public string Description  { get; set; } = "";
    [JsonPropertyName("tags")]         public List<string> Tags   { get; set; } = [];
    [JsonPropertyName("thumbnail")]    public string Thumbnail    { get; set; } = "";
    [JsonPropertyName("duration")]     public double Duration     { get; set; }
    [JsonPropertyName("uploader")]     public string Uploader     { get; set; } = "";
    [JsonPropertyName("artist")]       public string? Artist      { get; set; }
    [JsonPropertyName("album")]        public string? Album       { get; set; }
    [JsonPropertyName("is_music")]     public bool IsMusic        { get; set; }
    [JsonPropertyName("release_year")] public string? ReleaseYear { get; set; }
    [JsonPropertyName("track_count")]  public int? TrackCount     { get; set; }
}

public sealed class JobCreateResponse
{
    [JsonPropertyName("job_id")] public string JobId { get; set; } = "";
}

public sealed class JobStatus
{
    [JsonPropertyName("id")]       public string Id       { get; set; } = "";
    [JsonPropertyName("status")]   public string Status   { get; set; } = "";
    [JsonPropertyName("progress")] public int    Progress { get; set; }
    [JsonPropertyName("message")]  public string Message  { get; set; } = "";
    [JsonPropertyName("error")]    public string? Error   { get; set; }
    [JsonPropertyName("eta")]      public int?    Eta     { get; set; }
    [JsonPropertyName("speed")]    public string? Speed   { get; set; }
    [JsonPropertyName("result")]   public Dictionary<string, object>? Result { get; set; }
}
