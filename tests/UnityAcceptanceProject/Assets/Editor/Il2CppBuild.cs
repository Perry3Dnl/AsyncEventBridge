using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AsyncEventBridgeAcceptance
{
    public static class Il2CppBuild
    {
        public static void PerformBuild()
        {
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP);

            const string scenePath = "Assets/AsyncEventBridgeAcceptance.unity";
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            var root = new GameObject("AsyncEventBridge Acceptance");
            root.AddComponent<AsyncEventBridgeAcceptanceSmoke>();
            EditorSceneManager.SaveScene(scene, scenePath);

            Directory.CreateDirectory("build");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = "build/AsyncEventBridgeAcceptance.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.StrictMode,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"IL2CPP acceptance build failed: {report.summary.result} " +
                    $"({report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings).");
            }
        }
    }
}
