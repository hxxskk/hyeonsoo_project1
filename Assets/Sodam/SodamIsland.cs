using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sodam
{
    // All artwork is drawn from original geometric sprites; no external art is required.
    public sealed class SodamIsland : MonoBehaviour
    {
        [Serializable] public class Decoration { public float x, y; public int kind; }
        [Serializable] public class SaveData
        {
            public int version = 1, day = 1, coins = 40, apples, wood, fish, crops, seeds = 6;
            public float x = -4, y = -2;
            public int gathered, caught; public bool talked, rewarded, gifted;
            public bool[] picked = new bool[7];
            public int[] plots = new int[6];
            public float[] growth = new float[6];
            public List<Decoration> decorations = new List<Decoration>();
        }
        sealed class Spot
        {
            public string kind; public int index; public Vector2 pos; public GameObject root;
            public Spot(string k, int i, Vector2 p) { kind = k; index = i; pos = p; }
        }
        public SaveData Data { get; private set; }
        public bool Welcome { get; set; } = true;
        public string LastNotice { get; private set; }
        string savePathOverride;
        public string SavePath => savePathOverride ?? Path.Combine(Application.persistentDataPath, "sodam-island.json");
        readonly List<Spot> spots = new List<Spot>();
        readonly List<GameObject> ripples = new List<GameObject>();
        readonly List<Vector3> rippleOrigins = new List<Vector3>();
        readonly List<Transform> butterflies = new List<Transform>();
        readonly List<Vector2> treePositions = new List<Vector2> { new Vector2(-9,2), new Vector2(-8,-1), new Vector2(-7,-4), new Vector2(-4,4.3f), new Vector2(.5f,4.5f), new Vector2(5,3.5f), new Vector2(5,-3.2f) };
        readonly Dictionary<string, Texture2D> panels = new Dictionary<string, Texture2D>();
        Sprite circle, square, triangle; Camera cam; Transform player, shadow;
        Font font; AudioSource audioSource; AudioClip pluck, chime, water; Texture2D white;
        GUIStyle label, small, title, button, big;
        Color ink = Hex("294C48"), cream = Hex("FFF9EB"), green = Hex("377D69");
        Vector2 destination; bool walking, help, shop, decorating, paused, hasSave;
        int decorKind; float clock, noticeUntil, fishTime, saveTimer; int fishing;
        string dialog = ""; Spot nearby; Rect viewport;
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString("#" + s, out var c); return c; }

        void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Data = new SaveData();
            if (File.Exists(SavePath))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                    if (loaded != null && loaded.version == 1 && loaded.picked?.Length == 7 && loaded.plots?.Length == 6 && loaded.growth?.Length == 6)
                    { Data = loaded; Data.decorations ??= new List<Decoration>(); hasSave = true; }
                }
                catch (Exception e) { Debug.LogWarning("Save could not be loaded: " + e.Message); }
            }
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 24);
            MakeSprites(); MakeWorld(); MakeAudio();
            SetViewport();
            destination = new Vector2(Data.x, Data.y);
            player.position = new Vector3(Data.x, Data.y, 0);
            FindNearest();
            Notice("소담섬에 오신 걸 환영해요. 오늘은 천천히 걸어볼까요?");
        }

        void MakeSprites()
        {
            white = Texture2D.whiteTexture;
            var t = new Texture2D(96,96,TextureFormat.RGBA32,false);
            for (int y=0;y<96;y++) for (int x=0;x<96;x++)
            { float d = Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(48,48)); t.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(48-d))); }
            t.Apply(); circle = Sprite.Create(t,new Rect(0,0,96,96),new Vector2(.5f,.5f),96);
            square = Sprite.Create(white,new Rect(0,0,white.width,white.height),new Vector2(.5f,.5f),white.width);
            var tri = new Texture2D(96,96,TextureFormat.RGBA32,false);
            for(int y=0;y<96;y++) for(int x=0;x<96;x++) tri.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01((48-y*.5f)-Mathf.Abs(x-47.5f))));
            tri.Apply(); triangle=Sprite.Create(tri,new Rect(0,0,96,96),new Vector2(.5f,.5f),96);
        }
        GameObject Group(string name, Vector2 p, int order=0)
        { var g=new GameObject(name);g.transform.SetParent(transform);g.transform.position=p;return g; }
        GameObject Shape(string name, Transform parent, Vector2 p, Vector2 scale, Color color, int order, int kind=0, float angle=0)
        {
            var g=new GameObject(name); g.transform.SetParent(parent,false);g.transform.localPosition=p;g.transform.localScale=scale;g.transform.localRotation=Quaternion.Euler(0,0,angle);
            var sr=g.AddComponent<SpriteRenderer>();sr.sprite=kind==1?square:kind==2?triangle:circle;sr.color=color;sr.sortingOrder=order;return g;
        }
        void Oval(Transform p,float x,float y,float w,float h,string color,int order) => Shape("Paint",p,new Vector2(x,y),new Vector2(w,h),Hex(color),order);
        void Box(Transform p,float x,float y,float w,float h,string color,int order,float angle=0) => Shape("Paint",p,new Vector2(x,y),new Vector2(w,h),Hex(color),order,1,angle);
        void Tri(Transform p,float x,float y,float w,float h,string color,int order,float angle=0) => Shape("Paint",p,new Vector2(x,y),new Vector2(w,h),Hex(color),order,2,angle);
        int Depth(float y) => 2000-Mathf.RoundToInt(y*100);

        void MakeWorld()
        {
            cam = Camera.main;
            if(!cam) cam=new GameObject("Island Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            cam.orthographic=true;cam.orthographicSize=8.25f;cam.transform.position=new Vector3(-.6f,0,-10);cam.backgroundColor=Hex("86C9CB");cam.clearFlags=CameraClearFlags.SolidColor;
            var land=Group("Coast · sand and meadow",Vector2.zero).transform;
            Oval(land,-2,-.55f,21.1f,13.7f,"70B8BF",-10010);
            Oval(land,-2,-.1f,20.8f,13.6f,"B8E0D8",-10009);
            Oval(land,-2,0,20.1f,13.0f,"F5DDA6",-10008);
            Oval(land,-2,.4f,18.8f,11.7f,"77AC77",-10007);
            Oval(land,-2,.6f,18.5f,11.3f,"AFCC8B",-10006);
            Oval(land,-2,.7f,18.2f,10.9f,"BAD596",-10005);
            var rng=new System.Random(1204);
            for(int i=0;i<95;i++)
            {
                float x=(float)rng.NextDouble()*35-18,y=(float)rng.NextDouble()*20-10;
                if(Mathf.Pow((x+2)/11,2)+Mathf.Pow(y/7.3f,2)<1)continue;
                Box(land,x,y,.18f+(float)rng.NextDouble()*.5f,.025f,"C3E5DD",-10000);
            }
            // Winding, sun-warmed footpath.
            for(int i=0;i<38;i++)
            { float a=i/37f; float y=Mathf.Lerp(-5.3f,3.1f,a),x=-3.6f+Mathf.Sin(a*6)*.55f;Oval(land,x,y,1.38f,1.0f,"E9D5A5",-9900); }
            for(int i=0;i<24;i++)Oval(land,-3.5f+i*.32f,-2.0f+Mathf.Sin(i*.18f)*.25f,1.12f,.95f,"E9D5A5",-9900);
            for(int i=0;i<125;i++)
            {
                float x=(float)rng.NextDouble()*17-10.5f,y=(float)rng.NextDouble()*9.5f-4.5f;
                if(Mathf.Pow((x+2)/8.5f,2)+Mathf.Pow((y-.7f)/5.1f,2)>.9f || Mathf.Abs(x+3.6f)<1 || (x>1.6f&&y>-.4f&&y<4))continue;
                if(i%4==0) Flower(new Vector2(x,y),i%3);
                else { Box(land,x,y,.045f,.15f,"91B77B",-9800,-22);Box(land,x+.07f,y,.04f,.12f,"91B77B",-9800,18); }
            }
            // Pond with soft layered edges, lilies, and animated highlights.
            var pond=Group("Willow pond",new Vector2(3.3f,1.6f)).transform;
            Oval(pond,0,-.1f,4.8f,3.9f,"82AE7C",-9700);Oval(pond,0,0,4.35f,3.5f,"F0DCAE",-9699);
            Oval(pond,0,.08f,3.95f,3.12f,"619EAE",-9698);Oval(pond,0,.17f,3.8f,2.95f,"7CBFBE",-9697);
            for(int i=0;i<9;i++) { var glintPosition=new Vector2((float)rng.NextDouble()*2.6f-1.3f,(float)rng.NextDouble()*1.8f-.8f);var r=Shape("Water glint",pond,glintPosition,new Vector2(.4f,.026f),Hex("BDE4D6"),-9696,1);ripples.Add(r);rippleOrigins.Add(r.transform.localPosition); }
            Oval(pond,-.9f,.5f,.52f,.24f,"5D9B7C",-9690);Oval(pond,.8f,-.35f,.62f,.26f,"5D9B7C",-9690);Oval(pond,-.9f,.58f,.13f,.14f,"FFE4CF",-9689);
            // Boardwalk on the southern shore.
            for(int i=0;i<9;i++) { Box(land,1.5f,-4.3f-i*.24f,1.5f,.2f,"B8835C",-9600);Box(land,1.5f,-4.29f-i*.24f,1.3f,.025f,"DCA97B",-9599); }
            for(int i=0;i<4;i++)Oval(land,.7f+i%2*1.6f,-4.3f-i/2*1.7f,.14f,.22f,"795841",-9590);
            House();
            for(int i=0;i<treePositions.Count;i++) { var s=new Spot("tree",i,treePositions[i]);spots.Add(s);DrawTree(s); }
            for(int i=0;i<6;i++) { var s=new Spot("plot",i,new Vector2(-1.8f+(i%3)*.86f,2.55f-(i/3)*.85f));spots.Add(s);DrawPlot(s); }
            var home=new Spot("home",0,new Vector2(-6.1f,1.5f));spots.Add(home);
            var fishSpot=new Spot("pond",0,new Vector2(3.0f,-.65f));spots.Add(fishSpot);
            var p=Group("Pond sign",new Vector2(4.9f,-.4f)).transform;Box(p,0,0,.08f,.7f,"8B7157",Depth(-.4f));Box(p,0,.32f,.65f,.4f,"F7E6BA",Depth(-.4f)+1);
            Oval(p,-.04f,.32f,.23f,.11f,"619EAE",Depth(-.4f)+2);Tri(p,.12f,.32f,.13f,.15f,"619EAE",Depth(-.4f)+2,-90);
            var shopSpot=new Spot("shop",0,new Vector2(.2f,-2.2f));spots.Add(shopSpot);Stall(shopSpot.pos);
            var npc=new Spot("friend",0,new Vector2(-5.7f,-1.3f));spots.Add(npc);Character("Resident · 토리",npc.pos,true);
            player=Character("Player · 섬지기",new Vector2(Data.x,Data.y),false);
            shadow=player.Find("Shadow");
            foreach(var d in Data.decorations)DrawDecoration(d);
            for(int i=0;i<7;i++) {var b=Group("Butterfly",new Vector2(-8+i*2,3)).transform;Oval(b,-.06f,0,.13f,.16f,i%2==0?"FFF1BB":"F6CEBC",3500);Oval(b,.06f,0,.13f,.16f,i%2==0?"FFF1BB":"F6CEBC",3500);butterflies.Add(b);}
        }

        void House()
        {
            var p=Group("Your little cottage",new Vector2(-6.1f,2.9f)).transform;int z=Depth(2);
            Oval(p,0,-.65f,3.8f,.8f,"91AE78",z-1);Box(p,0,.1f,2.7f,2.4f,"F4E7C9",z);
            Box(p,0,-.83f,2.8f,.22f,"C4A47E",z+1);Box(p,.65f,1.85f,.36f,1.05f,"BC8A6B",z+1);
            Tri(p,0,1.65f,3.8f,1.65f,"446D66",z+2);Box(p,0,.86f,3.3f,.19f,"365C56",z+3);
            for(int i=0;i<4;i++) Box(p,0,1.08f+i*.23f,2.8f-i*.55f,.04f,"60857A",z+3);
            Box(p,-.15f,-.25f,.77f,1.45f,"A77955",z+4);Oval(p,.08f,-.24f,.07f,.07f,"FCE2A5",z+5);
            Box(p,.88f,.05f,.55f,.65f,"84B8B4",z+4);Box(p,.88f,.05f,.04f,.68f,"FFF8DE",z+5);Box(p,.88f,.05f,.58f,.04f,"FFF8DE",z+5);
            Box(p,-1,.05f,.55f,.65f,"84B8B4",z+4);Box(p,-1,.05f,.04f,.68f,"FFF8DE",z+5);Box(p,-1,.05f,.58f,.04f,"FFF8DE",z+5);
            Box(p,-.15f,-1.06f,1.25f,.3f,"D4BA91",z+6);
            Flower(new Vector2(-7.65f,1.8f),1);Flower(new Vector2(-4.6f,1.8f),0);
        }
        void DrawTree(Spot s)
        {
            if(s.root) Destroy(s.root);s.root=Group("Apple tree "+s.index,s.pos);var p=s.root.transform;int z=Depth(s.pos.y);
            Oval(p,.12f,-.1f,1.95f,.62f,"8AAE78",z-1);Box(p,0,.7f,.27f,1.55f,"957451",z);
            Oval(p,-.55f,1.35f,1.25f,1.4f,"4C8462",z+1);Oval(p,.54f,1.37f,1.2f,1.35f,"4C8462",z+1);Oval(p,0,1.9f,1.75f,1.6f,"4C8462",z+1);
            Oval(p,-.4f,1.65f,1.25f,1.4f,"72A370",z+2);Oval(p,.36f,1.8f,1.4f,1.55f,"80AD77",z+2);Oval(p,-.2f,2.12f,1.15f,1.05f,"90B77C",z+3);
            if(!Data.picked[s.index]) for(int i=0;i<3;i++) {float x=-.62f+i*.57f,y=1.44f+(i%2)*.56f;Oval(p,x,y,.27f,.28f,"E78670",z+4);Oval(p,x-.045f,y+.045f,.08f,.09f,"FFD0A0",z+5);Box(p,x,y+.17f,.035f,.11f,"685F42",z+5,-12);}
        }
        void DrawPlot(Spot s)
        {
            if(s.root)Destroy(s.root);s.root=Group("Garden bed "+s.index,s.pos);var p=s.root.transform;
            Box(p,0,0,.76f,.67f,"AD8764",-9500);Box(p,0,.02f,.64f,.54f,"BC9670",-9499);
            for(int i=0;i<3;i++)Box(p,0,-.16f+i*.17f,.58f,.025f,"9F7B5F",-9498);
            int stage=Data.plots[s.index];if(stage==0)return;
            if(stage==1) {Oval(p,-.12f,.02f,.07f,.06f,"EED7A1",-9490);Oval(p,.14f,-.08f,.07f,.06f,"EED7A1",-9490);return;}
            int z=Depth(s.pos.y);
            if(stage==2)Oval(p,0,0,.6f,.42f,"876D53",-9497);
            for(int i=0;i<3;i++) {float x=(i-1)*.2f;Box(p,x,.15f,.035f,.3f,"4D845B",z);Oval(p,x-.06f,.23f,.17f,.09f,"5E9962",z+1);Oval(p,x+.06f,.3f,.17f,.1f,"80AD6C",z+1);if(stage==3) {Oval(p,x,.12f,.16f,.25f,"EDA075",z+2);}}
        }
        Transform Character(string name,Vector2 pos,bool rabbit)
        {
            var p=Group(name,pos).transform;int z=Depth(pos.y);
            Shape("Shadow",p,new Vector2(.04f,-.12f),new Vector2(.78f,.27f),new Color(.2f,.36f,.29f,.2f),z);
            Oval(p,-.16f,-.04f,.23f,.2f,"705F53",z+1);Oval(p,.16f,-.04f,.23f,.2f,"705F53",z+1);
            Oval(p,0,.31f,.7f,.7f,rabbit?"D7AA7D":"DDA17F",z+2);
            Box(p,0,.27f,.55f,.5f,rabbit?"C78872":"659F9F",z+3);Oval(p,-.37f,.38f,.17f,.3f,rabbit?"F4DFC0":"F8D7B1",z+4);Oval(p,.37f,.38f,.17f,.3f,rabbit?"F4DFC0":"F8D7B1",z+4);
            if(rabbit) {Oval(p,-.21f,1.28f,.23f,.72f,"EAD2B2",z+4);Oval(p,.21f,1.28f,.23f,.72f,"EAD2B2",z+4);Oval(p,-.21f,1.3f,.1f,.44f,"DDAA98",z+5);Oval(p,.21f,1.3f,.1f,.44f,"DDAA98",z+5);}
            Oval(p,0,.88f,.82f,.77f,rabbit?"F4DFC0":"F9DAB9",z+5);
            if(!rabbit) {Oval(p,0,1.11f,.86f,.48f,"715B4B",z+6);Oval(p,0,1.23f,1.15f,.2f,"ECD7A4",z+7);Oval(p,0,1.41f,.75f,.4f,"ECD7A4",z+7);Box(p,0,1.3f,.72f,.09f,"9FBEA2",z+8);}
            Oval(p,-.16f,.9f,.055f,.075f,"3D4B41",z+9);Oval(p,.16f,.9f,.055f,.075f,"3D4B41",z+9);
            Oval(p,-.26f,.77f,.12f,.065f,"EAB199",z+9);Oval(p,.26f,.77f,.12f,.065f,"EAB199",z+9);Oval(p,0,.76f,.065f,.035f,"B78370",z+9);
            if(rabbit){Box(p,0,.51f,.59f,.13f,"89AFA3",z+10);Box(p,.17f,.39f,.12f,.27f,"89AFA3",z+10,-12);}
            return p;
        }
        void Stall(Vector2 pos)
        {
            var p=Group("Momo's market",pos).transform;int z=Depth(pos.y);
            Oval(p,0,-.15f,2.7f,.65f,"91AE78",z-1);Box(p,-1,.8f,.09f,1.8f,"967758",z);Box(p,1,.8f,.09f,1.8f,"967758",z);
            Box(p,0,.3f,2.2f,.7f,"C89D73",z+1);Box(p,0,.69f,2.36f,.16f,"E9C797",z+2);
            for(int i=0;i<6;i++){Box(p,-1+i*.4f,1.68f,.4f,.52f,i%2==0?"FFF0C9":"89B2A1",z+3);Oval(p,-1+i*.4f,1.4f,.4f,.2f,i%2==0?"FFF0C9":"89B2A1",z+3);}
            for(int i=0;i<3;i++)Oval(p,-.65f+i*.25f,.85f,.22f,.23f,"E58D77",z+3);
            Box(p,.55f,.87f,.45f,.29f,"F6DFAB",z+3);Box(p,.55f,.87f,.3f,.06f,"89AFA3",z+4);
        }
        void Flower(Vector2 pos,int c)
        {
            var p=Group("Wildflower",pos).transform;int z=Depth(pos.y);Box(p,0,.08f,.025f,.22f,"729D69",z);
            string col=c==0?"FFF0BF":c==1?"EFBCA8":"D9D8F1";
            for(int i=0;i<5;i++){float a=i*Mathf.PI*2/5;Oval(p,Mathf.Cos(a)*.07f,.2f+Mathf.Sin(a)*.07f,.11f,.1f,col,z+1);}Oval(p,0,.2f,.07f,.07f,"DEB465",z+2);
        }
        void DrawDecoration(Decoration d)
        {
            var p=Group("Placed decoration",new Vector2(d.x,d.y)).transform;int z=Depth(d.y);
            if(d.kind==0) {Box(p,-.3f,.08f,.08f,.35f,"927154",z);Box(p,.3f,.08f,.08f,.35f,"927154",z);Box(p,0,.3f,.9f,.22f,"C1966A",z+1);Box(p,0,.55f,.9f,.24f,"D5AC7D",z+1);}
            else if(d.kind==1) {Box(p,0,.45f,.07f,.95f,"8B7257",z);Oval(p,0,.9f,.42f,.6f,"F7DC9A",z+1);Box(p,0,1.2f,.45f,.1f,"779685",z+2);}
            else {Box(p,0,.15f,.52f,.32f,"D19E7E",z);for(int i=0;i<4;i++)Flower(new Vector2(d.x-.28f+i*.18f,d.y+.25f),i%3);}
        }

        void MakeAudio()
        {
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.volume=.14f;
            pluck=Tone(660,.16f);chime=Tone(880,.5f);water=Tone(330,.3f);
            var ambience=gameObject.AddComponent<AudioSource>();ambience.loop=true;ambience.volume=.045f;
            int len=44100*12;var samples=new float[len];var random=new System.Random(9);
            float[] notes={261.63f,329.63f,392f,493.88f,523.25f,392f};
            for(int n=0;n<len;n++){float t=n/44100f,local=t%2;float f=notes[Mathf.Min(5,(int)(t/2))];samples[n]=(Mathf.Sin(2*Mathf.PI*f*t)+.3f*Mathf.Sin(2*Mathf.PI*f*2*t))*Mathf.Exp(-local*2)*.16f+((float)random.NextDouble()-.5f)*.025f;}
            var clip=AudioClip.Create("Island breeze",len,1,44100,false);clip.SetData(samples,0);ambience.clip=clip;ambience.Play();
        }
        AudioClip Tone(float hz,float seconds)
        {int len=(int)(44100*seconds);var a=new float[len];for(int i=0;i<len;i++){float t=i/44100f;a[i]=Mathf.Sin(2*Mathf.PI*hz*t)*Mathf.Exp(-t*10)*.45f;}var c=AudioClip.Create("Soft chime",len,1,44100,false);c.SetData(a,0);return c;}
        void Sound(AudioClip c) { if(audioSource)audioSource.PlayOneShot(c); }

        void Update()
        {
            SetViewport();
            clock+=Time.deltaTime;
            for(int i=0;i<ripples.Count;i++)ripples[i].transform.localPosition=rippleOrigins[i]+new Vector3(Mathf.Sin(clock*.7f+i)*.14f,0,0);
            for(int i=0;i<butterflies.Count;i++){butterflies[i].position=new Vector3(-8+i*2+Mathf.Sin(clock*.3f+i)*.6f,3.4f+Mathf.Sin(clock*.5f+i*2)*1.2f,0);butterflies[i].localScale=new Vector3(.5f+Mathf.Abs(Mathf.Sin(clock*6+i))*.5f,1,1);}
            var k=Keyboard.current;
            if(k!=null && k.escapeKey.wasPressedThisFrame)
            {
                if(Welcome) Welcome=false;else if(help)help=false;else if(shop)shop=false;else if(dialog!="")dialog="";else if(decorating)decorating=false;else if(fishing>0){fishing=0;Notice("다음에 다시 낚아봐요.");}else paused=!paused;
                walking=false;
            }
            if(Welcome||help||shop||dialog!=""||paused)return;
            if(fishing>0){fishTime+=Time.deltaTime;if(fishing==1&&fishTime>1.9f){fishing=2;fishTime=0;Sound(water);}if(fishing==2&&fishTime>7){fishing=0;Notice("물고기가 쉬러 갔어요. 다시 시도해볼까요?");}if(k!=null&&(k.eKey.wasPressedThisFrame||k.spaceKey.wasPressedThisFrame))Reel();return;}
            Vector2 input=Vector2.zero;
            if(k!=null)
            {
                input.x=(k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0);
                input.y=(k.wKey.isPressed||k.upArrowKey.isPressed?1:0)-(k.sKey.isPressed||k.downArrowKey.isPressed?1:0);
                if(k.hKey.wasPressedThisFrame)help=true;
                if(k.bKey.wasPressedThisFrame){decorating=!decorating;walking=false;}
                if(k.eKey.wasPressedThisFrame&&!decorating)Interact();
            }
            if(Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mp=Mouse.current.position.ReadValue();float scale=viewport.width/1440f;
                Vector2 gui=new Vector2((mp.x-viewport.x)/scale,(Screen.height-mp.y-viewport.y)/scale);
                bool overDecorToolbar=decorating&&new Rect(220,153,720,112).Contains(gui);
                if(gui.x<1095&&gui.y>140&&gui.y<750&&!overDecorToolbar)
                {
                    Vector2 target=cam.ScreenToWorldPoint(mp);
                    if(decorating)PlaceDecoration(target);else {destination=target;walking=true;}
                }
            }
            if(input.sqrMagnitude>.01f)walking=false;
            if(walking){Vector2 delta=destination-(Vector2)player.position;if(delta.magnitude<.08f)walking=false;else input=delta.normalized;}
            Vector2 current=player.position,motion=input.normalized*Time.deltaTime*3.0f;
            Vector2 next=current+motion;
            if(Walkable(next))player.position=next;
            else if(Walkable(current+new Vector2(motion.x,0)))player.position=current+new Vector2(motion.x,0);
            else if(Walkable(current+new Vector2(0,motion.y)))player.position=current+new Vector2(0,motion.y);
            else walking=false;
            var renderers=player.GetComponentsInChildren<SpriteRenderer>();int depth=Depth(player.position.y);for(int i=0;i<renderers.Length;i++)renderers[i].sortingOrder=depth+i;
            player.localScale=new Vector3(1,1+(input.sqrMagnitude>.1f?Mathf.Sin(clock*12)*.025f:Mathf.Sin(clock*2)*.008f),1);
            Data.x=player.position.x;Data.y=player.position.y;
            for(int i=0;i<6;i++)if(Data.plots[i]==2){Data.growth[i]+=Time.deltaTime;if(Data.growth[i]>=45){Data.plots[i]=3;DrawPlot(spots.Find(s=>s.kind=="plot"&&s.index==i));Notice("텃밭의 당근이 다 자랐어요!");Save();}}
            FindNearest();saveTimer+=Time.deltaTime;if(saveTimer>20){saveTimer=0;Save();}
        }
        bool Walkable(Vector2 p)
        {
            if(Mathf.Pow((p.x+2)/8.65f,2)+Mathf.Pow((p.y-.6f)/5.0f,2)>.98f)return false;
            if(p.x>-7.7f&&p.x<-4.6f&&p.y>1.95f&&p.y<4.7f)return false;
            if(Mathf.Pow((p.x-3.3f)/2.25f,2)+Mathf.Pow((p.y-1.7f)/1.8f,2)<1)return false;
            if(p.x>-1.05f&&p.x<1.45f&&p.y>-2.25f&&p.y<-1.45f)return false;
            foreach(var t in treePositions)if(Vector2.Distance(p,t)<.42f)return false;
            return true;
        }
        void FindNearest()
        {nearby=null;float distance=1.5f;foreach(var s in spots){float d=Vector2.Distance(player.position,s.pos);if(d<distance){nearby=s;distance=d;}}}
        public string ActionLabel()
        {
            if(nearby==null)return "섬을 산책하며 가까이 다가가 보세요";
            switch(nearby.kind){case "tree":return Data.picked[nearby.index]?"내일 다시 열릴 사과나무":"사과 따기 · 나뭇가지 줍기";
                case "plot":return new[]{"씨앗 심기","물 주기","당근이 자라는 중…","당근 수확하기"}[Data.plots[nearby.index]];
                case "friend":return "토리와 이야기하기";case "shop":return "모모의 작은 상점";case "home":return "집에서 쉬고 다음 날 맞이하기";case "pond":return "연못에서 낚시하기";default:return "";}
        }
        public void Interact()
        {
            if(nearby==null)return;walking=false;int i=nearby.index;
            switch(nearby.kind)
            {
                case "tree":
                    if(Data.picked[i]){Notice("오늘은 모두 땄어요. 내일 다시 만나요!");return;}
                    Data.picked[i]=true;Data.apples+=3;Data.wood+=1;Data.gathered+=3;DrawTree(nearby);Notice("사과 3개와 나뭇가지 1개를 주웠어요.");Sound(pluck);break;
                case "plot":
                    if(Data.plots[i]==0){if(Data.seeds<=0){Notice("씨앗이 없어요. 모모의 상점에서 구할 수 있어요.");return;}Data.seeds--;Data.plots[i]=1;Notice("작은 당근 씨앗을 심었어요. 한 번 물을 주세요.");}
                    else if(Data.plots[i]==1){Data.plots[i]=2;Data.growth[i]=0;Notice("촉촉해졌어요! 45초 뒤 또는 내일 수확할 수 있어요.");Sound(water);}
                    else if(Data.plots[i]==2){Notice("천천히 자라는 중이에요. 산책하며 기다려 주세요.");return;}
                    else {Data.crops+=2;Data.plots[i]=0;Data.growth[i]=0;Notice("싱싱한 당근 2개를 수확했어요.");Sound(chime);}DrawPlot(nearby);break;
                case "friend":Data.talked=true;dialog=Data.day==1?"오늘부터 이 섬의 이웃이구나! 나는 토리야.\n\n급할 건 하나도 없어. 나무 아래서 사과도 줍고,\n연못에 앉아 물결도 바라봐.\n작은 일들이 모이면 여기가 너의 집이 될 거야.":"또 만나서 반가워! 오늘 바람은 조금 달콤하네.\n\n네가 놓아둔 작은 물건들 덕분에\n우리 섬이 매일 더 포근해지는 것 같아.";Sound(pluck);break;
                case "shop":shop=true;break;
                case "home":dialog="따뜻한 차 한 잔, 푹신한 이불.\n오늘도 충분히 잘 보냈어요.\n\n다음 날로 넘어가면 사과가 다시 열리고,\n물을 준 당근도 모두 자라요.";break;
                case "pond":fishing=1;fishTime=0;Sound(water);break;
            }
            Save();
        }
        public void Reel()
        {
            if(fishing!=2)return;
            float cursor=.5f+Mathf.Sin(fishTime*2.6f)*.43f;
            if(cursor>.29f&&cursor<.71f){Data.fish++;Data.caught++;Notice("은빛 물고기 한 마리! 가방에 담았어요.");Sound(chime);fishing=0;Save();}
            else {Notice("조금 빨랐어요. 초록 구간에서 다시 E를 눌러요.");Sound(pluck);}
        }
        public bool ClaimDaily()
        {if(Data.rewarded||Data.gathered<3||Data.caught<1||!Data.talked)return false;Data.coins+=80;Data.seeds+=3;Data.rewarded=true;Notice("오늘의 작은 행복 완료! 80잎과 씨앗 3개를 받았어요.");Sound(chime);Save();return true;}
        public void NextDay()
        {
            Data.day++;Array.Clear(Data.picked,0,7);Data.gathered=Data.caught=0;Data.talked=Data.rewarded=Data.gifted=false;
            for(int i=0;i<6;i++)if(Data.plots[i]==2)Data.plots[i]=3;
            foreach(var s in spots){if(s.kind=="tree")DrawTree(s);else if(s.kind=="plot")DrawPlot(s);}
            player.position=new Vector3(-5.8f,1.3f,0);Data.x=-5.8f;Data.y=1.3f;walking=false;dialog="";FindNearest();Notice("좋은 아침! "+Data.day+"일째의 소담섬이에요.");Sound(chime);Save();
        }
        public int SellAll()
        {int value=Data.apples*8+Data.fish*25+Data.crops*18;if(value==0){Notice("판매할 수확물이 아직 없어요.");return 0;}Data.coins+=value;Data.apples=Data.fish=Data.crops=0;Notice("오늘의 수확을 나눴어요. +"+value+"잎");Sound(chime);Save();return value;}
        public bool BuySeeds()
        {if(Data.coins<10){Notice("씨앗 3개에는 10잎이 필요해요.");return false;}Data.coins-=10;Data.seeds+=3;Sound(pluck);Save();return true;}
        public bool Gift()
        {if(Data.gifted||Data.apples<2)return false;Data.apples-=2;Data.coins+=25;Data.gifted=true;dialog="우와, 사과를 나눠 주는 거야? 고마워!\n\n내가 모아둔 25잎을 줄게.\n내일도 여기서 같이 산책하자.";Sound(chime);Save();return true;}
        public bool PlaceDecoration(Vector2 p)
        {
            if(Data.coins<35||Data.wood<2){Notice("꾸미기에는 35잎과 나뭇가지 2개가 필요해요.");return false;}
            if(!Walkable(p)||spots.Exists(s=>Vector2.Distance(s.pos,p)<1)||Data.decorations.Exists(d=>Vector2.Distance(new Vector2(d.x,d.y),p)<.9f)){Notice("길과 시설에서 조금 떨어진 빈 곳을 골라 주세요.");return false;}
            Data.coins-=35;Data.wood-=2;var decor=new Decoration{x=p.x,y=p.y,kind=decorKind};Data.decorations.Add(decor);DrawDecoration(decor);Sound(chime);Notice("우리 섬에 작은 쉼터가 생겼어요.");Save();return true;
        }
        public void Save()
        {
            try {string tmp=SavePath+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(Data,true));if(File.Exists(SavePath))File.Copy(SavePath,SavePath+".bak",true);File.Copy(tmp,SavePath,true);File.Delete(tmp);}
            catch(Exception e){Debug.LogWarning("Save failed: "+e.Message);Notice("저장하지 못했어요. 저장 폴더 권한을 확인해 주세요.");}
        }
        void OnApplicationQuit(){if(Data!=null)Save();}
        void OnApplicationPause(bool value){if(value&&Data!=null)Save();}
        void Notice(string message){LastNotice=message;noticeUntil=Time.unscaledTime+5;}
        void SetViewport()
        {
            float scale=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            viewport=new Rect((Screen.width-1440*scale)*.5f,(Screen.height-900*scale)*.5f,1440*scale,900*scale);
            if(cam)cam.pixelRect=viewport;
        }

        // A resolution-independent, keyboard/mouse-friendly illustrated journal UI.
        void Styles()
        {
            if(label!=null)return;
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=20,wordWrap=true};label.normal.textColor=ink;
            small=new GUIStyle(label){fontSize=16};title=new GUIStyle(label){fontSize=34,fontStyle=FontStyle.Bold};big=new GUIStyle(label){fontSize=54,fontStyle=FontStyle.Bold};
            button=new GUIStyle(label){alignment=TextAnchor.MiddleCenter,fontSize=18,fontStyle=FontStyle.Bold};
        }
        Texture2D Rounded(string color)
        {
            if(panels.TryGetValue(color,out var cached))return cached;
            var c=Hex(color);var tex=new Texture2D(64,64,TextureFormat.RGBA32,false);tex.wrapMode=TextureWrapMode.Clamp;
            for(int y=0;y<64;y++)for(int x=0;x<64;x++){var q=new Vector2(Mathf.Max(12-x,Mathf.Max(x-51,0)),Mathf.Max(12-y,Mathf.Max(y-51,0)));float a=1-Mathf.Clamp01(q.magnitude-11);tex.SetPixel(x,y,new Color(c.r,c.g,c.b,a));}tex.Apply();panels[color]=tex;return tex;
        }
        void Panel(Rect r,string color="FFF9EB")
        {
            if(Event.current.type!=EventType.Repaint)return;
            var style=new GUIStyle { border=new RectOffset(12,12,12,12) };
            style.normal.background=Rounded(color);style.Draw(r,GUIContent.none,false,false,false,false);
        }
        void Text(float x,float y,float w,float h,string text,GUIStyle style=null){GUI.Label(new Rect(x,y,w,h),text,style??label);}
        bool Button(Rect r,string text,bool primary=false,bool enabled=true)
        {
            Color prev=GUI.color;GUI.color=enabled?Color.white:new Color(1,1,1,.45f);Panel(r,primary?"377D69":"EAECD9");
            var st=new GUIStyle(button);st.normal.textColor=primary?cream:ink;
            bool clicked=GUI.Button(r,text,st)&&enabled;GUI.color=prev;if(clicked)Sound(pluck);return clicked;
        }
        void Line(float x,float y,float w){Color old=GUI.color;GUI.color=Hex("DDE2CC");GUI.DrawTexture(new Rect(x,y,w,1),white);GUI.color=old;}
        void OnGUI()
        {
            Styles();float scale=viewport.width/1440f;
            GUI.matrix=Matrix4x4.TRS(new Vector3(viewport.x,viewport.y,0),Quaternion.identity,new Vector3(scale,scale,1));
            Panel(new Rect(28,24,310,112));Text(50,35,265,47,"소담섬",title);Text(53,84,265,30,"S O D A M   ·   slow island days",small);
            Panel(new Rect(362,30,310,65));Text(385,44,270,38,"초록빛 계절     /     "+Data.day.ToString("00")+"일째");
            if(Button(new Rect(923,34,73,46),"도움말"))help=true;
            if(Button(new Rect(1005,34,72,46),"저장")){Save();Notice("지금의 소담섬을 저장했어요.");}
            // Right-hand journal and inventory.
            Panel(new Rect(1100,24,312,850));Text(1124,43,265,30,"ISLAND JOURNAL",small);Text(1124,78,255,46,"오늘의 작은 행복",new GUIStyle(title){fontSize=26});
            Text(1124,129,255,52,"서두르지 않아도 괜찮아요.\n하나씩, 마음이 가는 대로.",small);Line(1124,194,263);
            TaskRow(220,"사과 3개 채집",Data.gathered>=3,Mathf.Min(3,Data.gathered)+" / 3");
            TaskRow(267,"연못에서 물고기 낚기",Data.caught>=1,Mathf.Min(1,Data.caught)+" / 1");
            TaskRow(314,"이웃 토리와 인사",Data.talked,Data.talked?"완료":"0 / 1");
            bool complete=Data.gathered>=3&&Data.caught>=1&&Data.talked;
            Text(1124,369,260,27,"선물  80잎 + 당근 씨앗 3개",small);
            if(Button(new Rect(1124,408,263,47),Data.rewarded?"오늘도 잘 보냈어요 ✓":"작은 행복 선물 받기",true,complete&&!Data.rewarded))ClaimDaily();
            Line(1124,478,263);Text(1124,498,240,36,"나의 가방",new GUIStyle(title){fontSize=26});Text(1124,543,260,35,Data.coins+" 잎",new GUIStyle(title){fontSize=30});
            InventoryRow(594,"사과",Data.apples,"E58D77");InventoryRow(631,"물고기",Data.fish,"7CBFBE");InventoryRow(668,"당근",Data.crops,"E8AE74");InventoryRow(705,"씨앗",Data.seeds,"9EAB73");InventoryRow(742,"나뭇가지",Data.wood,"B78D6A");
            Text(1124,805,263,45,"자동 저장 · 20초마다\n꾸민 공간 "+Data.decorations.Count+"곳",small);
            // Map labels.
            WorldLabel(new Vector2(-6.1f,4.9f),"나의 작은 집");WorldLabel(new Vector2(-.9f,3.3f),"햇살 텃밭");WorldLabel(new Vector2(.2f,-.7f),"모모의 상점");WorldLabel(new Vector2(3.5f,3.65f),"잔물결 연못");WorldLabel(new Vector2(-5.7f,.6f),"토리");
            Panel(new Rect(28,781,1049,93));Text(51,794,1020,25,"WASD / 방향키  산책     ·     땅 클릭  이동     ·     E  상호작용     ·     B  꾸미기     ·     H  도움말",small);
            if(Button(new Rect(51,828,1002,34),"[ E ]  "+ActionLabel(),true,nearby!=null&&!IsModal()&&!decorating))Interact();
            if(Time.unscaledTime<noticeUntil&&!Welcome){Panel(new Rect(174,699,780,54),"FFF3D4");Text(192,712,746,34,LastNotice,new GUIStyle(small){alignment=TextAnchor.MiddleCenter});}
            if(decorating&&!IsModal())
            {
                Panel(new Rect(220,153,720,112));Text(242,166,680,28,"섬 꾸미기 · 빈 땅을 클릭해 놓아 주세요  /  35잎 + 나뭇가지 2개",small);
                string[] names={"나무 벤치","별빛 랜턴","꽃 화분"};for(int i=0;i<3;i++)if(Button(new Rect(242+i*225,209,210,39),names[i],decorKind==i))decorKind=i;
            }
            if(Welcome)WelcomeUI();else if(help)HelpUI();else if(shop)ShopUI();else if(dialog!="")DialogUI();else if(paused)PauseUI();else if(fishing>0)FishingUI();
        }
        bool IsModal()=>Welcome||help||shop||dialog!=""||paused||fishing>0;
        void TaskRow(float y,string name,bool done,string count)
        {Panel(new Rect(1124,y+4,24,24),done?"7EAA86":"E4E6D2");if(done)Text(1128,y,22,30,"✓",small);Text(1158,y,230,30,name,small);Text(1158,y+23,220,23,count,new GUIStyle(small){fontSize=13});}
        void InventoryRow(float y,string name,int amount,string col)
        {Panel(new Rect(1126,y+5,18,18),col);Text(1157,y,160,30,name,small);Text(1328,y,58,30,amount.ToString(),new GUIStyle(small){alignment=TextAnchor.UpperRight});}
        void WorldLabel(Vector2 p,string text)
        {Vector3 s=cam.WorldToScreenPoint(p);float scale=viewport.width/1440f;float x=(s.x-viewport.x)/scale,y=(Screen.height-s.y-viewport.y)/scale;if(x<70||x>1020||y<138||y>690)return;Panel(new Rect(x-69,y-12,138,29),"F5F3DA");Text(x-66,y-10,132,27,text,new GUIStyle(small){fontSize=13,alignment=TextAnchor.MiddleCenter});}
        void Dim(){Color c=GUI.color;GUI.color=new Color(.14f,.26f,.24f,.38f);GUI.DrawTexture(new Rect(0,0,1440,900),white);GUI.color=c;}
        void WelcomeUI()
        {
            Dim();Panel(new Rect(285,180,790,520));Text(334,222,670,30,"A LITTLE ISLAND, A LITTLE HAPPINESS",small);Text(330,270,680,86,"오늘도, 소담하게.",big);
            Text(334,378,670,106,"사과 향을 따라 걷고, 작은 씨앗에 물을 주고,\n이웃과 인사를 나누는 곳.\n당신의 속도로 이 섬의 하루를 채워 주세요.",new GUIStyle(label){fontSize=23});
            if(Button(new Rect(334,528,660,59),hasSave?"나의 섬으로 돌아가기":"소담섬에서 첫날 시작하기",true)){Welcome=false;Notice("가까운 사과나무로 걸어간 뒤 E를 눌러 보세요.");}
            Text(334,615,660,35,"시간 제한 없이 · 실패의 부담 없이 · 작은 일상부터",small);
        }
        void HelpUI()
        {
            Dim();Panel(new Rect(260,140,820,600));Text(304,177,710,55,"소담섬 생활 안내",title);
            Text(306,253,720,330,"01   산책   WASD / 방향키 또는 땅을 클릭해 이동해요.\n\n02   일상   가까이 다가가 E 또는 아래 초록 버튼을 눌러요.\n       사과나무 → 채집 / 텃밭 → 심기 · 물 주기 · 수확\n\n03   낚시   연못 앞에서 E. 입질 뒤 초록 구간에서 다시 E!\n\n04   나누기   상점에서 수확물을 팔고 씨앗을 살 수 있어요.\n       B로 꾸미기 → 가구 선택 → 빈 땅 클릭. Esc로 취소해요.\n\n05   내일   집 앞에서 E를 누르면 다음 날로 쉴 수 있어요.",label);
            Text(306,625,710,35,"20초마다 자동 저장됩니다. Esc는 창 닫기 / 일시 정지예요.",small);
            if(Button(new Rect(306,674,720,43),"천천히 해볼게요",true))help=false;
        }
        void ShopUI()
        {
            Dim();Panel(new Rect(300,200,740,480));Text(344,238,650,52,"모모의 작은 상점",title);Text(346,309,640,65,"“오늘 모은 것들을 이웃에게 나눠 주세요.\n  작은 씨앗으로 새 내일을 시작해도 좋고요.”",label);
            int value=Data.apples*8+Data.fish*25+Data.crops*18;Text(346,394,640,40,"사과 8잎   ·   물고기 25잎   ·   당근 18잎",small);
            if(Button(new Rect(346,449,645,50),"수확물 모두 판매하기   +"+value+"잎",true,value>0))SellAll();
            if(Button(new Rect(346,513,645,50),"당근 씨앗 3개 구입하기   10잎",false,Data.coins>=10))BuySeeds();
            if(Button(new Rect(346,594,645,43),"다음에 또 올게요"))shop=false;
        }
        void DialogUI()
        {
            Dim();Panel(new Rect(285,228,785,447));bool home=nearby!=null&&nearby.kind=="home";
            Text(329,264,700,52,home?"오늘 하루도 수고했어요":"숲속 이웃, 토리",title);Text(331,338,690,220,dialog,new GUIStyle(label){fontSize=22});
            if(home){if(Button(new Rect(331,588,327,49),"푹 자고 다음 날로",true))NextDay();if(Button(new Rect(675,588,327,49),"조금 더 산책할래요"))dialog="";}
            else {if(Button(new Rect(331,588,327,49),Data.gifted?"오늘의 선물 전달 완료":"사과 2개 선물 · +25잎",false,!Data.gifted&&Data.apples>=2))Gift();if(Button(new Rect(675,588,327,49),"또 만나, 토리!",true))dialog="";}
        }
        void PauseUI()
        {Dim();Panel(new Rect(410,270,600,365));Text(456,307,510,60,"잠깐, 쉬어가요.",title);Text(456,388,510,60,"당신이 돌아올 때까지 섬도 쉬고 있어요.",label);if(Button(new Rect(456,461,510,50),"산책 계속하기",true))paused=false;if(Button(new Rect(456,529,510,50),"저장하고 종료하기")){Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }}
        void FishingUI()
        {
            Panel(new Rect(251,475,720,196));Text(280,495,660,45,fishing==1?"찌를 바라보며, 잠시 기다려요…":"입질이에요! 초록 구간에서 E",new GUIStyle(title){fontSize=27});
            Panel(new Rect(286,557,648,24),"DDE5D1");Panel(new Rect(286+648*.29f,557,648*.42f,24),"80AC87");
            if(fishing==2){float cursor=.5f+Mathf.Sin(fishTime*2.6f)*.43f;Panel(new Rect(286+cursor*648-5,549,10,40),"365B52");}
            if(Button(new Rect(286,604,648,44),fishing==1?"물고기가 다가오고 있어요":"[ E ]  살짝 낚아올리기",true,fishing==2))Reel();
        }
    }
}
