using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeGeneratorTests
{
    private const string RuntimeStubs = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace AsyncEventBridge
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class GenerateAsyncEventsAttribute : Attribute
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
    public void GeneratesAsyncMethodsForSupportedEvents()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<SensorEventArgs>? ValueChanged;
                    public event EventHandler? Tick;
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("namespace AsyncEventBridge;", generatedSource, StringComparison.Ordinal);
        Assert.Contains("public static class Demo_DOT_SensorAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::Demo.SensorEventArgs> ValueChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task TickAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::Demo.SensorEventArgs> ValueChangedStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<global::System.EventArgs> TickStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::System.Predicate<global::Demo.SensorEventArgs> predicate", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::AsyncEventBridge.EventStreamOptions options", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::System.TimeSpan timeout", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::AsyncEventBridge.EventStream.Create<global::Demo.SensorEventArgs>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("(global::System.EventHandler handler) => source.Tick += handler", generatedSource, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ExistingInstanceMethodWinsWhileGeneratedExtensionRemainsExplicitlyAccessible()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<SensorEventArgs>? ValueChanged;

                    public Task<SensorEventArgs> ValueChangedAsync() =>
                        Task.FromResult(new SensorEventArgs());
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }

                public static class Consumer
                {
                    public static async Task UseAsync(Sensor sensor)
                    {
                        await sensor.ValueChangedAsync();
                        await AsyncEventBridge.Demo_DOT_SensorAsyncEventExtensions.ValueChangedAsync(sensor);
                    }
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("public static class Demo_DOT_SensorAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("ValueChangedAsync", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratesForPublicInheritedEventWhenBaseTypeIsNotGenerated()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public class SensorBase
                {
                    public event EventHandler<SensorEventArgs>? ValueChanged;
                }

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor : SensorBase
                {
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("public static class Demo_DOT_SensorAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("ValueChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("ValueChangedStream", generatedSource, StringComparison.Ordinal);
        Assert.Contains("source.ValueChanged += handler", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotRegenerateInheritedEventsWhenBaseTypeAlreadyGeneratesThem()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public class SensorBase
                {
                    public event EventHandler<SensorEventArgs>? ValueChanged;
                }

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor : SensorBase
                {
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSources = Assert.Single(result.Results).GeneratedSources;
        var generatedSource = Assert.Single(generatedSources).SourceText.ToString();

        Assert.Contains("public static class Demo_DOT_SensorBaseAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Demo_DOT_SensorAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotExposeProtectedInheritedEvent()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public class SensorBase
                {
                    protected event EventHandler<SensorEventArgs>? ValueChanged;
                }

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor : SensorBase
                {
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void UsesDifferentGeneratedClassNamesForSameSimpleTypeName()
    {
        var source = RuntimeStubs + """
            namespace Sensors.Left
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler? Changed;
                }
            }

            namespace Sensors.Right
            {
                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSources = Assert.Single(result.Results).GeneratedSources;

        Assert.Equal(2, generatedSources.Length);
        var allGenerated = string.Join("\n", generatedSources.Select(item => item.SourceText.ToString()));
        Assert.Contains("Sensors_DOT_Left_DOT_SensorAsyncEventExtensions", allGenerated, StringComparison.Ordinal);
        Assert.Contains("Sensors_DOT_Right_DOT_SensorAsyncEventExtensions", allGenerated, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresUnsupportedDelegateEvents()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void CustomHandler(int value);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event CustomHandler? Changed;
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetPlatformReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AsyncEventBridgeGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return driver.GetRunResult();
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
