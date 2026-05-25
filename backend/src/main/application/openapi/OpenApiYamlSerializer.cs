using System.Text;
using System.Text.Json.Nodes;

namespace backend.main.application.openapi
{
    internal static class OpenApiYamlSerializer
    {
        public static string ConvertJsonDocumentToYaml(string json)
        {
            var root = JsonNode.Parse(json) ?? throw new InvalidOperationException(
                "OpenAPI JSON output could not be parsed."
            );

            var builder = new StringBuilder();
            WriteNode(builder, root, 0);
            return builder.ToString();
        }

        private static void WriteNode(StringBuilder builder, JsonNode? node, int indent)
        {
            switch (node)
            {
                case null:
                    builder.Append(new string(' ', indent)).Append("null").AppendLine();
                    return;
                case JsonObject jsonObject:
                    WriteObject(builder, jsonObject, indent);
                    return;
                case JsonArray jsonArray:
                    WriteArray(builder, jsonArray, indent);
                    return;
                case JsonValue jsonValue:
                    builder.Append(new string(' ', indent)).Append(FormatScalar(jsonValue)).AppendLine();
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported JSON node type '{node.GetType().Name}'."
                    );
            }
        }

        private static void WriteObject(StringBuilder builder, JsonObject jsonObject, int indent)
        {
            if (jsonObject.Count == 0)
            {
                builder.Append(new string(' ', indent)).Append("{}").AppendLine();
                return;
            }

            foreach (var property in jsonObject)
            {
                WriteProperty(builder, property.Key, property.Value, indent);
            }
        }

        private static void WriteProperty(
            StringBuilder builder,
            string key,
            JsonNode? value,
            int indent
        )
        {
            var padding = new string(' ', indent);
            var escapedKey = EscapeString(key);

            switch (value)
            {
                case null:
                    builder.Append(padding).Append(escapedKey).Append(": null").AppendLine();
                    return;
                case JsonValue jsonValue:
                    builder
                        .Append(padding)
                        .Append(escapedKey)
                        .Append(": ")
                        .Append(FormatScalar(jsonValue))
                        .AppendLine();
                    return;
                case JsonObject jsonObject when jsonObject.Count == 0:
                    builder.Append(padding).Append(escapedKey).Append(": {}").AppendLine();
                    return;
                case JsonArray jsonArray when jsonArray.Count == 0:
                    builder.Append(padding).Append(escapedKey).Append(": []").AppendLine();
                    return;
                default:
                    builder.Append(padding).Append(escapedKey).Append(':').AppendLine();
                    WriteNode(builder, value, indent + 2);
                    return;
            }
        }

        private static void WriteArray(StringBuilder builder, JsonArray jsonArray, int indent)
        {
            if (jsonArray.Count == 0)
            {
                builder.Append(new string(' ', indent)).Append("[]").AppendLine();
                return;
            }

            foreach (var item in jsonArray)
            {
                WriteArrayItem(builder, item, indent);
            }
        }

        private static void WriteArrayItem(StringBuilder builder, JsonNode? item, int indent)
        {
            var padding = new string(' ', indent);

            switch (item)
            {
                case null:
                    builder.Append(padding).Append("- null").AppendLine();
                    return;
                case JsonValue jsonValue:
                    builder.Append(padding).Append("- ").Append(FormatScalar(jsonValue)).AppendLine();
                    return;
                case JsonObject jsonObject when jsonObject.Count == 0:
                    builder.Append(padding).Append("- {}").AppendLine();
                    return;
                case JsonArray jsonArray when jsonArray.Count == 0:
                    builder.Append(padding).Append("- []").AppendLine();
                    return;
                case JsonObject jsonObject:
                    var first = true;
                    foreach (var property in jsonObject)
                    {
                        if (first && property.Value is JsonValue firstValue)
                        {
                            builder
                                .Append(padding)
                                .Append("- ")
                                .Append(EscapeString(property.Key))
                                .Append(": ")
                                .Append(FormatScalar(firstValue))
                                .AppendLine();
                        }
                        else if (first)
                        {
                            builder
                                .Append(padding)
                                .Append("- ")
                                .Append(EscapeString(property.Key))
                                .Append(':')
                                .AppendLine();
                            WriteNode(builder, property.Value, indent + 4);
                        }
                        else
                        {
                            WriteProperty(builder, property.Key, property.Value, indent + 2);
                        }

                        first = false;
                    }

                    return;
                case JsonArray jsonArray:
                    builder.Append(padding).Append('-').AppendLine();
                    WriteArray(builder, jsonArray, indent + 2);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported JSON node type '{item.GetType().Name}'."
                    );
            }
        }

        private static string FormatScalar(JsonValue value)
        {
            var primitive = value.ToJsonString();

            if (primitive is "true" or "false" or "null")
            {
                return primitive;
            }

            if (decimal.TryParse(primitive, out _))
            {
                return primitive;
            }

            if (primitive.StartsWith('"') && primitive.EndsWith('"'))
            {
                var unescaped = value.GetValue<string>();
                return EscapeString(unescaped);
            }

            return primitive;
        }

        private static string EscapeString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            if (value.All(ch => char.IsLetterOrDigit(ch) || "-_./".Contains(ch)))
            {
                return value;
            }

            return $"'{value.Replace("'", "''")}'";
        }
    }
}
