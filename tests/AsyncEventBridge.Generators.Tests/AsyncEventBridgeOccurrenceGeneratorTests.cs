using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeOccurrenceGeneratorTests
{
    private const string RuntimeStubs = """
        #nullable enable
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace AsyncEventBridge
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class GenerateAsyncEventsAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class GenerateAsyncEventsForAttribute : Attribute
            {
                public GenerateAsyncEventsForAttribute(Type targetType)
                {
                }
            }

            public readonly struct EventOccurrence<TSender, TPayload>
            {
            }

            public static class EventOccurrenceAwaiter
            {
                public static Task<EventOccurrence<TSender, TPayload>> WaitAsync<TSender, TPayload>(
                    Action<EventHandler<TSender, TPayload>> subscribe,
                    Action<EventHandler<TSender, TPayload>> unsubscribe,
                    Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null,
                    TimeProvider? timeProvider = null) => throw new NotImplementedException();
            }
        }
        """;

    [Fact]
    public void GeneratesStrongSenderOccurrenceWaits()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<Sensor, int>? ValueChanged;
                }
            }
            """;

        var result = RunGenerator(source);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("ValueChangedOccurrenceAsync", generated, StringComparison.Ordinal);
        Assert.Contains("EventOccurrence<global::Demo.Sensor, global::System.Int32>", generated, StringComparison.Ordinal);
        Assert.Contains("EventOccurrenceAwaiter.WaitAsync<global::Demo.Sensor, global::System.Int32>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesCustomDelegateOccurrenceWaits()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void ReadingHandler(Sensor sender, string value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event ReadingHandler? Reading;
                }
            }
            """;

        var result = RunGenerator(source);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("ReadingOccurrenceAsync", generated, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.ReadingHandler", generated, StringComparison.Ordinal);
        Assert.Contains("EventOccurrence<global::Demo.Sensor, global::System.String>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyTargetGeneratesOccurrenceWaits()
    {
        var source = RuntimeStubs.Replace(
            "namespace AsyncEventBridge",
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]" + Environment.NewLine + Environment.NewLine + "namespace AsyncEventBridge",
            StringComparison.Ordinal) + """

            namespace Demo
            {
                public sealed class Sensor
                {
                    public event EventHandler<int>? Changed;
                }
            }
            """;

        var result = RunGenerator(source);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("TargetedOccurrenceAsyncEventExtensions", generated, StringComparison.Ordinal);
        Assert.Contains("ChangedOccurrenceAsync", generated, StringComparison.Ordinal);
        Assert.Contains("EventOccurrence<global::System.Object?, global::System.Int32>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RefLikeSenderDoesNotGenerateOccurrenceApi()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void BufferHandler(Span<int> sender, int value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event BufferHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
        var compilation = CSharpCompilation.Create(
            "OccurrenceGeneratorTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AsyncEventBridgeOccurrenceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return driver.GetRunResult();
    }

    private static ImmutableArray<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies were not available.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(MetadataReference.CreateFromFile)
            .ToImmutableArray();
    }
}
