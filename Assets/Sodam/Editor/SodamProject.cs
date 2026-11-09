using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

namespace Sodam.Editor
{
    public static class SodamProject
    {
        [MenuItem("Sodam/Create or open island")]
        public static void Setup()
        {
            const string path = "Assets/Scenes/SodamIsland.unity";
            if(File.Exists(path)) { EditorSceneManager.OpenScene(path); return; }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Sodam Island",typeof(SodamIsland));
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";
            camera.transform.position=new Vector3(-.6f,0,-10);camera.GetComponent<Camera>().orthographic=true;camera.GetComponent<Camera>().orthographicSize=8.25f;
            EditorSceneManager.SaveScene(scene,path);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true)};
            PlayerSettings.productName="Sodam Island";PlayerSettings.companyName="Sodam Studio";
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Sodam/Build Windows game")]
        public static string Build()
        {
            Setup();Directory.CreateDirectory("Builds/SodamIsland");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{"Assets/Scenes/SodamIsland.unity"},locationPathName="Builds/SodamIsland/SodamIsland.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Build failed: "+result.summary.result);
            return result.summary.result+" · "+result.summary.totalSize+" bytes";
        }
    }
}
