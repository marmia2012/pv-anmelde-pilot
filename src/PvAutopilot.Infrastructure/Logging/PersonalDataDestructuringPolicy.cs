using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PvAutopilot.Core.Logging;
using Serilog.Core;
using Serilog.Events;

namespace PvAutopilot.Infrastructure.Logging;

/// <summary>Maskiert mit <see cref="PersonalDataAttribute"/> markierte Properties beim Destrukturieren (<c>{@Objekt}</c>).</summary>
public sealed class PersonalDataDestructuringPolicy : IDestructuringPolicy
{
    private const string Mask = "***";
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]?> Cache = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        var properties = Cache.GetOrAdd(value.GetType(), Inspect);
        if (properties is null)
        {
            result = null;
            return false;
        }

        result = new StructureValue(
            properties.Select(p => new LogEventProperty(
                p.Name,
                p.IsDefined(typeof(PersonalDataAttribute))
                    ? new ScalarValue(Mask)
                    : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true))),
            value.GetType().Name);
        return true;
    }

    private static PropertyInfo[]? Inspect(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();
        return properties.Any(p => p.IsDefined(typeof(PersonalDataAttribute))) ? properties : null;
    }
}
