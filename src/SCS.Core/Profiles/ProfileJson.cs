using System.Text;
using System.Text.Json;

namespace SCS.Core;

public static class ProfileJson
{
    public static string Serialize(ProfileRecord profile)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            json.WriteStartObject(); json.WriteNumber("schemaVersion", 1); json.WriteString("profileId", profile.Id.ToString());
            json.WriteString("displayName", profile.DisplayName); json.WriteString("catalogVersion", profile.CatalogVersion);
            json.WriteStartObject("values");
            foreach (var (id, value) in profile.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!value.IsValid) throw new InvalidOperationException("Cannot serialize an invalid setting value.");
                if (value.Number.HasValue) json.WriteNumber(id, value.Number.Value); else json.WriteBoolean(id, value.Boolean!.Value);
            }
            json.WriteEndObject(); json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ProfileRecord Deserialize(string text)
    {
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("A profile must be a JSON object.");
        if (root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count()) throw new FormatException("Duplicate profile metadata fields.");
        if (Required(root, "schemaVersion", JsonValueKind.Number).GetInt32() != 1) throw new FormatException("Unsupported profile schema.");
        var identity = Required(root, "profileId", JsonValueKind.String).GetString();
        if (!Enum.TryParse<ProfileId>(identity, out var id) || !Enum.IsDefined(id) || id == ProfileId.Vanilla || identity != id.ToString())
            throw new FormatException("Only the four custom profile identities can be imported.");
        var values = new Dictionary<string, SettingValue>(StringComparer.Ordinal);
        foreach (var item in Required(root, "values", JsonValueKind.Object).EnumerateObject())
        {
            var value = item.Value.ValueKind switch
            {
                JsonValueKind.Number => SettingValue.Numeric(item.Value.GetDecimal()),
                JsonValueKind.True => SettingValue.Logical(true), JsonValueKind.False => SettingValue.Logical(false),
                _ => throw new FormatException("Only decimal and boolean setting values are supported.")
            };
            if (!values.TryAdd(item.Name, value)) throw new FormatException("Duplicate setting: " + item.Name);
        }
        return ProfileRecord.CreateCustom(id, Required(root, "displayName", JsonValueKind.String).GetString()!, Required(root, "catalogVersion", JsonValueKind.String).GetString()!, values);
    }

    private static JsonElement Required(JsonElement root, string name, JsonValueKind kind)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new FormatException($"Profile field '{name}' is required and must be {kind}.");
        return value;
    }
}
