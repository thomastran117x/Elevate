using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace backend.main.shared.attributes.validation;

public static class RequestTextJsonMetadata
{
    public static void Configure(JsonSerializerOptions options)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(ApplyNormalization);
        options.TypeInfoResolver = resolver;
    }

    private static void ApplyNormalization(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            var classification = property.AttributeProvider?
                .GetCustomAttributes(typeof(RequestTextAttribute), inherit: true)
                .OfType<RequestTextAttribute>()
                .SingleOrDefault();

            if (classification?.Kind is not (
                RequestTextKind.HumanSingleLine or RequestTextKind.HumanMultiline))
            {
                continue;
            }

            var originalSetter = property.Set;
            if (originalSetter is null)
                continue;

            property.Set = (target, value) => originalSetter(target, NormalizeValue(value));
        }
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        string text => ContentTextPolicy.Normalize(text),
        List<string> values => values.Select(NormalizeOptional).ToList(),
        string[] values => values.Select(NormalizeOptional).ToArray(),
        _ => value,
    };

    private static string? NormalizeOptional(string? value) =>
        value is null ? null : ContentTextPolicy.Normalize(value);
}
