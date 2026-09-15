using System.Collections.Immutable;
using AsyncEventBridge.Unity.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeUnityGeneratorTests
{
    private const string RuntimeStubs = """
        #nullable enable
        using System;
        using System.Threading;

        namespace UnityEngine
        {
            public class Object
            {
            }

            public class MonoBehaviour : Object
            {
            }

            public sealed class Awaitable<T>
            {
            }
        }

        namespace AsyncEventBridge
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class GenerateAsyncEventsAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
            public sealed class GenerateAsyncEventsForAttribute : Attribute
            {
                public GenerateAsyncEventsForAttribute(Type targetType)
                {
                }
            }
        }

        namespace AsyncEventBridge.Unity
        {
            public static class UnityEventAwaiter
            {
                public static UnityEngine.Awaitable<EventArgs> WaitAsync(
                    Action<EventHandler> subscribe,
                    Action<EventHandler> unsubscribe,
                    Predicate<EventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null) => throw new NotImplementedException();

                public static UnityEngine.Awaitable<TEventArgs> WaitAsync<TEventArgs>(
                    Action<EventHandler<TEventArgs>> subscribe,
                    Action<EventHandler<TEventArgs>> unsubscribe,
                    Predicate<TEventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null)
                    where TEventArgs : EventArgs => throw new NotImplementedException();

                public static UnityEngine.Awaitable<EventArgs> WaitAsync(
                    UnityEngine.MonoBehaviour owner,
                    Action<EventHandler> subscribe,
                    Action<EventHandler> unsubscribe,
                    Predicate<EventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null) => throw new NotImplementedException();

                public static UnityEngine.Awaitable<TEventArgs> WaitAsync<TEventArgs>(
                    UnityEngine.MonoBehaviour owner,
                    Action<EventHandler<TEventArgs>> subscribe,
                    Action<EventHandler<TEventArgs>> unsubscribe,
                    Predicate<TEventArgs>? predicate = null,
                    CancellationToken cancellationToken = default,
                    TimeSpan? timeout = null)
                    where TEventArgs : EventArgs => throw new NotImplementedException();
            }
        }

        """;

    [Fact]
    public void GeneratesAwaitableAndLifecycleOverloadsForStandardAndCustomEvents()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate void LegacyHandler(object? sender, SensorEventArgs eventArgs);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event EventHandler<SensorEventArgs>? ValueChanged;
                    public event EventHandler? Tick;
                    public event LegacyHandler? LegacyChanged;
                }

                public sealed class SensorEventArgs : EventArgs
                {
                }
            }
            """;

        var result = RunGenerator(source);
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("namespace AsyncEventBridge.Unity", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Demo_DOT_SensorUnityAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains(
            "global::UnityEngine.Awaitable<global::Demo.SensorEventArgs> ValueChangedAsync",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::UnityEngine.Awaitable<global::System.EventArgs> TickAsync",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains("LegacyChangedAsync", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::UnityEngine.MonoBehaviour owner", generatedSource, StringComparison.Ordinal);
        Assert.Contains(
            "global::AsyncEventBridge.Unity.UnityEventAwaiter.WaitAsync<global::Demo.SensorEventArgs>",
            generatedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IAsyncEnumerable", generatedSource, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratesForAssemblyTargetAndPreservesGenericConstraints()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public class Sensor<TEventArgs>
                    where TEventArgs : EventArgs, new()
                {
                    public event EventHandler<TEventArgs>? Changed;
                }
            }
            """;

        var result = RunGenerator(
            source,
            assemblySource: "[assembly: AsyncEventBridge.GenerateAsyncEventsFor(typeof(Demo.Sensor<>))]");
        var generatedSource = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains("Sensor_A1UnityAsyncEventExtensions", generatedSource, StringComparison.Ordinal);
        Assert.Contains("ChangedAsync<TSource0>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("where TSource0 : global::System.EventArgs, new()", generatedSource, StringComparison.Ordinal);
        Assert.Contains("global::UnityEngine.MonoBehaviour owner", generatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAeb001ForUnsupportedDelegateShape()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate int UnsupportedHandler(object sender, EventArgs eventArgs);

                [AsyncEventBridge.GenerateAsyncEvents]
                public sealed class Sensor
                {
                    public event UnsupportedHandler? Broken;
                }
            }
            """;

        var result = RunGenerator(source, allowGeneratorWarnings: true);

        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AEB001");
    }

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        bool allowGeneratorWarnings = false,
        string? assemblySource = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(source, parseOptions),
        };

        if (assemblySource is not null)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(assemblySource, parseOptions));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "UnityGeneratorTests",
            syntaxTrees: syntaxTrees,
            references: GetPlatformReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AsyncEventBridgeUnityGenerator()],
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
