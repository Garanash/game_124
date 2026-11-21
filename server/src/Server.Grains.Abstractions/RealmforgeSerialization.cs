using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace Realmforge.Server.Grains;

/// <summary>
/// Rules and domain types are plain classes shared with Unity, so they travel between silo and clients as JSON
/// instead of carrying Orleans attributes. Every Orleans host and client must register this.
/// </summary>
public static class RealmforgeSerialization
{
    private const string RealmforgeNamespacePrefix = "Realmforge.";

    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static ISerializerBuilder AddRealmforgeSerializers(this ISerializerBuilder builder)
    {
        return builder.AddJsonSerializer(isSupported: IsRealmforgeType, jsonSerializerOptions: JsonOptions);
    }

    private static bool IsRealmforgeType(Type type)
    {
        return type.Namespace?.StartsWith(RealmforgeNamespacePrefix, StringComparison.Ordinal) == true
            && !type.IsInterface
            && type.GetCustomAttributes(typeof(GenerateSerializerAttribute), inherit: false).Length == 0;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            IncludeFields = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
