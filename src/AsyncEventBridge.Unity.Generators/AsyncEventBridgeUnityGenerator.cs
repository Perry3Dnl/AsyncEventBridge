using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace AsyncEventBridge.Unity.Generators;

[Generator]
public sealed class AsyncEventBridgeUnityGenerator : ISourceGenerator
{
    private const string DirectAttributeMetadataName = "AsyncEventBridge.GenerateAsyncEventsAttribute";
    private const string TargetAttributeMetadataName = "AsyncEventBridge.GenerateAsyncEventsForAttribute";

    private static readonly DiagnosticDescriptor UnsupportedEventDelegate = new DiagnosticDescriptor(
        id: "AEB001",
        title: "Unsupported event delegate",
        messageFormat: "Event '{0}.{1}' uses unsupported delegate type '{2}'. Generated event APIs require EventHandler, EventHandler<TEventArgs>, or a void delegate with two non-ref parameters whose second parameter derives from EventArgs.",
        category: "AsyncEventBridge",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb001");

    private static readonly DiagnosticDescriptor InvalidGenerationTarget = new DiagnosticDescriptor(
        id: "AEB002",
        title: "Invalid async-event generation target",
        messageFormat: "Type '{0}' cannot be targeted by GenerateAsyncEventsFor. Generated async-event adapters require a supported class or interface type.",
        category: "AsyncEventBridge",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb002");

    private static readonly DiagnosticDescriptor RedundantGenerationRequest = new DiagnosticDescriptor(
        id: "AEB003",
        title: "Redundant async-event generation request",
        messageFormat: "Async-event generation for type '{0}' was requested more than once. The redundant request is ignored.",
        category: "AsyncEventBridge",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb003");

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var compilation = context.Compilation;
        var directTargets = GetAllTypes(compilation.Assembly.GlobalNamespace)
            .Where(HasGenerateAsyncEventsAttribute)
            .Where(CanGenerateForType)
            .ToArray();

        var directTargetSet = new HashSet<INamedTypeSymbol>(directTargets, SymbolEqualityComparer.Default);
        var assemblyTargets = GetAssemblyTargets(context, compilation);
        var allStopTypes = new HashSet<INamedTypeSymbol>(directTargetSet, SymbolEqualityComparer.Default);

        foreach (var target in assemblyTargets.Keys)
        {
            allStopTypes.Add(target);
        }

        foreach (var typeSymbol in directTargets)
        {
            GenerateForType(
                context,
                compilation,
                typeSymbol,
                isExternalTarget: false,
                diagnosticLocation: null,
                stopAtTargetTypes: allStopTypes);
        }

        foreach (var pair in assemblyTargets)
        {
            var typeSymbol = pair.Key;

            if (directTargetSet.Contains(typeSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RedundantGenerationRequest,
                    pair.Value,
                    typeSymbol.ToDisplayString()));
                continue;
            }

            var isExternalTarget = !SymbolEqualityComparer.Default.Equals(
                typeSymbol.ContainingAssembly,
                compilation.Assembly);

            GenerateForType(
                context,
                compilation,
                typeSymbol,
                isExternalTarget,
                pair.Value,
                allStopTypes);
        }
    }

    private static Dictionary<INamedTypeSymbol, Location> GetAssemblyTargets(
        GeneratorExecutionContext context,
        Compilation compilation)
    {
        var targets = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);

        foreach (var request in compilation.Assembly.GetAttributes())
        {
            if (request.AttributeClass?.ToDisplayString() != TargetAttributeMetadataName ||
                request.ConstructorArguments.Length != 1 ||
                !(request.ConstructorArguments[0].Value is INamedTypeSymbol requestedType))
            {
                continue;
            }

            var targetType = requestedType.IsUnboundGenericType
                ? requestedType.OriginalDefinition
                : requestedType;
            var location = request.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? Location.None;

            if (!CanGenerateForType(targetType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidGenerationTarget,
                    location,
                    targetType.ToDisplayString()));
                continue;
            }

            if (targets.ContainsKey(targetType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RedundantGenerationRequest,
                    location,
                    targetType.ToDisplayString()));
                continue;
            }

