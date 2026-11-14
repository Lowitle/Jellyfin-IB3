using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Ib3.Client;

/// <summary>
/// The serializer settings of the IB3 API.
/// </summary>
internal static class Ib3Json
{
    /// <summary>
    /// Gets the options used to read every document of the API.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonNumberHandling.AllowReadingFromString"/> is what lets the same endpoint answer
    /// <c>"episode": 8</c> and <c>"episode": "8"</c> without a converter per field.
    /// </remarks>
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

/// <summary>
/// Reads a field that IB3 publishes as a string, as a number or as <c>null</c> (<c>season</c>,
/// <c>episode</c>) into a string.
/// </summary>
internal sealed class Ib3TextConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "1",
            JsonTokenType.False => "0",
            _ => null
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Reads the <c>seasons</c> field, which IB3 publishes as an array of numbers, as a single number or as
/// <c>null</c>.
/// </summary>
internal sealed class Ib3NumberListConverter : JsonConverter<List<int>?>
{
    /// <inheritdoc />
    public override List<int>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var seasons = new List<int>();

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                Add(ref reader, seasons);
            }

            return seasons;
        }

        Add(ref reader, seasons);
        return seasons;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<int>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var season in value)
        {
            writer.WriteNumberValue(season);
        }

        writer.WriteEndArray();
    }

    private static void Add(ref Utf8JsonReader reader, List<int> seasons)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                seasons.Add(number);
                break;
            case JsonTokenType.String when int.TryParse(
                reader.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed):
                seasons.Add(parsed);
                break;
        }
    }
}
