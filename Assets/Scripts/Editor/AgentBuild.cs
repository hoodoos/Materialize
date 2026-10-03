using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AgentBuild
{
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory("Assets/AgentGenerated");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/AgentGenerated/Agent.unity");
        PlayerSettings.productName = "Materialize Agent";
        PlayerSettings.companyName = "Materialize GPL Fork";
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 64; PlayerSettings.defaultScreenHeight = 64;
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[]{"Assets/AgentGenerated/Agent.unity"}, locationPathName = "build/windows/materialize.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
        File.WriteAllText("build/windows/materialize.cmd", "@echo off\r\n\"%~dp0materialize.exe\" -batchmode -force-d3d11 -logFile - -- %*\r\n");
        File.Copy("LICENSE", "build/windows/LICENSE", true);
        Debug.Log("AGENT-BUILD PASS");
    }
}
