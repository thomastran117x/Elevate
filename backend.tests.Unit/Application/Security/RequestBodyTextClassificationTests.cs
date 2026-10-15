using System.Collections;
using System.Reflection;

using backend.main.shared.attributes.validation;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;

namespace backend.tests.Unit.Application.Security;

public class RequestBodyTextClassificationTests
{
    [Fact]
    public void FromBodyStringProperties_ShouldBeClassifiedOrExplicitlyExcluded()
    {
        var bodyTypes = typeof(backend.main.features.auth.AuthController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null)
            .Select(parameter => Nullable.GetUnderlyingType(parameter.ParameterType)
                ?? parameter.ParameterType)
            .Distinct()
            .ToList();

        var unclassified = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<Type>();
        foreach (var bodyType in bodyTypes)
            FindUnclassifiedProperties(bodyType, visited, unclassified);

        unclassified.Should().BeEmpty(
            "every JSON body string must opt into human-text handling or a reviewed exclusion. "
            + "Unclassified: {0}",
            string.Join(", ", unclassified));
    }

    private static void FindUnclassifiedProperties(
        Type type,
        HashSet<Type> visited,
        ISet<string> unclassified)
    {
        type = UnwrapCollection(type);
        if (!visited.Add(type) || type.Namespace?.StartsWith("backend.main", StringComparison.Ordinal) != true)
            return;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsStringOrStringCollection(property.PropertyType))
            {
                if (property.GetCustomAttribute<RequestTextAttribute>(inherit: true) is null)
                {
                    unclassified.Add($"{property.DeclaringType!.FullName}.{property.Name}");
                }

                continue;
            }

            var nested = UnwrapCollection(property.PropertyType);
            if (nested != property.PropertyType || nested.IsClass)
                FindUnclassifiedProperties(nested, visited, unclassified);
        }
    }

    private static bool IsStringOrStringCollection(Type type)
    {
        if (type == typeof(string))
            return true;

        return type != typeof(string)
            && typeof(IEnumerable).IsAssignableFrom(type)
            && UnwrapCollection(type) == typeof(string);
    }

    private static Type UnwrapCollection(Type type)
    {
        if (type.IsArray)
            return type.GetElementType()!;

        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments()[0];

        return type;
    }
}
