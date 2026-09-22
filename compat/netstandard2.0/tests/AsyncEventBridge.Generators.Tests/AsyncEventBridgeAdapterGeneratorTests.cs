using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeAdapterGeneratorTests
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
                    TimeSpan? timeout = null) => throw new NotImplementedException();

                public static Task<TEventArgs> WaitAsync<TEventArgs>(
                    Action<EventHandler<TEventArgs>> subscribe,
                    Action<EventHandler<TEventArgs>> unsubscribe,
                    Predicate<TEventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null)
                    where TEventArgs : EventArgs => throw new NotImplementedException();
            }

            public static class EventStream
            {
                public static IAsyncEnumerable<EventArgs> Create(
                    Action<EventHandler> subscribe,
                    Action<EventHandler> unsubscribe,
                    Predicate<EventArgs>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default) => throw new NotImplementedException();

                public static IAsyncEnumerable<TEventArgs> Create<TEventArgs>(
                    Action<EventHandler<TEventArgs>> subscribe,
                    Action<EventHandler<TEventArgs>> unsubscribe,
                    Predicate<TEventArgs>? predicate = null,
                    EventStreamOptions? options = null,
                    CancellationToken cancellationToken = default)
                    where TEventArgs : EventArgs => throw new NotImplementedException();
            }
        }

        """;

    [Fact]
    public void GeneratesForEventArgsShapedCustomDelegate()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void SensorChangedHandler(object? sender, SensorEventArgs e);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event SensorChangedHandler? Changed;
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("/// <summary>Contains generated async event extension methods.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Asynchronously waits for the next Changed event occurrence.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Creates an async stream for Changed event occurrences.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::Demo.SensorEventArgs> ChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::Demo.SensorEventArgs> ChangedStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("new global::Demo.SensorChangedHandler", generatedSource, StringComparison.Ordinal);
        Assert.Contains("ConcurrentDictionary", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AEB001");
    }

    [Fact]
    public void PreservesNullableCustomDelegatePayload()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void SensorChangedHandler(object? sender, SensorEventArgs? e);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event SensorChangedHandler? Changed;
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("Task<global::Demo.SensorEventArgs?> ChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::Demo.SensorEventArgs?> ChangedStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Predicate<global::Demo.SensorEventArgs?> predicate", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAeb001ForUnsupportedDelegateShape()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void ValueHandler(int value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event ValueHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source, allowGeneratorWarnings: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB001"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb001", diagnostic.Descriptor.HelpLinkUri);
        Assert.Contains("Changed", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("ValueHandler", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void AssemblyAttributeGeneratesForUnannotatedType()
    {
        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]") + """

            namespace Demo
            {
                public sealed class Sensor
                {
                    public event EventHandler<SensorEventArgs>? Changed;
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("Demo_DOT_SensorTargetedAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::Demo.SensorEventArgs> ChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::Demo.SensorEventArgs> ChangedStream", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyAttributeGeneratesForThirdPartyPropertyChangedEvent()
    {
        var thirdPartyReference = CompileReference(
            "ThirdPartyApi",
            """
            #nullable enable
            using System.ComponentModel;

            namespace ThirdParty
            {
                public sealed class LegacySensor
                {
                    public event PropertyChangedEventHandler? PropertyChanged;
                }
            }
            """);

        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]") + """

            namespace Consumer
            {
                public static class Marker
                {
                }
            }
            """;

        var result = RunGenerator(source, additionalReferences: [thirdPartyReference]);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("ThirdParty_DOT_LegacySensorTargetedAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::System.ComponentModel.PropertyChangedEventArgs> PropertyChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::System.ComponentModel.PropertyChangedEventArgs> PropertyChangedStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("new global::System.ComponentModel.PropertyChangedEventHandler", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyAttributeGeneratesForThirdPartyInterface()
    {
        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(System.ComponentModel.INotifyPropertyChanged))]");

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("System_DOT_ComponentModel_DOT_INotifyPropertyChangedTargetedAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PropertyChangedAsync(this global::System.ComponentModel.INotifyPropertyChanged source", generatedSource, StringComparison.Ordinal);
        Assert.Contains("PropertyChangedStream(this global::System.ComponentModel.INotifyPropertyChanged source", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "AEB002");
    }

    [Fact]
    public void ReportsAeb002ForInvalidAssemblyTarget()
    {
        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]") + """

            namespace Demo
            {
                public struct Sensor
                {
                }
            }
            """;

        var result = RunGenerator(source, allowGeneratorWarnings: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB002"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb002", diagnostic.Descriptor.HelpLinkUri);
        Assert.Contains("Demo.Sensor", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void ReportsAeb003ForDuplicateAssemblyTarget()
    {
        var source = RuntimeWithAssemblyAttribute(
            """
            [assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]
            [assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]
            """) + """

            namespace Demo
            {
                public sealed class Sensor
                {
                    public event EventHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source, allowGeneratorWarnings: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB003"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("https://github.com/Perry3Dnl/AsyncEventBridge/blob/main/docs/diagnostics.md#aeb003", diagnostic.Descriptor.HelpLinkUri);
        Assert.Contains("Demo.Sensor", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Single(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void ReportsAeb003WhenAssemblyTargetIsAlreadyDirectlyAnnotated()
    {
        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor))]") + """

            namespace Demo
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source, allowGeneratorWarnings: true);
        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB003"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Demo.Sensor", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void TargetedExternalTypeDoesNotExposeNonPublicEvents()
    {
        var thirdPartyReference = CompileReference(
            "ThirdPartyApi",
            """
            #nullable enable
            using System;

            namespace ThirdParty
            {
                public sealed class LegacySensor
                {
                    public event EventHandler? Visible;
                    internal event EventHandler? Hidden;
                }
            }
            """);

        var source = RuntimeWithAssemblyAttribute(
            "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(ThirdParty.LegacySensor))]");

        var result = RunGenerator(source, additionalReferences: [thirdPartyReference]);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("VisibleAsync", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HiddenAsync", generatedSource, StringComparison.Ordinal);
    }

    private static string RuntimeWithAssemblyAttribute(string attribute) =>
        RuntimeStubs.Replace(
            "namespace AsyncEventBridge",
            attribute + Environment.NewLine + Environment.NewLine + "namespace AsyncEventBridge",
            StringComparison.Ordinal);

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        bool allowGeneratorWarnings = false,
        IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp8);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = GetPlatformReferences().ToBuilder();

        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "AdapterGeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references.ToImmutable(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AsyncEventBridgeAdapterGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var result = driver.GetRunResult();

        if (!allowGeneratorWarnings)
        {
            Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
        }

        return result;
    }

    private static MetadataReference CompileReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp8))],
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);

        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ImmutableArray<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies were not available.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }
}
