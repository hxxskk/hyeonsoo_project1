using UnityEngine;
public static class ResetIslandPreview
{
    public static string Run()
    {
        var existing=Object.FindAnyObjectByType<Sodam.SodamIsland>();
        if(existing)Object.DestroyImmediate(existing.gameObject);
        var fresh=new GameObject("Sodam Island",typeof(Sodam.SodamIsland)).GetComponent<Sodam.SodamIsland>();
        fresh.Welcome=false;
        return "Fresh preview ready.";
    }
}
