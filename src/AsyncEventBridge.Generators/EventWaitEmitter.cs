using System.Text;
using static AsyncEventBridge.Generators.GeneratorTypeSystem;
using Microsoft.CodeAnalysis;

namespace AsyncEventBridge.Generators;

internal static class EventWaitEmitter
{
    internal static void AppendMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters)
    {
        AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeTimeout: false);

        if (item.IsTyped)
        {
            AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeTimeout: false);
        }

        AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeTimeout: true);

        if (item.IsTyped)
        {
            AppendMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeTimeout: true);
        }
    }

    private static void AppendMethod(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationModel item,
        TypeParameterContext typeParameters,
        bool includePredicate,
        bool includeTimeout)
    {
        var sourceType = RenderType(typeSymbol, typeParameters);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = item.EventSymbol.Name + "Async";

        source.Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::System.Threading.Tasks.Task");

        if (item.IsTyped)
        {
            source.Append('<').Append(item.PayloadType).Append('>');
        }

        source.Append(' ').Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ").Append(sourceType).Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(item.PayloadType)
                .Append("> predicate, ");
        }

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
            .AppendLine();

        if (includePredicate)
        {
            source.AppendLine("        if (predicate is null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(predicate));")
                .AppendLine("        }")
                .AppendLine();
        }

        if (item.IsCustom)
        {
            AppendCustomBody(source, item, eventName, includePredicate, includeTimeout);
        }
        else
        {
            AppendStandardBody(source, item, eventName, includePredicate, includeTimeout);
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStandardBody(
        StringBuilder source,
        EventGenerationModel item,
        string eventName,
        bool includePredicate,
        bool includeTimeout)
    {
        var waitTypeArgument = item.IsTyped ? $"<{item.PayloadType}>" : string.Empty;

        source.Append("        return global::AsyncEventBridge.EventAwaiter.WaitAsync")
            .Append(waitTypeArgument)
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
    }

    private static void AppendCustomBody(
        StringBuilder source,
        EventGenerationModel item,
        string eventName,
        bool includePredicate,
        bool includeTimeout)
    {
        source.Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventAwaiter.WaitAsync<")
            .Append(item.PayloadType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.PayloadType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((_, eventArgs) => handler(null, eventArgs));")
            .Append("                source.")
            .Append(eventName)
            .AppendLine(" += adaptedHandler;")
            .AppendLine("            },")
            .Append("            (global::System.EventHandler<")
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
            .Append("            ")
            .AppendLine(includePredicate ? "predicate," : "null,")
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
    }
}
