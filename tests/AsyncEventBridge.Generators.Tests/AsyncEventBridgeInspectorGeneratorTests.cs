using System.Collections.Immutable;
using AsyncEventBridge.Unity.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AsyncEventBridge.Generators.Tests;

public sealed class AsyncEventBridgeInspectorGeneratorTests
{
    private const string RuntimeStubs = """
        #nullable enable
        using System;

        namespace UnityEngine
        {
            public class Object { }

            public class MonoBehaviour : Object { }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
            public sealed class RequireComponent : Attribute
            {
                public RequireComponent(Type requiredComponent) { }
            }

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class SerializeField : Attribute { }
        }

        namespace UnityEngine.Events
        {
            public class UnityEvent
            {
                public void Invoke() { }
            }

            public class UnityEvent<T0>
            {
                public void Invoke(T0 value) { }
            }
        }

        namespace AsyncEventBridge.Unity
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class GenerateInspectorEventsAttribute : Attribute { }

            public interface IAsyncEventBridgeInspectorSource
            {
                void ConnectInspectorEvents();
                void DisconnectInspectorEvents();
            }

            public sealed class AsyncEventBridgeInspectorHost : UnityEngine.MonoBehaviour { }
        }
        """;

    [Fact]
    public void GeneratesSerializedInspectorProjectionForPartialMonoBehaviour()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public sealed class ReadingEventArgs : EventArgs { }

                public delegate void LegacyHandler(object sender, ReadingEventArgs eventArgs);

                [AsyncEventBridge.Unity.GenerateInspectorEvents]
                public sealed partial class Sensor : UnityEngine.MonoBehaviour
                {
                    public event EventHandler? Tick;
                    public event EventHandler<ReadingEventArgs>? Reading;
                    public event LegacyHandler? Legacy;
                }
            }
            """;

        var result = RunGenerator(source);
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();

        Assert.Contains(
            "[global::UnityEngine.RequireComponent(typeof(global::AsyncEventBridge.Unity.AsyncEventBridgeInspectorHost))]",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("private __AebInspectorEvents AsyncEventBridgeEvents", generated, StringComparison.Ordinal);
        Assert.Contains("public global::UnityEngine.Events.UnityEvent Tick", generated, StringComparison.Ordinal);
        Assert.Contains(
            "public global::UnityEngine.Events.UnityEvent<global::Demo.ReadingEventArgs> Reading",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("this.Tick += __AebInspectorOnTick;", generated, StringComparison.Ordinal);
        Assert.Contains("this.Reading += __AebInspectorOnReading;", generated, StringComparison.Ordinal);
        Assert.Contains("this.Legacy += __AebInspectorOnLegacy;", generated, StringComparison.Ordinal);
        Assert.Contains("AsyncEventBridgeEvents.Tick.Invoke();", generated, StringComparison.Ordinal);
        Assert.Contains("AsyncEventBridgeEvents.Reading.Invoke(eventArgs);", generated, StringComparison.Ordinal);
        Assert.Contains("AsyncEventBridgeEvents.Legacy.Invoke(eventArgs);", generated, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void ReportsAeb004WhenTargetIsNotPartialMonoBehaviour()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.Unity.GenerateInspectorEvents]
                public sealed class Sensor : UnityEngine.MonoBehaviour
                {
                    public event EventHandler? Tick;
                }
            }
            """;

        var result = RunGenerator(source, allowWarnings: true);

        var diagnostic = Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Id == "AEB004"));
        Assert.Contains("partial MonoBehaviour", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    [Fact]
    public void ReportsAeb005AndStillGeneratesSupportedEvents()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                public delegate int UnsupportedHandler(object sender, EventArgs eventArgs);

                [AsyncEventBridge.Unity.GenerateInspectorEvents]
                public sealed partial class Sensor : UnityEngine.MonoBehaviour
                {
                    public event EventHandler? Tick;
                    public event UnsupportedHandler? Broken;
                }
            }
            """;

        var result = RunGenerator(source, allowWarnings: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AEB005");
        var generated = Assert.Single(Assert.Single(result.Results).GeneratedSources).SourceText.ToString();
        Assert.Contains("UnityEvent Tick", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Broken =", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAeb006ForReservedMemberCollision()
    {
        var source = RuntimeStubs + """
            namespace Demo
            {
                [AsyncEventBridge.Unity.GenerateInspectorEvents]
                public sealed partial class Sensor : UnityEngine.MonoBehaviour
                {
                    private int AsyncEventBridgeEvents;
                    public event EventHandler? Tick;
                }
            }
            """;

        var result = RunGenerator(source, allowWarnings: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AEB006");
        Assert.Empty(Assert.Single(result.Results).GeneratedSources);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool allowWarnings = false)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        var compilation = CSharpCompilation.Create(
            assemblyName: "InspectorGeneratorTests",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: GetPlatformReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { new AsyncEventBridgeInspectorGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var result = driver.GetRunResult();
        if (!allowWarnings)
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
