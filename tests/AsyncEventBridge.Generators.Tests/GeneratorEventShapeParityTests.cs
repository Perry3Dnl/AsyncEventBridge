using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class GeneratorEventShapeParityTests
{
    private const string RuntimeStubs = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace AsyncEventBridge
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
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

            public sealed class EventStreamOptions
            {
            }

            public static class EventAwaiter
            {
                public static Task<EventArgs> WaitAsync(
                    Action<EventHandler> subscribe,
                    Action<EventHandler> unsubscribe,
                    Predicate<EventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null,
                    TimeProvider? timeProvider = null) => throw new NotImplementedException();

                public static Task<TPayload> WaitAsync<TPayload>(
                    Action<EventHandler<TPayload>> subscribe,
                    Action<EventHandler<TPayload>> unsubscribe,
                    Predicate<TPayload>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null,
                    TimeProvider? timeProvider = null) => throw new NotImplementedException();
            }

            public static class EventStream
            {
                public static IAsyncEnumerable<EventArgs> Create(
                    Action<EventHandler> subscribe,
                    Action<EventHandler> unsubscribe,
                    Predicate<EventArgs>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default) => throw new NotImplementedException();

                public static IAsyncEnumerable<TPayload> Create<TPayload>(
                    Action<EventHandler<TPayload>> subscribe,
                    Action<EventHandler<TPayload>> unsubscribe,
                    Predicate<TPayload>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default) => throw new NotImplementedException();
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

            public static class EventOccurrenceStream
            {
                public static IAsyncEnumerable<EventOccurrence<TSender, TPayload>> Create<TSender, TPayload>(
                    Action<EventHandler<TSender, TPayload>> subscribe,
                    Action<EventHandler<TSender, TPayload>> unsubscribe,
                    Predicate<EventOccurrence<TSender, TPayload>>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default) => throw new NotImplementedException();
            }
        }
        """;

    [Fact]
    public void StandardCustomAndUnsupportedShapesStayAlignedAcrossGenerators()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void ReadingHandler(Sensor sender, string value);
                public delegate void InvalidHandler(int value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<int>? Standard;
                    public event ReadingHandler? Custom;
                    public event InvalidHandler? Invalid;
                }
            }
            """;

        var result = RunGenerators(source);
        var generated = result.Results
            .SelectMany(item => item.GeneratedSources)
            .Select(item => item.SourceText.ToString())
            .ToArray();

        Assert.Contains(generated, sourceText =>
            sourceText.Contains("StandardAsync", StringComparison.Ordinal) &&
            sourceText.Contains("StandardStream", StringComparison.Ordinal));

        Assert.Contains(generated, sourceText =>
            sourceText.Contains("CustomAsync", StringComparison.Ordinal) &&
            sourceText.Contains("CustomStream", StringComparison.Ordinal));

        Assert.Contains(generated, sourceText =>
            sourceText.Contains("StandardOccurrenceAsync", StringComparison.Ordinal) &&
            sourceText.Contains("CustomOccurrenceAsync", StringComparison.Ordinal));

        var unsupported = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB001"));
        Assert.Contains("Invalid", unsupported.GetMessage(), StringComparison.Ordinal);

        Assert.DoesNotContain(generated, sourceText =>
            sourceText.Contains("InvalidAsync", StringComparison.Ordinal) ||
            sourceText.Contains("InvalidOccurrenceAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedGeneratedApisKeepCompleteOverloadMatrix()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void ReadingHandler(Sensor sender, string value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<int>? Standard;
                    public event ReadingHandler? Custom;
                }
            }
            """;

        var result = RunGenerators(source);
        var generated = string.Join(
            "\n",
            result.Results
                .SelectMany(item => item.GeneratedSources)
                .Select(item => item.SourceText.ToString()));

        Assert.Equal(4, CountOccurrences(generated, " StandardAsync("));
        Assert.Equal(4, CountOccurrences(generated, " StandardStream("));
        Assert.Equal(4, CountOccurrences(generated, " CustomAsync("));
        Assert.Equal(4, CountOccurrences(generated, " CustomStream("));
        Assert.Equal(4, CountOccurrences(generated, " StandardOccurrenceAsync("));
        Assert.Equal(4, CountOccurrences(generated, " StandardOccurrenceStream("));
        Assert.Equal(4, CountOccurrences(generated, " CustomOccurrenceAsync("));
        Assert.Equal(4, CountOccurrences(generated, " CustomOccurrenceStream("));
    }

    [Fact]
    public void SharedTypeSystemPreservesNestedGenericsConstraintsNullabilityAndKeywords()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void CustomHandler<TPayload>(object? sender, TPayload payload);

                public sealed class Payload<TLeft, TRight>
                {
                }

                public sealed class Outer<TOuter>
                    where TOuter : class?
                {
                    [AsyncEventBridge.GenerateAsyncEvents]
                    public sealed class Sensor<TState>
                        where TState : class, new()
                    {
                        public event EventHandler<Payload<TOuter, TState>?>? @event;
                        public event CustomHandler<Payload<TOuter, TState>?>? Custom;
                    }
                }
            }
            """;

        var result = RunGenerators(source);
        var generated = result.Results
            .SelectMany(item => item.GeneratedSources)
            .Select(item => item.SourceText.ToString())
            .ToArray();

        Assert.Equal(3, generated.Length);

        foreach (var generatedSource in generated)
        {
            Assert.Contains(
                "global::Demo.Outer<TSource0>.Sensor<TSource1>",
                generatedSource,
                StringComparison.Ordinal);
            Assert.Contains("where TSource0 : class?", generatedSource, StringComparison.Ordinal);
            Assert.Contains("where TSource1 : class, new()", generatedSource, StringComparison.Ordinal);
        }

        var standard = Assert.Single(generated, sourceText =>
            sourceText.Contains("AsyncEventExtensions", StringComparison.Ordinal) &&
            !sourceText.Contains("CustomAsyncEventExtensions", StringComparison.Ordinal) &&
            !sourceText.Contains("OccurrenceAsyncEventExtensions", StringComparison.Ordinal));
        Assert.Contains("source.@event", standard, StringComparison.Ordinal);

        var occurrence = Assert.Single(generated, sourceText =>
            sourceText.Contains("OccurrenceAsyncEventExtensions", StringComparison.Ordinal));
        Assert.Contains(
            "EventOccurrence<global::System.Object?, global::Demo.Payload<TSource0, TSource1>?>",
            occurrence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedMembersHideInheritedEventsAcrossAllEmitters()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void CustomHandler(object? sender, int payload);

                public class SensorBase
                {
                    public event EventHandler<int>? Standard;
                    public event CustomHandler? Custom;
                }

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor : SensorBase
                {
                    public int Standard => 0;
                    public int Custom => 0;
                }
            }
            """;

        var result = RunGenerators(source);
        var generated = result.Results.SelectMany(item => item.GeneratedSources);

        Assert.Empty(generated);
    }

    [Fact]
    public void AnnotatedBaseStopsInheritedGenerationAcrossAllEmitters()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void CustomHandler(object? sender, int payload);

                [AsyncEventBridge.GenerateAsyncEvents]
                public class SensorBase
                {
                    public event EventHandler<int>? Standard;
                    public event CustomHandler? Custom;
                }

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor : SensorBase
                {
                }
            }
            """;

        var result = RunGenerators(source);
        var generated = result.Results
            .SelectMany(item => item.GeneratedSources)
            .Select(item => item.SourceText.ToString())
            .ToArray();

        Assert.Equal(3, generated.Length);
        Assert.DoesNotContain(generated, sourceText =>
            sourceText.Contains("this global::Demo.Sensor source", StringComparison.Ordinal));
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static GeneratorDriverRunResult RunGenerators(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
        var compilation = CSharpCompilation.Create(
            "EventShapeParityTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AsyncEventBridgeGenerator().AsSourceGenerator(),
                new AsyncEventBridgeAdapterGenerator().AsSourceGenerator(),
                new AsyncEventBridgeOccurrenceGenerator().AsSourceGenerator(),
            ],
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
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
