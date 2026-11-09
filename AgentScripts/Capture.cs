using System.IO;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
public static class IslandCapture
{
    public static string Run()
    {
        Directory.CreateDirectory("Artifacts");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        var editorAssembly=typeof(Editor).Assembly;
        var sizesType=editorAssembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
        var groupType=editorAssembly.GetType("UnityEditor.GameViewSizeGroupType");
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new object[]{Convert.ToInt32(Enum.Parse(groupType,"Standalone"))});
        var sizeType=editorAssembly.GetType("UnityEditor.GameViewSize");
        var modeType=editorAssembly.GetType("UnityEditor.GameViewSizeType");
        var size=Activator.CreateInstance(sizeType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{Enum.Parse(modeType,"FixedResolution"),1440,900,"Sodam 1440 x 900"},null);
        group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size});
        int index=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null)-1;
        var view=EditorWindow.GetWindow(editorAssembly.GetType("UnityEditor.GameView"));
        view.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(view,index);
        view.Focus();view.Repaint();
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Artifacts/sodam-gameplay.png"));
        return "Capturing the next rendered frame.";
    }
}
