using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

/// <summary>
/// Normalizes the delegate shape behind an event before any emitter decides which APIs to generate.
/// </summary>
internal static class EventShapeClassifier
{
    internal static EventShape Classify(IEventSymbol eventSymbol)
    {
        if (eventSymbol.Type is not INamedTypeSymbol delegateType ||
            delegateType.TypeKind != TypeKind.Delegate ||
            delegateType.DelegateInvokeMethod is not { } invokeMethod ||
            !invokeMethod.ReturnsVoid ||
            invokeMethod.Parameters.Length != 2 ||
            invokeMethod.Parameters[0].RefKind != RefKind.None ||
            invokeMethod.Parameters[1].RefKind != RefKind.None)
        {
            return EventShape.Unsupported();
        }

        var senderType = invokeMethod.Parameters[0].Type;
        var payloadType = invokeMethod.Parameters[1].Type;
        var senderCompatible = IsAsyncLifetimeCompatible(senderType);
        var payloadCompatible = IsAsyncLifetimeCompatible(payloadType);

        if (delegateType.Name == "EventHandler" &&
            delegateType.ContainingNamespace.ToDisplayString() == "System")
        {
            if (delegateType.TypeArguments.Length == 0)
            {
                return new EventShape(
                    EventShapeKind.StandardUntyped,
                    delegateType,
                    senderType,
                    payloadType,
                    senderCompatible,
                    payloadCompatible);
            }

            if (delegateType.TypeArguments.Length == 1)
            {
                return new EventShape(
                    EventShapeKind.StandardTyped,
                    delegateType,
                    senderType,
                    payloadType,
                    senderCompatible,
                    payloadCompatible);
            }

            if (delegateType.TypeArguments.Length != 2)
            {
                return EventShape.Unsupported();
            }
        }

        return new EventShape(
            EventShapeKind.Custom,
            delegateType,
            senderType,
            payloadType,
            senderCompatible,
            payloadCompatible);
    }

    private static bool IsAsyncLifetimeCompatible(ITypeSymbol typeSymbol) =>
        !typeSymbol.IsRefLikeType &&
        (typeSymbol is not ITypeParameterSymbol typeParameter || !typeParameter.AllowsRefLikeType);
}

internal enum EventShapeKind
{
    Unsupported,
    StandardUntyped,
    StandardTyped,
    Custom,
}

internal readonly struct EventShape
{
    internal EventShape(
        EventShapeKind kind,
        INamedTypeSymbol delegateType,
        ITypeSymbol senderType,
        ITypeSymbol payloadType,
        bool isSenderAsyncCompatible,
        bool isPayloadAsyncCompatible)
    {
        Kind = kind;
        DelegateType = delegateType;
        SenderType = senderType;
        PayloadType = payloadType;
        IsSenderAsyncCompatible = isSenderAsyncCompatible;
        IsPayloadAsyncCompatible = isPayloadAsyncCompatible;
    }

    internal EventShapeKind Kind { get; }

    internal INamedTypeSymbol? DelegateType { get; }

    internal ITypeSymbol? SenderType { get; }

    internal ITypeSymbol? PayloadType { get; }

    internal bool IsSenderAsyncCompatible { get; }

    internal bool IsPayloadAsyncCompatible { get; }

    internal static EventShape Unsupported() => default;
}
