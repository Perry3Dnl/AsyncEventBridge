using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeGeneratorTests
{
    [Fact]
    public void GeneratesAsyncMethodsForSupportedEvents()
    {
        const string source = """
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
            }

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

        Assert.Contains("ValueChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("TickAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::Demo.SensorEventArgs>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Task<global::System.EventArgs>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::System.TimeSpan timeout", generatedSource, StringComparison.Ordinal);
        Assert.Contains("(global::System.EventHandler handler) => source.Tick += handler", generatedSource, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void IgnoresUnsupportedDelegateEvents()
    {
        const string source = """
            using System;

            namespace AsyncEventBridge
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class GenerateAsyncEventsAttribute : Attribute
                {
                }
            }

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
