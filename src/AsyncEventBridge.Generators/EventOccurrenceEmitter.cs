using System.Text;
using static AsyncEventBridge.Generators.GeneratorTypeSystem;
using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

internal static class EventOccurrenceEmitter
{
    internal static void AppendMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters)
    {
        AppendWait(source, typeSymbol, item, typeParameters, includeTimeout: false);
        AppendWait(source, typeSymbol, item, typeParameters, includeTimeout: true);
        AppendStream(source, typeSymbol, item, typeParameters, includeOptions: false);
        AppendStream(source, typeSymbol, item, typeParameters, includeOptions: true);
    }

    private static void AppendWait(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters,
        bool includeTimeout)
    {
        var sourceType = RenderType(typeSymbol, typeParameters, preserveNullableAnnotations: true);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = eventName.TrimStart('@') + "OccurrenceAsync";
        var occurrenceType = $"global::AsyncEventBridge.EventOccurrence<{item.SenderType}, {item.PayloadType}>";

        source.Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::System.Threading.Tasks.Task<")
            .Append(occurrenceType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includeTimeout)
        {
            source.Append("global::System.TimeSpan timeout, ");
        }

        source.Append("global::System.Threading.CancellationToken cancellationToken = default");

        if (includeTimeout)
        {
            source.Append(", global::System.TimeProvider? timeProvider = null");
        }

        source.Append(')');
        AppendMethodConstraints(source, typeParameters, preserveNullableAnnotations: true);
        source.AppendLine()
            .AppendLine("    {")
            .AppendLine("        if (source is null)")
            .AppendLine("        {")
            .AppendLine("            throw new global::System.ArgumentNullException(nameof(source));")
            .AppendLine("        }")
            .AppendLine()
            .Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventOccurrenceAwaiter.WaitAsync<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((sender, payload) => handler(sender, payload));")
            .Append("                source.")
            .Append(eventName)
            .AppendLine(" += adaptedHandler;")
            .AppendLine("            },")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> _) =>")
            .AppendLine("            {")
            .AppendLine("                if (adaptedHandler != null)")
            .AppendLine("                {")
            .Append("                    source.")
            .Append(eventName)
            .AppendLine(" -= adaptedHandler;")
            .AppendLine("                }")
            .AppendLine("            },")
            .AppendLine("            null,")
            .Append("            cancellationToken");

        if (includeTimeout)
        {
            source.AppendLine(",")
                .AppendLine("            timeout,")
                .AppendLine("            timeProvider);");
        }
        else
        {
            source.AppendLine(");");
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStream(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters,
        bool includeOptions)
    {
        var sourceType = RenderType(typeSymbol, typeParameters, preserveNullableAnnotations: true);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = eventName.TrimStart('@') + "OccurrenceStream";
        var occurrenceType = $"global::AsyncEventBridge.EventOccurrence<{item.SenderType}, {item.PayloadType}>";

        source.Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::System.Collections.Generic.IAsyncEnumerable<")
            .Append(occurrenceType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includeOptions)
        {
            source.Append("global::AsyncEventBridge.EventStreamOptions options, ");
        }

        source.Append("global::System.Threading.CancellationToken cancellationToken = default)");
        AppendMethodConstraints(source, typeParameters, preserveNullableAnnotations: true);
        source.AppendLine()
            .AppendLine("    {")
            .AppendLine("        if (source is null)")
            .AppendLine("        {")
            .AppendLine("            throw new global::System.ArgumentNullException(nameof(source));")
            .AppendLine("        }")
            .AppendLine();

        if (includeOptions)
        {
            source.AppendLine("        if (options is null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(options));")
                .AppendLine("        }")
                .AppendLine();
        }

        source.Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventOccurrenceStream.Create<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((sender, payload) => handler(sender, payload));")
            .Append("                source.")
            .Append(eventName)
            .AppendLine(" += adaptedHandler;")
            .AppendLine("            },")
            .Append("            (global::System.EventHandler<")
            .Append(item.SenderType)
            .Append(", ")
            .Append(item.PayloadType)
            .AppendLine("> _) =>")
            .AppendLine("            {")
            .AppendLine("                if (adaptedHandler != null)")
            .AppendLine("                {")
            .Append("                    source.")
            .Append(eventName)
            .AppendLine(" -= adaptedHandler;")
            .AppendLine("                }")
            .AppendLine("            },")
            .AppendLine("            null,")
            .Append("            ")
            .AppendLine(includeOptions ? "options," : "null,")
            .AppendLine("            cancellationToken);")
            .AppendLine("    }")
            .AppendLine();
    }
}
