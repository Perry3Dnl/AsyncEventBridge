using System.Text;
using static AsyncEventBridge.Generators.GeneratorTypeSystem;
using static AsyncEventBridge.Generators.GeneratorSymbolAnalysis;
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

    private static readonly DiagnosticDescriptor InvalidGenerationTarget = new(
        id: "AEB002",
        title: "Invalid async-event generation target",
        messageFormat: "Type '{0}' cannot be targeted by GenerateAsyncEventsFor. Generated async-event adapters require a supported class type.",
        category: "AsyncEventBridge",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RedundantGenerationRequest = new(
        id: "AEB003",
        title: "Redundant async-event generation request",
        messageFormat: "Async-event generation for type '{0}' was requested more than once. The redundant request is ignored.",
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
        var supportedEvents = new List<EventGenerationModel>();

        foreach (var eventSymbol in GetEventsForGeneration(
            typeSymbol,
            static current => HasGenerateAsyncEventsAttribute(current) && CanGenerateForType(current)))
        {
            if (eventSymbol.IsStatic || !CanAccessEvent(eventSymbol, typeSymbol, isExternalTarget: false))
            {
                continue;
            }

            var shape = EventShapeClassifier.Classify(eventSymbol);

            if (shape.Kind == EventShapeKind.Unsupported || !shape.IsPayloadAsyncCompatible)
            {
                ReportUnsupportedEvent(context, typeSymbol, eventSymbol, GetEventLocation(eventSymbol));
                continue;
            }

            if (shape.Kind != EventShapeKind.Custom)
            {
                continue;
            }

            supportedEvents.Add(EventGenerationModel.Create(
                eventSymbol,
                shape,
                typeParameters,
                GetMethodAccessibility(typeSymbol, eventSymbol),
                preserveNullableAnnotations: false));
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
                context.ReportDiagnostic(Diagnostic.Create(
                    RedundantGenerationRequest,
                    requestLocation,
                    typeSymbol.ToDisplayString()));
                continue;
            }

            var typeParameters = CreateTypeParameterContext(typeSymbol);
            var supportedEvents = new List<EventGenerationModel>();

            foreach (var eventSymbol in GetEventsForGeneration(
                typeSymbol,
                current => targetedTypes.Contains(current) ||
                    (HasGenerateAsyncEventsAttribute(current) && CanGenerateForType(current))))
            {
                if (eventSymbol.IsStatic || !CanAccessEvent(eventSymbol, typeSymbol, isExternalTarget))
                {
                    continue;
                }

                var shape = EventShapeClassifier.Classify(eventSymbol);

                if (shape.Kind == EventShapeKind.Unsupported || !shape.IsPayloadAsyncCompatible)
                {
                    ReportUnsupportedEvent(context, typeSymbol, eventSymbol, requestLocation);
                    continue;
                }

                supportedEvents.Add(EventGenerationModel.Create(
                    eventSymbol,
                    shape,
                    typeParameters,
                    GetMethodAccessibility(typeSymbol, eventSymbol),
                    preserveNullableAnnotations: false));
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
        IReadOnlyList<EventGenerationModel> supportedEvents,
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
            EventWaitEmitter.AppendMethods(source, typeSymbol, item, typeParameters);
            EventStreamEmitter.AppendMethods(source, typeSymbol, item, typeParameters);
        }

        source.AppendLine("}")
            .AppendLine("}");

        context.AddSource(
            extensionClassName + ".g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
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

}
