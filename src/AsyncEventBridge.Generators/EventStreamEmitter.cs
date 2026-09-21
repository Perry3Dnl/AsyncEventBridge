using System.Text;
using static AsyncEventBridge.Generators.GeneratorTypeSystem;
using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

internal static class EventStreamEmitter
{
    internal static void AppendMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters)
    {
        AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeOptions: false);
        AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeOptions: true);

        if (item.IsTyped)
        {
            AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeOptions: false);
            AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeOptions: true);
        }
    }

    private static void AppendMethod(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters,
        bool includePredicate,
        bool includeOptions)
    {
        var sourceType = RenderType(typeSymbol, typeParameters);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = item.EventSymbol.Name + "Stream";

        source.Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::System.Collections.Generic.IAsyncEnumerable<")
            .Append(item.PayloadType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ").Append(sourceType).Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(item.PayloadType)
                .Append("> predicate, ");
        }

        if (includeOptions)
        {
            source.Append("global::AsyncEventBridge.EventStreamOptions options, ");
        }

        source.Append("global::System.Threading.CancellationToken cancellationToken = default)");
        AppendMethodConstraints(source, typeParameters);
        source.AppendLine()
            .AppendLine("    {")
            .AppendLine("        if (source is null)")
            .AppendLine("        {")
            .AppendLine("            throw new global::System.ArgumentNullException(nameof(source));")
            .AppendLine("        }")
            .AppendLine();

        if (includePredicate)
        {
            source.AppendLine("        if (predicate is null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(predicate));")
                .AppendLine("        }")
                .AppendLine();
        }

        if (includeOptions)
        {
            source.AppendLine("        if (options is null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(options));")
                .AppendLine("        }")
                .AppendLine();
        }

        if (item.IsCustom)
        {
            AppendCustomBody(source, item, eventName, includePredicate, includeOptions);
        }
        else
        {
            AppendStandardBody(source, item, eventName, includePredicate, includeOptions);
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStandardBody(
        StringBuilder source,
        EventGenerationModel item,
        string eventName,
        bool includePredicate,
        bool includeOptions)
    {
        source.Append("        return global::AsyncEventBridge.EventStream.Create")
            .Append(item.IsTyped ? $"<{item.PayloadType}>" : string.Empty)
            .AppendLine("(")
            .Append("            (")
            .Append(item.HandlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" += handler,")
            .Append("            (")
            .Append(item.HandlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" -= handler,")
            .Append("            ")
            .AppendLine(includePredicate ? "predicate," : "null,")
            .Append("            ")
            .AppendLine(includeOptions ? "options," : "null,")
            .AppendLine("            cancellationToken);");
    }

    private static void AppendCustomBody(
        StringBuilder source,
        EventGenerationModel item,
        string eventName,
        bool includePredicate,
        bool includeOptions)
    {
        source.Append("        var adaptedHandlers = new global::System.Collections.Concurrent.ConcurrentDictionary<")
            .Append("global::System.EventHandler<")
            .Append(item.PayloadType)
            .Append(">, ")
            .Append(item.HandlerType)
            .AppendLine(">();")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventStream.Create<")
            .Append(item.PayloadType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                var adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((_, eventArgs) => handler(null, eventArgs));")
            .AppendLine("                if (!adaptedHandlers.TryAdd(handler, adaptedHandler))")
            .AppendLine("                {")
            .AppendLine("                    throw new global::System.InvalidOperationException(\"The event handler adapter was already registered.\");")
            .AppendLine("                }")
            .Append("                source.")
            .Append(eventName)
            .AppendLine(" += adaptedHandler;")
            .AppendLine("            },")
            .Append("            (global::System.EventHandler<")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                if (adaptedHandlers.TryRemove(handler, out ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler))")
            .AppendLine("                {")
            .Append("                    source.")
            .Append(eventName)
            .AppendLine(" -= adaptedHandler;")
            .AppendLine("                }")
            .AppendLine("            },")
            .Append("            ")
            .AppendLine(includePredicate ? "predicate," : "null,")
            .Append("            ")
            .AppendLine(includeOptions ? "options," : "null,")
            .AppendLine("            cancellationToken);");
    }
}
