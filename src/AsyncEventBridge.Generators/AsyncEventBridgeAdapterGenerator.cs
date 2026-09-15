using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace AsyncEventBridge.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class AsyncEventBridgeAdapterGenerator : IIncrementalGenerator
{
    private const string DirectAttributeMetadataName = "AsyncEventBridge.GenerateAsyncEventsAttribute";
    private const string TargetAttributeMetadataName = "AsyncEventBridge.GenerateAsyncEventsForAttribute";

    private static readonly DiagnosticDescriptor UnsupportedEventDelegate = new(
        id: "AEB001",
        title: "Unsupported event delegate",
        messageFormat: "Event '{0}.{1}' uses unsupported delegate type '{2}'. Generated event APIs require EventHandler, EventHandler<TPayload>, EventHandler<TSender, TPayload>, or a void delegate with two non-ref parameters and a non-ref-like second parameter.",
        category: "AsyncEventBridge",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var directlyAnnotatedTypes = context.SyntaxProvider.ForAttributeWithMetadataName(
            DirectAttributeMetadataName,
            static (_, _) => true,
            static (generatorContext, _) => (INamedTypeSymbol)generatorContext.TargetSymbol);

        context.RegisterSourceOutput(directlyAnnotatedTypes, static (productionContext, typeSymbol) =>
        {
            GenerateForDirectCustomDelegates(productionContext, typeSymbol);
        });

        context.RegisterSourceOutput(context.CompilationProvider, static (productionContext, compilation) =>
        {
            GenerateAssemblyTargets(productionContext, compilation);
        });
    }

    private static void GenerateForDirectCustomDelegates(
        SourceProductionContext context,
        INamedTypeSymbol typeSymbol)
    {
        if (!CanGenerateForType(typeSymbol))
        {
            return;
        }

        var typeParameters = CreateTypeParameterContext(typeSymbol);
        var supportedEvents = new List<EventGenerationInfo>();

        foreach (var eventSymbol in GetEventsForGeneration(typeSymbol, stopAtTargetTypes: null))
        {
            if (eventSymbol.IsStatic || !CanAccessEvent(eventSymbol, typeSymbol, isExternalTarget: false))
            {
                continue;
            }

            var classification = ClassifyEvent(eventSymbol, typeParameters);

            if (classification.Kind == EventKind.Unsupported)
            {
                ReportUnsupportedEvent(context, typeSymbol, eventSymbol, GetEventLocation(eventSymbol));
                continue;
            }

            if (classification.Kind != EventKind.Custom)
            {
                continue;
            }

            supportedEvents.Add(new EventGenerationInfo(
                eventSymbol,
                classification.EventArgsType,
                classification.HandlerType,
                classification.Kind,
                GetMethodAccessibility(typeSymbol, eventSymbol)));
        }

        GenerateSource(
            context,
            typeSymbol,
            supportedEvents,
            extensionSuffix: "CustomAsyncEventExtensions");
    }

    private static void GenerateAssemblyTargets(
        SourceProductionContext context,
        Compilation compilation)
    {
        var requests = compilation.Assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == TargetAttributeMetadataName)
            .ToArray();

        if (requests.Length == 0)
        {
            return;
        }

        var targets = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);

        foreach (var request in requests)
        {
            if (request.ConstructorArguments.Length != 1 ||
                request.ConstructorArguments[0].Value is not INamedTypeSymbol requestedType)
            {
                continue;
            }

            var targetType = requestedType.IsUnboundGenericType
                ? requestedType.OriginalDefinition
                : requestedType;

            if (!CanGenerateForType(targetType))
            {
                continue;
            }

            var location = request.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? Location.None;

            if (!targets.ContainsKey(targetType))
            {
                targets.Add(targetType, location);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        var targetedTypes = new HashSet<INamedTypeSymbol>(targets.Keys, SymbolEqualityComparer.Default);

        foreach (var pair in targets)
        {
            var typeSymbol = pair.Key;
            var requestLocation = pair.Value;
            var isExternalTarget = !SymbolEqualityComparer.Default.Equals(
                typeSymbol.ContainingAssembly,
                compilation.Assembly);

            if (!isExternalTarget && HasGenerateAsyncEventsAttribute(typeSymbol))
            {
                continue;
            }

            var typeParameters = CreateTypeParameterContext(typeSymbol);
            var supportedEvents = new List<EventGenerationInfo>();

            foreach (var eventSymbol in GetEventsForGeneration(typeSymbol, targetedTypes))
            {
                if (eventSymbol.IsStatic || !CanAccessEvent(eventSymbol, typeSymbol, isExternalTarget))
                {
                    continue;
                }

                var classification = ClassifyEvent(eventSymbol, typeParameters);

                if (classification.Kind == EventKind.Unsupported)
                {
                    ReportUnsupportedEvent(context, typeSymbol, eventSymbol, requestLocation);
                    continue;
                }

                supportedEvents.Add(new EventGenerationInfo(
                    eventSymbol,
                    classification.EventArgsType,
                    classification.HandlerType,
                    classification.Kind,
                    GetMethodAccessibility(typeSymbol, eventSymbol)));
            }

            GenerateSource(
                context,
                typeSymbol,
                supportedEvents,
                extensionSuffix: "TargetedAsyncEventExtensions");
        }
    }

    private static void GenerateSource(
        SourceProductionContext context,
        INamedTypeSymbol typeSymbol,
        IReadOnlyList<EventGenerationInfo> supportedEvents,
        string extensionSuffix)
    {
        if (supportedEvents.Count == 0)
        {
            return;
        }

        var typeParameters = CreateTypeParameterContext(typeSymbol);
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine("namespace AsyncEventBridge")
            .AppendLine("{")
            .AppendLine();

        var extensionAccessibility = supportedEvents.Any(item => item.Accessibility == "public")
            ? "public"
            : "internal";
        var extensionClassName = GetExtensionClassName(typeSymbol, extensionSuffix);

        source.Append(extensionAccessibility)
            .Append(" static class ")
            .Append(extensionClassName)
            .AppendLine()
            .AppendLine("{");

        foreach (var item in supportedEvents)
        {
            AppendWaitMethods(source, typeSymbol, item, typeParameters);
            AppendStreamMethods(source, typeSymbol, item, typeParameters);
        }

        source.AppendLine("}")
            .AppendLine("}");

        context.AddSource(
            extensionClassName + ".g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void AppendWaitMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
        TypeParameterContext typeParameters)
    {
        AppendWaitMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeTimeout: false);

        if (item.IsTyped)
        {
            AppendWaitMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeTimeout: false);
        }

        AppendWaitMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeTimeout: true);

        if (item.IsTyped)
        {
            AppendWaitMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeTimeout: true);
        }
    }

    private static void AppendWaitMethod(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
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
            source.Append('<').Append(item.EventArgsType).Append('>');
        }

        source.Append(' ').Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ").Append(sourceType).Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(item.EventArgsType)
                .Append("> predicate, ");
        }

        if (includeTimeout)
        {
            source.Append("global::System.TimeSpan timeout, ");
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

        if (item.Kind == EventKind.Custom)
        {
            AppendCustomWaitBody(source, item, eventName, includePredicate, includeTimeout);
        }
        else
        {
            AppendStandardWaitBody(source, item, eventName, includePredicate, includeTimeout);
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStandardWaitBody(
        StringBuilder source,
        EventGenerationInfo item,
        string eventName,
        bool includePredicate,
        bool includeTimeout)
    {
        var waitTypeArgument = item.IsTyped ? $"<{item.EventArgsType}>" : string.Empty;

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
                .AppendLine("            timeout);");
        }
        else
        {
            source.AppendLine(");");
        }
    }

    private static void AppendCustomWaitBody(
        StringBuilder source,
        EventGenerationInfo item,
        string eventName,
        bool includePredicate,
        bool includeTimeout)
    {
        source.Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventAwaiter.WaitAsync<")
            .Append(item.EventArgsType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.EventArgsType)
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
            .Append(item.EventArgsType)
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
                .AppendLine("            timeout);");
        }
        else
        {
            source.AppendLine(");");
        }
    }

    private static void AppendStreamMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
        TypeParameterContext typeParameters)
    {
        AppendStreamMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeOptions: false);
        AppendStreamMethod(source, typeSymbol, item, typeParameters, includePredicate: false, includeOptions: true);

        if (item.IsTyped)
        {
            AppendStreamMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeOptions: false);
            AppendStreamMethod(source, typeSymbol, item, typeParameters, includePredicate: true, includeOptions: true);
        }
    }

    private static void AppendStreamMethod(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
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
            .Append(item.EventArgsType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ").Append(sourceType).Append(" source, ");

        if (includePredicate)
        {
            source.Append("global::System.Predicate<")
                .Append(item.EventArgsType)
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

        if (item.Kind == EventKind.Custom)
        {
            AppendCustomStreamBody(source, item, eventName, includePredicate, includeOptions);
        }
        else
        {
            AppendStandardStreamBody(source, item, eventName, includePredicate, includeOptions);
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStandardStreamBody(
        StringBuilder source,
        EventGenerationInfo item,
        string eventName,
        bool includePredicate,
        bool includeOptions)
    {
        source.Append("        return global::AsyncEventBridge.EventStream.Create")
            .Append(item.IsTyped ? $"<{item.EventArgsType}>" : string.Empty)
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

    private static void AppendCustomStreamBody(
        StringBuilder source,
        EventGenerationInfo item,
        string eventName,
        bool includePredicate,
        bool includeOptions)
    {
        source.Append("        var adaptedHandlers = new global::System.Collections.Concurrent.ConcurrentDictionary<")
            .Append("global::System.EventHandler<")
            .Append(item.EventArgsType)
            .Append(">, ")
            .Append(item.HandlerType)
            .AppendLine(">();")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.EventStream.Create<")
            .Append(item.EventArgsType)
            .AppendLine(">(")
            .Append("            (global::System.EventHandler<")
            .Append(item.EventArgsType)
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
            .Append(item.EventArgsType)
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

    private static EventClassification ClassifyEvent(
        IEventSymbol eventSymbol,
        TypeParameterContext typeParameters)
    {
        if (eventSymbol.Type is not INamedTypeSymbol delegateType ||
            delegateType.TypeKind != TypeKind.Delegate)
        {
            return EventClassification.Unsupported(eventSymbol.Type.ToDisplayString());
        }

        if (delegateType.Name == "EventHandler" &&
            delegateType.ContainingNamespace.ToDisplayString() == "System")
        {
            if (delegateType.TypeArguments.Length == 0)
            {
                return new EventClassification(
                    EventKind.StandardUntyped,
                    "global::System.EventArgs",
                    "global::System.EventHandler");
            }

            if (delegateType.TypeArguments.Length == 1 &&
                IsAsyncPayloadCompatible(delegateType.TypeArguments[0]))
            {
                var payloadType = RenderType(delegateType.TypeArguments[0], typeParameters);
                return new EventClassification(
                    EventKind.StandardTyped,
                    payloadType,
                    $"global::System.EventHandler<{payloadType}>");
            }

            if (delegateType.TypeArguments.Length == 2 &&
                IsAsyncPayloadCompatible(delegateType.TypeArguments[1]))
            {
                return new EventClassification(
                    EventKind.Custom,
                    RenderType(delegateType.TypeArguments[1], typeParameters),
                    RenderType(delegateType, typeParameters));
            }

            return EventClassification.Unsupported(RenderType(delegateType, typeParameters));
        }

        var invokeMethod = delegateType.DelegateInvokeMethod;

        if (invokeMethod is null ||
            !invokeMethod.ReturnsVoid ||
            invokeMethod.Parameters.Length != 2 ||
            invokeMethod.Parameters[0].RefKind != RefKind.None ||
            invokeMethod.Parameters[1].RefKind != RefKind.None ||
            !IsAsyncPayloadCompatible(invokeMethod.Parameters[1].Type))
        {
            return EventClassification.Unsupported(RenderType(delegateType, typeParameters));
        }

        return new EventClassification(
            EventKind.Custom,
            RenderType(invokeMethod.Parameters[1].Type, typeParameters),
            RenderType(delegateType, typeParameters));
    }

    private static IEnumerable<IEventSymbol> GetEventsForGeneration(
        INamedTypeSymbol typeSymbol,
        ISet<INamedTypeSymbol>? stopAtTargetTypes)
    {
        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        INamedTypeSymbol? current = typeSymbol;
        var isTargetType = true;

        while (current is not null)
        {
            if (!isTargetType &&
                ((stopAtTargetTypes is not null && stopAtTargetTypes.Contains(current)) ||
                 (HasGenerateAsyncEventsAttribute(current) && CanGenerateForType(current))))
            {
                yield break;
            }

            foreach (var eventSymbol in current.GetMembers().OfType<IEventSymbol>())
            {
                if (!hiddenNames.Contains(eventSymbol.Name))
                {
                    yield return eventSymbol;
                }
            }

            foreach (var member in current.GetMembers())
            {
                hiddenNames.Add(member.Name);
            }

            isTargetType = false;
            current = current.BaseType;
        }
    }

    private static bool IsAsyncPayloadCompatible(ITypeSymbol typeSymbol) =>
        !typeSymbol.IsRefLikeType;

    private static bool CanAccessEvent(
        IEventSymbol eventSymbol,
        INamedTypeSymbol targetType,
        bool isExternalTarget)
    {
        if (eventSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            return true;
        }

        if (isExternalTarget)
        {
            return false;
        }

        var sameAssembly = SymbolEqualityComparer.Default.Equals(
            eventSymbol.ContainingAssembly,
            targetType.ContainingAssembly);

        return sameAssembly &&
            eventSymbol.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    private static string GetMethodAccessibility(INamedTypeSymbol typeSymbol, IEventSymbol eventSymbol)
    {
        return IsPubliclyAccessible(typeSymbol) &&
               eventSymbol.DeclaredAccessibility == Accessibility.Public &&
               IsPubliclyAccessible(eventSymbol.Type)
            ? "public"
            : "internal";
    }

    private static bool IsPubliclyAccessible(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is ITypeParameterSymbol)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsPubliclyAccessible(arrayType.ElementType);
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return true;
        }

        if (namedType.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (namedType.ContainingType is not null && !IsPubliclyAccessible(namedType.ContainingType))
        {
            return false;
        }

        return namedType.TypeArguments.All(IsPubliclyAccessible);
    }

    private static bool CanGenerateForType(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class)
        {
            return false;
        }

        INamedTypeSymbol? current = typeSymbol;

        while (current is not null)
        {
            if (current.DeclaredAccessibility is not (
                Accessibility.Public or
                Accessibility.Internal or
                Accessibility.ProtectedOrInternal))
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
    }

    private static bool HasGenerateAsyncEventsAttribute(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == DirectAttributeMetadataName);

    private static void ReportUnsupportedEvent(
        SourceProductionContext context,
        INamedTypeSymbol typeSymbol,
        IEventSymbol eventSymbol,
        Location location)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedEventDelegate,
            location,
            typeSymbol.ToDisplayString(),
            eventSymbol.Name,
            eventSymbol.Type.ToDisplayString()));
    }

    private static Location GetEventLocation(IEventSymbol eventSymbol) =>
        eventSymbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

    private static TypeParameterContext CreateTypeParameterContext(INamedTypeSymbol typeSymbol)
    {
        var typeChain = new Stack<INamedTypeSymbol>();
        INamedTypeSymbol? current = typeSymbol;

        while (current is not null)
        {
            typeChain.Push(current);
            current = current.ContainingType;
        }

        var parameters = new List<ITypeParameterSymbol>();
        var names = new Dictionary<ITypeParameterSymbol, string>(SymbolEqualityComparer.Default);
        var index = 0;

        foreach (var type in typeChain)
        {
            foreach (var parameter in type.TypeParameters)
            {
                parameters.Add(parameter);
                names.Add(parameter, "TSource" + index);
                index++;
            }
        }

        return new TypeParameterContext(parameters, names);
    }

    private static string RenderType(ITypeSymbol typeSymbol, TypeParameterContext typeParameters)
    {
        if (typeSymbol is ITypeParameterSymbol typeParameter &&
            typeParameters.Names.TryGetValue(typeParameter, out var parameterName))
        {
            return parameterName;
        }

        var builder = new StringBuilder();

        foreach (var part in typeSymbol.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            if (part.Symbol is ITypeParameterSymbol partTypeParameter &&
                typeParameters.Names.TryGetValue(partTypeParameter, out var replacement))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(part.ToString());
            }
        }

        return builder.ToString();
    }

    private static void AppendMethodTypeParameters(StringBuilder source, TypeParameterContext typeParameters)
    {
        if (typeParameters.Parameters.Count == 0)
        {
            return;
        }

        source.Append('<');

        for (var index = 0; index < typeParameters.Parameters.Count; index++)
        {
            if (index != 0)
            {
                source.Append(", ");
            }

            source.Append(typeParameters.Names[typeParameters.Parameters[index]]);
        }

        source.Append('>');
    }

    private static void AppendMethodConstraints(StringBuilder source, TypeParameterContext typeParameters)
    {
        foreach (var parameter in typeParameters.Parameters)
        {
            var constraints = GetConstraints(parameter, typeParameters);

            if (constraints.Count == 0)
            {
                continue;
            }

            source.AppendLine()
                .Append("        where ")
                .Append(typeParameters.Names[parameter])
                .Append(" : ")
                .Append(string.Join(", ", constraints));
        }
    }

    private static List<string> GetConstraints(
        ITypeParameterSymbol parameter,
        TypeParameterContext typeParameters)
    {
        var constraints = new List<string>();

        if (parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            constraints.Add(
                parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                    ? "class?"
                    : "class");
        }
        else if (parameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }

        foreach (var constraintType in parameter.ConstraintTypes)
        {
            constraints.Add(RenderType(constraintType, typeParameters));
        }

        if (parameter.HasConstructorConstraint &&
            !parameter.HasValueTypeConstraint &&
            !parameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("new()");
        }

        return constraints;
    }

    private static string GetExtensionClassName(INamedTypeSymbol typeSymbol, string suffix)
    {
        var builder = new StringBuilder();

        if (!typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            AppendEncodedName(builder, typeSymbol.ContainingNamespace.ToDisplayString());
            builder.Append("_DOT_");
        }

        var typeChain = new Stack<INamedTypeSymbol>();
        INamedTypeSymbol? current = typeSymbol;

        while (current is not null)
        {
            typeChain.Push(current);
            current = current.ContainingType;
        }

        var first = true;

        foreach (var type in typeChain)
        {
            if (!first)
            {
                builder.Append("_NESTED_");
            }

            AppendEncodedName(builder, type.Name);

            if (type.Arity != 0)
            {
                builder.Append("_A")
                    .Append(type.Arity);
            }

            first = false;
        }

        builder.Append(suffix);
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

    private enum EventKind
    {
        Unsupported,
        StandardUntyped,
        StandardTyped,
        Custom,
    }

    private readonly struct EventClassification
    {
        internal EventClassification(EventKind kind, string eventArgsType, string handlerType)
        {
            Kind = kind;
            EventArgsType = eventArgsType;
            HandlerType = handlerType;
        }

        internal EventKind Kind { get; }

        internal string EventArgsType { get; }

        internal string HandlerType { get; }

        internal static EventClassification Unsupported(string handlerType) =>
            new(EventKind.Unsupported, string.Empty, handlerType);
    }

    private sealed class TypeParameterContext
    {
        internal TypeParameterContext(
            IReadOnlyList<ITypeParameterSymbol> parameters,
            IReadOnlyDictionary<ITypeParameterSymbol, string> names)
        {
            Parameters = parameters;
            Names = names;
        }

        internal IReadOnlyList<ITypeParameterSymbol> Parameters { get; }

        internal IReadOnlyDictionary<ITypeParameterSymbol, string> Names { get; }
    }

    private sealed class EventGenerationInfo
    {
        internal EventGenerationInfo(
            IEventSymbol eventSymbol,
            string eventArgsType,
            string handlerType,
            EventKind kind,
            string accessibility)
        {
            EventSymbol = eventSymbol;
            EventArgsType = eventArgsType;
            HandlerType = handlerType;
            Kind = kind;
            Accessibility = accessibility;
        }

        internal IEventSymbol EventSymbol { get; }

        internal string EventArgsType { get; }

        internal string HandlerType { get; }

        internal EventKind Kind { get; }

        internal bool IsTyped => Kind != EventKind.StandardUntyped;

        internal string Accessibility { get; }
    }
}
