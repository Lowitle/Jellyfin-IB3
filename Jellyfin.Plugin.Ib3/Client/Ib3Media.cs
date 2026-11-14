using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// A video of a program (an episode, an emission, a clip).
/// </summary>
public sealed class Ib3Media
{
    /// <summary>
    /// Gets or sets the id of the video.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the slug of the video (<c>emergencies/e12</c>).
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the title of the video.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description of the video.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the season, as IB3 publishes it: <c>"1"</c>, <c>"T1"</c> or an empty string.
    /// </summary>
    /// <remarks>
    /// The field is a number for some programs and a string for others —and an empty string for the
    /// documentaries—, so it is read as text and parsed by the synchronization.
    /// </remarks>
    [JsonPropertyName("season")]
    [JsonConverter(typeof(Ib3TextConverter))]
    public string? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode, as IB3 publishes it: <c>"8"</c>, <c>"100UGT-25"</c> or an empty string.
    /// </summary>
    [JsonPropertyName("episode")]
    [JsonConverter(typeof(Ib3TextConverter))]
    public string? Episode { get; set; }

    /// <summary>
    /// Gets or sets the broadcast date (<c>2025-10-18 23:50:00</c>).
    /// </summary>
    [JsonPropertyName("broadcast")]
    public string? Broadcast { get; set; }

    /// <summary>
    /// Gets or sets the date the video stops being published, or <c>null</c> when it never expires.
    /// </summary>
    [JsonPropertyName("publishOut")]
    public string? PublishOut { get; set; }

    /// <summary>
    /// Gets or sets the kind of content of IB3.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the files of the video (MP4 and subtitles).
    /// </summary>
    [JsonPropertyName("media")]
    public Ib3MediaFile? Media { get; set; }

    /// <summary>
    /// Gets or sets the artwork of the video.
    /// </summary>
    [JsonPropertyName("image")]
    public Ib3Image? Image { get; set; }

    /// <summary>
    /// Gets or sets the id of the program the video belongs to.
    /// </summary>
    [JsonPropertyName("programId")]
    public string? ProgramId { get; set; }
}

/// <summary>
/// The files of a video: the progressive MP4 and the WebVTT subtitles, published with no scheme in the
/// URL (<c>media.ib3alacarta.com/...</c>).
/// </summary>
public sealed class Ib3MediaFile
{
    /// <summary>
    /// Gets or sets the URL of the MP4.
    /// </summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>
    /// Gets or sets the size of the MP4, in bytes.
    /// </summary>
    [JsonPropertyName("filesize")]
    public long? FileSize { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the file (<c>video/mp4</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the duration of the video, in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    /// <summary>
    /// Gets or sets the URL of the WebVTT subtitles, or an empty string when the video has none.
    /// </summary>
    [JsonPropertyName("track")]
    public string? Track { get; set; }

    /// <summary>
    /// Gets or sets the URL of the trailer.
    /// </summary>
    [JsonPropertyName("trailer")]
    public string? Trailer { get; set; }
}
