using System.Text.Json.Serialization;
namespace MapCorleon.Import;

internal sealed class MapGenieDocument
{
    [JsonPropertyName("map")] public MapGenieMap Map { get; set; } = new();
    [JsonPropertyName("mapConfig")] public MapGenieConfig Config { get; set; } = new();
    [JsonPropertyName("groups")] public List<MapGenieGroup> Groups { get; set; } = [];
    [JsonPropertyName("locations")] public List<MapGenieLocation> Locations { get; set; } = [];
}
internal sealed class MapGenieMap { [JsonPropertyName("id")] public long Id { get; set; } [JsonPropertyName("title")] public string Title { get; set; } = "Imported Map"; }
internal sealed class MapGenieConfig
{
    [JsonPropertyName("initial_zoom")] public double InitialZoom { get; set; }
    [JsonPropertyName("start_lat")] public double StartLat { get; set; }
    [JsonPropertyName("start_lng")] public double StartLng { get; set; }
    [JsonPropertyName("tile_sets")] public List<MapGenieTileSet> TileSets { get; set; } = [];
}
internal sealed class MapGenieTileSet { [JsonPropertyName("pattern")] public string? Pattern { get; set; } [JsonPropertyName("min_zoom")] public int MinZoom { get; set; } [JsonPropertyName("max_zoom")] public int MaxZoom { get; set; } }
internal sealed class MapGenieGroup { [JsonPropertyName("id")] public long Id { get; set; } [JsonPropertyName("title")] public string Title { get; set; } = "Group"; [JsonPropertyName("categories")] public List<MapGenieCategory> Categories { get; set; } = []; }
internal sealed class MapGenieCategory { [JsonPropertyName("id")] public long Id { get; set; } [JsonPropertyName("group_id")] public long GroupId { get; set; } [JsonPropertyName("title")] public string Title { get; set; } = "Category"; [JsonPropertyName("icon")] public string? Icon { get; set; } [JsonPropertyName("visible")] public bool Visible { get; set; } = true; }
internal sealed class MapGenieLocation { [JsonPropertyName("id")] public long Id { get; set; } [JsonPropertyName("category_id")] public long CategoryId { get; set; } [JsonPropertyName("title")] public string Title { get; set; } = "Location"; [JsonPropertyName("description")] public string? Description { get; set; } [JsonPropertyName("latitude")] public double Latitude { get; set; } [JsonPropertyName("longitude")] public double Longitude { get; set; } [JsonPropertyName("media")] public List<MapGenieMedia> Media { get; set; } = []; }
internal sealed class MapGenieMedia { [JsonPropertyName("id")] public long Id { get; set; } [JsonPropertyName("title")] public string? Title { get; set; } [JsonPropertyName("url")] public string? Url { get; set; } [JsonPropertyName("file_name")] public string? FileName { get; set; } [JsonPropertyName("mime_type")] public string? MimeType { get; set; } }
