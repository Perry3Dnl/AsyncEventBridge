using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace AsyncEventBridge.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class AsyncEventBridgeGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "AsyncEventBridge.GenerateAsyncEventsAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targetTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (_, _) => true,
            static (generatorContext, _) => (INamedTypeSymbol)generatorContext.TargetSymbol);

        context.RegisterSourceOutput(targetTypes, static (productionContext, typeSymbol) =>
        {
            GenerateForType(productionContext, typeSymbol);
        });
    }

    private static void GenerateForType(SourceProductionContext context, INamedTypeSymbol typeSymbol)
    {
        if (!CanGenerateForType(typeSymbol))
        {
            return;
        }

        var events = GetEventsForGeneration(typeSymbol);
        var source = new StringBuilder();
        var hasSupportedEvent = false;

        source.AppendLine("#nullable enable")
            .AppendLine("namespace AsyncEventBridge;")
            .AppendLine();

        var extensionAccessibility = typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        source.Append(extensionAccessibility)
            .Append(" static class ")
            .Append(GetExtensionClassName(typeSymbol))
            .AppendLine()
            .AppendLine("{");

        foreach (var eventSymbol in events)
        {
            if (!TryGetEventArgsType(eventSymbol, out var eventArgsType, out var isGenericEventHandler))
            {
                continue;
            }

            if (!IsAccessibleFromExtension(eventSymbol) || eventSymbol.IsStatic)
            {
                continue;
            }

            hasSupportedEvent = true;
            AppendWaitMethods(source, typeSymbol, eventSymbol, eventArgsType, isGenericEventHandler);
            AppendStreamMethods(source, typeSymbol, eventSymbol, eventArgsType, isGenericEventHandler);
        }

        source.AppendLine("}");

        if (!hasSupportedEvent)
        {
            return;
        }

        var hintName = GetHintName(typeSymbol) + ".AsyncEvents.g.cs";
        context.AddSource(hintName, SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static IEnumerable<IEventSymbol> GetEventsForGeneration(INamedTypeSymbol typeSymbol)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        INamedTypeSymbol? current = typeSymbol;
        var isTargetType = true;

        while (current is not null)
        {
            if (!isTargetType && HasGenerateAsyncEventsAttribute(current) && CanGenerateForType(current))
            {
                yield break;
            }

            foreach (var eventSymbol in current.GetMembers().OfType<IEventSymbol>())
            {
                if (!seenNames.Add(eventSymbol.Name))
                {
                    continue;
                }

                if (isTargetType || eventSymbol.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return eventSymbol;
                }
            }

            isTargetType = false;
            current = current.BaseType;
        }
    }

    private static void AppendWaitMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        IEventSymbol eventSymbol,
        string eventArgsType,
        bool isGenericEventHandler)
    {
        var sourceType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var eventName = EscapeIdentifier(eventSymbol.Name);
        var methodName = eventSymbol.Name + "Async";
        var handlerType = isGenericEventHandler
            ? $"global::System.EventHandler<{eventArgsType}>"
            : "global::System.EventHandler";
        var waitTypeArgument = isGenericEventHandler ? $"<{eventArgsType}>" : string.Empty;

        AppendMethod(
            source,
            sourceType,
            eventName,
            methodName,
            eventArgsType,
            handlerType,
            waitTypeArgument,
            isGenericEventHandler,
            includePredicate: false,
            includeTimeout: false);

        if (isGenericEventHandler)
        {
            AppendMethod(
                source,
                sourceType,
                eventName,
                methodName,
                eventArgsType,
                handlerType,
                waitTypeArgument,
                isGenericEventHandler,
                includePredicate: true,
                includeTimeout: false);
        }

        AppendMethod(
            source,
            sourceType,
            eventName,
            methodName,
            eventArgsType,
            handlerType,
            waitTypeArgument,
            isGenericEventHandler,
            includePredicate: false,
            includeTimeout: true);

        if (isGenericEventHandler)
        {
            AppendMethod(
                source,
                sourceType,
                eventName,
                methodName,
                eventArgsType,
                handlerType,
                waitTypeArgument,
                isGenericEventHandler,
                includePredicate: true,
                includeTimeout: true);
        }
    }

    private static void AppendStreamMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        IEventSymbol eventSymbol,
        string eventArgsType,
        bool isGenericEventHandler)
    {
        var sourceType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var eventName = EscapeIdentifier(eventSymbol.Name);
        var methodName = eventSymbol.Name + "Stream";
        var handlerType = isGenericEventHandler
            ? $"global::System.EventHandler<{eventArgsType}>"
            : "global::System.EventHandler";

        AppendStreamMethod(
            source,
            sourceType,
            eventName,
            methodName,
            eventArgsType,
            handlerType,
            isGenericEventHandler,
            includePredicate: false,
            includeOptions: false);

        AppendStreamMethod(
            source,
            sourceType,
            eventName,
            methodName,
            eventArgsType,
            handlerType,
            isGenericEventHandler,
            includePredicate: false,
            includeOptions: true);

        if (isGenericEventHandler)
        {
            AppendStreamMethod(
                source,
                sourceType,
                eventName,
                methodName,
                eventArgsType,
                handlerType,
                isGenericEventHandler,
                includePredicate: true,
                includeOptions: false);

            AppendStreamMethod(
                source,
                sourceType,
                eventName,
                methodName,
                eventArgsType,
                handlerType,
                isGenericEventHandler,
                includePredicate: true,
                includeOptions: true);
        }
    }

    private static void AppendMethod(
        StringBuilder source,
        string sourceType,
        string eventName,
        string methodName,
        string eventArgsType,
        string handlerType,
        string waitTypeArgument,
        bool isGenericEventHandler,
        bool includePredicate,
        bool includeTimeout)
    {
        source.Append("    public static global::System.Threading.Tasks.Task");

        if (isGenericEventHandler)
        {
            source.Append('<')
                .Append(eventArgsType)
                .Append('>');
        }

        source.Append(' ')
            .Append(methodName)
            .Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(eventArgsType)
                .Append("> predicate, ");
        }

        if (includeTimeout)
        {
            source.Append("global::System.TimeSpan timeout, ");
        }

        source.AppendLine("global::System.Threading.CancellationToken cancellationToken = default)")
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

        source.Append("        return global::AsyncEventBridge.EventAwaiter.WaitAsync")
            .Append(waitTypeArgument)
            .AppendLine("(")
            .Append("            (")
            .Append(handlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" += handler,")
            .Append("            (")
            .Append(handlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" -= handler,")
            .Append("            ")
            .AppendLine(includePredicate ? "predicate," : "null,")
            .Append("            cancellationToken");

        if (includeTimeout)
        {
            source.AppendLine(",")
                .AppendLine("            timeout);");
        }
        else
        {
            source.AppendLine(");");
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStreamMethod(
        StringBuilder source,
        string sourceType,
        string eventName,
        string methodName,
        string eventArgsType,
        string handlerType,
        bool isGenericEventHandler,
        bool includePredicate,
        bool includeOptions)
    {
        source.Append("    public static global::System.Collections.Generic.IAsyncEnumerable<")
            .Append(eventArgsType)
            .Append("> ")
            .Append(methodName)
            .Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(eventArgsType)
                .Append("> predicate, ");
        }

        if (includeOptions)
        {
            source.Append("global::AsyncEventBridge.EventStreamOptions options, ");
        }

        source.AppendLine("global::System.Threading.CancellationToken cancellationToken = default)")
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

        source.Append("        return global::AsyncEventBridge.EventStream.Create")
            .Append(isGenericEventHandler ? $"<{eventArgsType}>" : string.Empty)
            .AppendLine("(")
            .Append("            (")
            .Append(handlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" += handler,")
            .Append("            (")
            .Append(handlerType)
            .Append(" handler) => source.")
            .Append(eventName)
            .AppendLine(" -= handler,")
            .Append("            ")
            .AppendLine(includePredicate ? "predicate," : "null,")
            .Append("            ")
            .AppendLine(includeOptions ? "options," : "null,")
            .AppendLine("            cancellationToken);")
            .AppendLine("    }")
            .AppendLine();
    }

    private static bool TryGetEventArgsType(
        IEventSymbol eventSymbol,
        out string eventArgsType,
        out bool isGenericEventHandler)
    {
        eventArgsType = string.Empty;
        isGenericEventHandler = false;

        if (eventSymbol.Type is not INamedTypeSymbol delegateType ||
            delegateType.Name != "EventHandler" ||
            delegateType.ContainingNamespace.ToDisplayString() != "System")
        {
            return false;
        }

        if (delegateType.TypeArguments.Length == 0)
        {
            eventArgsType = "global::System.EventArgs";
            return true;
        }

        if (delegateType.TypeArguments.Length == 1)
        {
            eventArgsType = delegateType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            isGenericEventHandler = true;
            return true;
        }

        return false;
    }

    private static bool IsAccessibleFromExtension(IEventSymbol eventSymbol) =>
        eventSymbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool CanGenerateForType(INamedTypeSymbol typeSymbol) =>
        typeSymbol.TypeParameters.Length == 0 && typeSymbol.ContainingType is null;

    private static bool HasGenerateAsyncEventsAttribute(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == AttributeMetadataName);

    private static string GetExtensionClassName(INamedTypeSymbol typeSymbol)
    {
        var builder = new StringBuilder();

        if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            AppendEncodedName(builder, typeSymbol.ContainingNamespace.ToDisplayString());
            builder.Append("_DOT_");
        }

        AppendEncodedName(builder, typeSymbol.Name);
        builder.Append("AsyncEventExtensions");
        return builder.ToString();
    }

    private static void AppendEncodedName(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '.':
                    builder.Append("_DOT_");
                    break;
                case '_':
                    builder.Append("__");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }
    }

    private static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;

    private static string GetHintName(INamedTypeSymbol typeSymbol)
    {
        var value = typeSymbol.ToDisplayString();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}