            targets.Add(targetType, location);
        }

        return targets;
    }

    private static void GenerateForType(
        GeneratorExecutionContext context,
        Compilation compilation,
        INamedTypeSymbol typeSymbol,
        bool isExternalTarget,
        Location? diagnosticLocation,
        ISet<INamedTypeSymbol> stopAtTargetTypes)
    {
        var typeParameters = CreateTypeParameterContext(typeSymbol);
        var supportedEvents = new List<EventGenerationInfo>();

        foreach (var eventSymbol in GetEventsForGeneration(typeSymbol, stopAtTargetTypes))
        {
            if (eventSymbol.IsStatic || !CanAccessEvent(eventSymbol, compilation.Assembly, isExternalTarget))
            {
                continue;
            }

            var classification = ClassifyEvent(eventSymbol, typeParameters);
            if (classification.Kind == EventKind.Unsupported)
            {
                var location = diagnosticLocation ?? GetEventLocation(eventSymbol);
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsupportedEventDelegate,
                    location,
                    typeSymbol.ToDisplayString(),
                    eventSymbol.Name,
                    eventSymbol.Type.ToDisplayString()));
                continue;
            }

            supportedEvents.Add(new EventGenerationInfo(
                eventSymbol,
                classification.EventArgsType,
                classification.HandlerType,
                classification.Kind,
                GetMethodAccessibility(typeSymbol, eventSymbol)));
        }

        if (supportedEvents.Count == 0)
        {
            return;
        }

        GenerateSource(context, typeSymbol, supportedEvents, typeParameters);
    }

    private static void GenerateSource(
        GeneratorExecutionContext context,
        INamedTypeSymbol typeSymbol,
        IReadOnlyList<EventGenerationInfo> supportedEvents,
        TypeParameterContext typeParameters)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>")
            .AppendLine("#nullable enable")
            .AppendLine("namespace AsyncEventBridge.Unity")
            .AppendLine("{")
            .AppendLine();

        var extensionAccessibility = supportedEvents.Any(item => item.Accessibility == "public")
            ? "public"
            : "internal";
        var extensionClassName = GetExtensionClassName(typeSymbol);

        source.Append(extensionAccessibility)
            .Append(" static class ")
            .Append(extensionClassName)
            .AppendLine()
            .AppendLine("{");

        foreach (var item in supportedEvents)
        {
            AppendWaitMethods(source, typeSymbol, item, typeParameters, includeOwner: false);
            AppendWaitMethods(source, typeSymbol, item, typeParameters, includeOwner: true);
        }

        source.AppendLine("}")
            .AppendLine("}");

        context.AddSource(
            extensionClassName + ".UnityAsyncEvents.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void AppendWaitMethods(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
        TypeParameterContext typeParameters,
        bool includeOwner)
    {
        AppendWaitMethod(
            source,
            typeSymbol,
            item,
            typeParameters,
            includeOwner,
            includePredicate: false,
            includeTimeout: false);

        if (item.IsTyped)
        {
            AppendWaitMethod(
                source,
                typeSymbol,
                item,
                typeParameters,
                includeOwner,
                includePredicate: true,
                includeTimeout: false);
        }

        AppendWaitMethod(
            source,
            typeSymbol,
            item,
            typeParameters,
            includeOwner,
            includePredicate: false,
            includeTimeout: true);

        if (item.IsTyped)
        {
            AppendWaitMethod(
                source,
                typeSymbol,
                item,
                typeParameters,
                includeOwner,
                includePredicate: true,
                includeTimeout: true);
        }
    }

    private static void AppendWaitMethod(
        StringBuilder source,
        INamedTypeSymbol typeSymbol,
        EventGenerationInfo item,
        TypeParameterContext typeParameters,
        bool includeOwner,
        bool includePredicate,
        bool includeTimeout)
    {
        var sourceType = RenderType(typeSymbol, typeParameters);
        var eventName = EscapeIdentifier(item.EventSymbol.Name);
        var methodName = item.EventSymbol.Name + "Async";

        source.Append("    /// <summary>Asynchronously waits for the next ")
            .Append(item.EventSymbol.Name)
            .AppendLine(" event occurrence using Unity Awaitable.</summary>")
            .Append("    ")
            .Append(item.Accessibility)
            .Append(" static global::UnityEngine.Awaitable<")
            .Append(item.EventArgsType)
            .Append("> ")
            .Append(methodName);
        AppendMethodTypeParameters(source, typeParameters);
        source.Append("(this ")
            .Append(sourceType)
            .Append(" source, ");

        if (includeOwner)
        {
            source.Append("global::UnityEngine.MonoBehaviour owner, ");
        }

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

        if (includeOwner)
        {
            source.AppendLine("        if (owner == null)")
                .AppendLine("        {")
                .AppendLine("            throw new global::System.ArgumentNullException(nameof(owner));")
                .AppendLine("        }")
                .AppendLine();
        }

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
            AppendCustomWaitBody(source, item, eventName, includeOwner, includePredicate, includeTimeout);
        }
        else
        {
            AppendStandardWaitBody(source, item, eventName, includeOwner, includePredicate, includeTimeout);
        }

        source.AppendLine("    }")
            .AppendLine();
    }

    private static void AppendStandardWaitBody(
        StringBuilder source,
        EventGenerationInfo item,
        string eventName,
        bool includeOwner,
        bool includePredicate,
        bool includeTimeout)
    {
        source.Append("        return global::AsyncEventBridge.Unity.UnityEventAwaiter.WaitAsync");

        if (item.IsTyped)
        {
            source.Append('<').Append(item.EventArgsType).Append('>');
        }

        source.AppendLine("(");

        if (includeOwner)
        {
            source.AppendLine("            owner,");
        }

        source.Append("            (")
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
        bool includeOwner,
        bool includePredicate,
        bool includeTimeout)
    {
        source.Append("        ")
            .Append(item.HandlerType)
            .AppendLine("? adaptedHandler = null;")
            .AppendLine()
            .Append("        return global::AsyncEventBridge.Unity.UnityEventAwaiter.WaitAsync<")
            .Append(item.EventArgsType)
            .AppendLine(">(");

        if (includeOwner)
        {
            source.AppendLine("            owner,");
        }

        source.Append("            (global::System.EventHandler<")
            .Append(item.EventArgsType)
            .AppendLine("> handler) =>")
            .AppendLine("            {")
            .Append("                adaptedHandler = new ")
            .Append(item.HandlerType)
            .AppendLine("((sender, eventArgs) => handler(sender, eventArgs));")
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

    private static EventClassification ClassifyEvent(
        IEventSymbol eventSymbol,
        TypeParameterContext typeParameters)
    {
        var delegateType = eventSymbol.Type as INamedTypeSymbol;
        if (delegateType is null || delegateType.TypeKind != TypeKind.Delegate)
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
                IsEventArgsCompatible(delegateType.TypeArguments[0]))
            {
                var eventArgsType = RenderType(delegateType.TypeArguments[0], typeParameters);
                return new EventClassification(
                    EventKind.StandardTyped,
                    eventArgsType,
                    "global::System.EventHandler<" + eventArgsType + ">");
            }

            return EventClassification.Unsupported(RenderType(delegateType, typeParameters));
        }

        var invokeMethod = delegateType.DelegateInvokeMethod;
        if (invokeMethod is null ||
            !invokeMethod.ReturnsVoid ||
            invokeMethod.Parameters.Length != 2 ||
            invokeMethod.Parameters[0].RefKind != RefKind.None ||
            invokeMethod.Parameters[1].RefKind != RefKind.None ||
            invokeMethod.Parameters[0].Type.IsRefLikeType ||
            !IsEventArgsCompatible(invokeMethod.Parameters[1].Type))
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
        ISet<INamedTypeSymbol> stopAtTargetTypes)
    {
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            foreach (var eventSymbol in typeSymbol.GetMembers().OfType<IEventSymbol>())
            {
                yield return eventSymbol;
            }

            yield break;
        }

        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        INamedTypeSymbol? current = typeSymbol;
        var isTargetType = true;

        while (current is not null)
        {
            if (!isTargetType && stopAtTargetTypes.Contains(current))
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

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            foreach (var nestedOrSelf in GetTypeAndNestedTypes(type))
            {
                yield return nestedOrSelf;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(childNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypeAndNestedTypes(INamedTypeSymbol typeSymbol)
    {
        yield return typeSymbol;

        foreach (var nested in typeSymbol.GetTypeMembers())
        {
            foreach (var nestedOrSelf in GetTypeAndNestedTypes(nested))
            {
                yield return nestedOrSelf;
            }
        }
    }

    private static bool IsEventArgsCompatible(ITypeSymbol typeSymbol)
    {
        var typeParameter = typeSymbol as ITypeParameterSymbol;
        if (typeParameter is not null)
        {
            return typeParameter.ConstraintTypes.Any(IsEventArgsCompatible);
        }

        var namedType = typeSymbol as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        INamedTypeSymbol? current = namedType;
        while (current is not null)
        {
            if (current.Name == "EventArgs" &&
                current.ContainingNamespace.ToDisplayString() == "System")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool CanAccessEvent(
        IEventSymbol eventSymbol,
        IAssemblySymbol consumingAssembly,
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
            consumingAssembly);

        return sameAssembly &&
            (eventSymbol.DeclaredAccessibility == Accessibility.Internal ||
             eventSymbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal);
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

        var arrayType = typeSymbol as IArrayTypeSymbol;
        if (arrayType is not null)
        {
            return IsPubliclyAccessible(arrayType.ElementType);
        }

        var namedType = typeSymbol as INamedTypeSymbol;
        if (namedType is null)
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
        if (typeSymbol.TypeKind != TypeKind.Class &&
            typeSymbol.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        INamedTypeSymbol? current = typeSymbol;
        while (current is not null)
        {
            if (current.DeclaredAccessibility != Accessibility.Public &&
                current.DeclaredAccessibility != Accessibility.Internal &&
                current.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
    }

    private static bool HasGenerateAsyncEventsAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == DirectAttributeMetadataName);
    }

    private static Location GetEventLocation(IEventSymbol eventSymbol)
    {
        return eventSymbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
    }

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
        var directParameter = typeSymbol as ITypeParameterSymbol;
        if (directParameter is not null &&
            typeParameters.Names.TryGetValue(directParameter, out var directName))
        {
            return directName;
        }

        var builder = new StringBuilder();
        foreach (var part in typeSymbol.ToDisplayParts(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            var partTypeParameter = part.Symbol as ITypeParameterSymbol;
            if (partTypeParameter is not null &&
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

    private static void AppendMethodTypeParameters(
        StringBuilder source,
        TypeParameterContext typeParameters)
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

    private static void AppendMethodConstraints(
        StringBuilder source,
        TypeParameterContext typeParameters)
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
            constraints.Add("class");
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

    private static string GetExtensionClassName(INamedTypeSymbol typeSymbol)
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
                builder.Append("_A").Append(type.Arity);
            }

            first = false;
        }

        builder.Append("UnityAsyncEventExtensions");
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

    private static string EscapeIdentifier(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
            ? identifier
            : "@" + identifier;
    }

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

        internal static EventClassification Unsupported(string handlerType)
        {
            return new EventClassification(EventKind.Unsupported, string.Empty, handlerType);
        }
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
