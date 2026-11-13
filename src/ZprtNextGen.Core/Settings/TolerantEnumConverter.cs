namespace ZprtNextGen.Core.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Терпимый конвертер enum: неизвестное значение не роняет загрузку конфига,
/// а заменяется значением по умолчанию.
/// </summary>
internal sealed class TolerantEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (Enum.TryParse<T>(text, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                return parsed;

            return default;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            var parsed = (T)Enum.ToObject(typeof(T), number);
            return Enum.IsDefined(parsed) ? parsed : default;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
