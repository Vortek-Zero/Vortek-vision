using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build automatizado (headless) do app Android do AR Metaverse.
///
/// Uso:
///   Unity -batchmode -quit -projectPath . -buildTarget Android \
///         -executeMethod AndroidImaxBuild.BuildImaxApk
///
/// Gera: Builds/Android/AR_Metaverse_imax.apk
/// (cena recomendada pelo README: AndroidAppScene_imax)
/// </summary>
public static class AndroidImaxBuild
{
    private const string ImaxScene = "Assets/Scenes/RemoteRenderingScenes/imax/AndroidAppScene_imax.unity";
    private const string ArScene = "Assets/Scenes/ARMetaverseScene.unity";
    private const string OutputApk = "Builds/Android/AR_Metaverse_imax.apk";
    private const string OutputArApk = "Builds/Android/AR_Metaverse_AR.apk";

    [MenuItem("Build/Android - APK IMAX (AndroidAppScene_imax)")]
    public static void BuildImaxApk()
    {
        Build(new[] { ImaxScene }, OutputApk);
    }

    [MenuItem("Build/Android - APK AR (ARMetaverseScene)")]
    public static void BuildArApk()
    {
        Build(new[] { ArScene }, OutputArApk);
    }

    private static void Build(string[] scenes, string outputPath)
    {
        foreach (var scene in scenes)
        {
            if (!System.IO.File.Exists(scene))
            {
                Debug.LogError($"[Build] Cena não encontrada: {scene}");
                EditorApplication.Exit(1);
                return;
            }
        }

        // Ajusta a lista de cenas apenas durante o build e restaura ao final,
        // pra não deixar o projeto com EditorBuildSettings.asset alterado.
        var previousScenes = EditorBuildSettings.scenes;
        EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };

        Debug.Log($"[Build] Iniciando build Android -> {outputPath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log($"[Build] Resultado: {summary.result} | " +
                  $"Tempo: {summary.totalTime} | " +
                  $"Tamanho: {summary.totalSize / (1024 * 1024)} MB | " +
                  $"Erros: {summary.totalErrors} | Warnings: {summary.totalWarnings}");

        // Devolve a lista de cenas do projeto ao estado original.
        EditorBuildSettings.scenes = previousScenes;

        if (summary.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[Build] {step.name}: {msg.content}");
                }
            }
        }

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
