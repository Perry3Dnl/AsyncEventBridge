using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

/// <summary>
/// Normalized event information consumed by wait, stream, and occurrence emitters.
/// </summary>
internal readonly struct EventGenerationModel
{
    private EventGenerationModel(
        IEventSymbol eventSymbol,
        EventShape shape,
        string handlerType,
        string senderType,
        string payloadType,
        string accessibility)
    {
        EventSymbol = eventSymbol;
        Shape = shape;
        HandlerType = handlerType;
        SenderType = senderType;
        PayloadType = payloadType;
        Accessibility = accessibility;
    }

    internal IEventSymbol EventSymbol { get; }

    internal EventShape Shape { get; }

    internal EventShapeKind Kind => Shape.Kind;

    internal string HandlerType { get; }

    internal string SenderType { get; }

    internal string PayloadType { get; }

    internal string Accessibility { get; }

    internal bool IsTyped => Kind != EventShapeKind.StandardUntyped;

    internal bool IsStandardTyped => Kind == EventShapeKind.StandardTyped;

    internal bool IsCustom => Kind == EventShapeKind.Custom;

    internal static EventGenerationModel Create(
        IEventSymbol eventSymbol,
        EventShape shape,
        TypeParameterContext typeParameters,
        string accessibility,
        bool preserveNullableAnnotations)
    {
        if (shape.Kind == EventShapeKind.Unsupported ||
            shape.DelegateType is null ||
            shape.SenderType is null ||
            shape.PayloadType is null)
        {
            throw new ArgumentException("A supported event shape is required.", nameof(shape));
        }

        return new EventGenerationModel(
            eventSymbol,
            shape,
            GeneratorTypeSystem.RenderType(
                shape.DelegateType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                typeParameters,
                preserveNullableAnnotations),
            GeneratorTypeSystem.RenderType(
                shape.SenderType,
                typeParameters,
                preserveNullableAnnotations),
            GeneratorTypeSystem.RenderType(
                shape.PayloadType,
                typeParameters,
                preserveNullableAnnotations),
            accessibility);
    }
}
