namespace AsyncEventBridge;

/// <summary>
/// Requests generated async event APIs for a type that cannot be annotated directly,
/// such as a type from a third-party assembly.
/// </summary>
/// <remarks>
/// Apply this attribute at assembly level. The target type must be accessible to the consuming compilation.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class GenerateAsyncEventsForAttribute : Attribute
{
    /// <summary>
    /// Initializes a new generation request for <paramref name="targetType"/>.
    /// </summary>
    public GenerateAsyncEventsForAttribute(Type targetType)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }

    /// <summary>
    /// Gets the event source type for which async facade methods should be generated.
    /// </summary>
    public Type TargetType { get; }
}
