using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Sodam.Editor
{
    public static class SodamVerification
    {
        static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
        static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static void Call(object target,string method) => target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);

        // An integration check against the actual running island. The player's save is never used.
        public static string Run()
        {
            var game=UnityEngine.Object.FindAnyObjectByType<SodamIsland>();
            Require(game!=null&&Application.isPlaying,"Open the island in Play Mode first.");
            string snapshot=JsonUtility.ToJson(game.Data);
            var player=GameObject.Find("Player · 섬지기").transform;Vector3 start=player.position;
            string temporary=Path.GetFullPath("Temp/sodam-verification.json");
            Set(game,"savePathOverride",temporary);
            var passed=new List<string>();
            Action<Vector2> interact=p=>{player.position=p;Call(game,"FindNearest");game.Interact();};
            try
            {
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new SodamIsland.SaveData()),game.Data);
                game.Welcome=false;
                Require(!game.ClaimDaily(),"Incomplete daily request must not pay out.");
                game.Data.picked[0]=false;game.Data.apples=0;game.Data.wood=0;
                interact(new Vector2(-9,1.5f));
                Require(game.Data.apples==3&&game.Data.wood==1,"Tree did not give resources.");
                game.Interact();Require(game.Data.apples==3,"Tree rewards repeated on the same day.");passed.Add("harvest + same-day depletion");
                game.Data.plots[0]=0;game.Data.seeds=6;
                interact(new Vector2(-1.8f,2.55f));Require(game.Data.plots[0]==1&&game.Data.seeds==5,"Plant failed.");
                game.Interact();Require(game.Data.plots[0]==2,"Water failed.");passed.Add("plant + water");
                interact(new Vector2(-5.7f,-1.3f));Require(game.Data.talked,"Resident greeting failed.");
                Require(game.Gift()&&!game.Gift(),"Gift should work only once per day.");Set(game,"dialog","");passed.Add("resident + one gift per day");
                interact(new Vector2(3,-.65f));Set(game,"fishing",2);Set(game,"fishTime",0f);int fish=game.Data.fish;game.Reel();
                Require(game.Data.fish==fish+1&&game.Data.caught>=1,"Fishing success did not award fish.");passed.Add("fishing mini-game");
                int coins=game.Data.coins;Require(game.ClaimDaily()&&game.Data.coins==coins+80&&!game.ClaimDaily(),"Daily reward must be paid exactly once.");passed.Add("daily goals + single reward");
                game.Data.apples=2;game.Data.fish=1;game.Data.crops=2;coins=game.Data.coins;
                Require(game.SellAll()==77&&game.Data.coins==coins+77&&game.Data.apples==0&&game.Data.fish==0&&game.Data.crops==0,"Market accounting failed.");
                game.Data.coins=0;Require(!game.BuySeeds()&&game.Data.coins==0,"Insufficient funds allowed purchase.");
                game.Data.coins=100;int seeds=game.Data.seeds;Require(game.BuySeeds()&&game.Data.seeds==seeds+3&&game.Data.coins==90,"Seed purchase failed.");passed.Add("market + insufficient funds");
                game.Data.wood=4;int count=game.Data.decorations.Count;
                Require(!game.PlaceDecoration(new Vector2(3.3f,1.7f)),"Furniture was placed in water.");
                Vector2 free=new Vector2(-4,-3.5f);
                Require(game.PlaceDecoration(free)&&game.Data.decorations.Count==count+1&&game.Data.wood==2,"Furniture placement failed.");
                Require(!game.PlaceDecoration(free),"Overlapping furniture was allowed.");passed.Add("decoration + water/overlap constraints");
                game.NextDay();Require(!game.Data.picked[0]&&game.Data.plots[0]==3&&!game.Data.talked&&!game.Data.rewarded,"Next day did not replenish/reset/grow.");
                int crops=game.Data.crops;interact(new Vector2(-1.8f,2.55f));Require(game.Data.crops==crops+2&&game.Data.plots[0]==0,"Ripe harvest failed.");passed.Add("next day + ripe harvest");
                game.Save();var restored=JsonUtility.FromJson<SodamIsland.SaveData>(File.ReadAllText(temporary));
                Require(restored.day==game.Data.day&&restored.coins==game.Data.coins&&restored.decorations.Count==game.Data.decorations.Count,"Save round trip failed.");passed.Add("JSON save round trip");
                string result="PASS: "+string.Join("; ",passed);
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/gameplay-verification.txt",result);return result;
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(snapshot,game.Data);player.position=start;
                Set(game,"savePathOverride",null);Set(game,"dialog","");Set(game,"fishing",0);
                if(File.Exists(temporary))File.Delete(temporary);
                if(File.Exists(temporary+".bak"))File.Delete(temporary+".bak");
            }
        }
    }
}
