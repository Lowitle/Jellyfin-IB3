using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// A page of the IB3 JSON API (<c>total</c>, <c>itemsPerPage</c>, <c>offset</c>, <c>items</c>).
/// </summary>
/// <typeparam name="T">Type of the items of the page.</typeparam>
public sealed class Ib3Page<T>
{
    /// <summary>
    /// Gets or sets the total number of items, in every page.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Gets or sets the size of the page.
    /// </summary>
    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; set; }

    /// <summary>
    /// Gets or sets the offset of the page.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>
    /// Gets or sets the items of the page.
    /// </summary>
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];
}

/// <summary>
/// A program of the IB3 catalog (a documentary, a series, a news magazine...).
/// </summary>
public sealed class Ib3Program
{
    /// <summary>
    /// Gets or sets the program id (a GUID), which is the key used by every other endpoint.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the slug of the program (<c>emergencies</c>), used in the web addresses.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the kind of content of IB3 (<c>video</c>, <c>audio</c>...).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the title of the program.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description of the program. It is plain text, not HTML.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the publication date of the program.
    /// </summary>
    [JsonPropertyName("release")]
    public string? Release { get; set; }

    /// <summary>
    /// Gets or sets the recommended age of the program: <c>7</c>, <c>12</c>, <c>16</c>, <c>18</c>, or
    /// <c>1</c> for «todos los públicos». It is the classification by age, not a score.
    /// </summary>
    /// <remarks>
    /// IB3 publishes it as a number or as <c>null</c>, so it is read as text like <c>season</c> and
    /// <c>episode</c>. Of the 579 programs of the catalog only 93 publish it.
    /// </remarks>
    [JsonPropertyName("rate")]
    [JsonConverter(typeof(Ib3TextConverter))]
    public string? Rate { get; set; }

    /// <summary>
    /// Gets or sets the seasons the program publishes (an array of numbers, or <c>null</c> when IB3
    /// publishes no season at all).
    /// </summary>
    [JsonPropertyName("seasons")]
    [JsonConverter(typeof(Ib3NumberListConverter))]
    public List<int>? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the artwork of the program.
    /// </summary>
    [JsonPropertyName("image")]
    public Ib3Image? Image { get; set; }

    /// <summary>
    /// Gets or sets the categories of the catalog the program is filed under.
    /// </summary>
    [JsonPropertyName("categories")]
    public List<Ib3Category>? Categories { get; set; }
}

/// <summary>
/// The artwork of a program or a video: three sizes of the same picture. The URL carries no scheme.
/// </summary>
public sealed class Ib3Image
{
    /// <summary>
    /// Gets or sets the smallest variant.
    /// </summary>
    [JsonPropertyName("small")]
    public string? Small { get; set; }

    /// <summary>
    /// Gets or sets the medium variant.
    /// </summary>
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    /// <summary>
    /// Gets or sets the biggest variant.
    /// </summary>
    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

/// <summary>
/// A category of the IB3 catalog (<c>Sèries</c>, <c>Documentals</c>, <c>Actualitat</c>...) with the
/// subcategories the program is filed under.
/// </summary>
public sealed class Ib3Category
{
    /// <summary>
    /// Gets or sets the id of the category.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the category.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the kind of content the category files (<c>video</c>, <c>audio</c>).
    /// </summary>
    /// <remarks>
    /// The catalog has a section of video and another one of audio with the same name («Actualitat»,
    /// «Destacat», «Arxiu»), so the type is what tells them apart.
    /// </remarks>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the subcategories. IB3 repeats each one many times, so the synchronization
    /// de-duplicates them.
    /// </summary>
    [JsonPropertyName("subcategories")]
    public List<Ib3Category>? Subcategories { get; set; }
}
