using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class ZooBuilder
{
    class Sp { public string id; public int group; public string type, size, style; }
    struct Obb { public Vector2 c, ax; public float hw, hd; }

    const float R1 = 200f, R2 = 340f, ROAD = 10f, MAP = 600f, ZOO = 470f;
    static readonly float[] SpokeDeg = { 0, 45, 90, 135, 180, 225, 270, 315 };
    static readonly List<Obb> obbs = new List<Obb>();
    static readonly List<Rect> rects = new List<Rect>();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Mesh cubeMesh, cylMesh, sphMesh;
    static Font font;
    // Agac sistemi: Blender FBX agaclari parcalara (chunk) birlestirilir -> az draw call, gorus alani culling'i calisir
    class TS
    {
        public string id, trunk, fol; public float r, kin;
        public TS(string id, string trunk, string fol, float r, float kin) { this.id = id; this.trunk = trunk; this.fol = fol; this.r = r; this.kin = kin; }
    }
    static readonly TS[] TreeTable =
    {
        new TS("oak", "trunk_brown", "fol_A", 0.4f, 0.75f),
        new TS("plane_tree", "trunk_light", "fol_B", 0.5f, 0.6f),
        new TS("poplar", "trunk_light", "fol_B", 0.25f, 0.6f),
        new TS("pine", "trunk_brown", "fol_C", 0.3f, 0.7f),
        new TS("spruce", "trunk_brown", "fol_C", 0.3f, 0.8f),
        new TS("cypress", "trunk_brown", "fol_C", 0.3f, 0.8f),
        new TS("birch", "trunk_light", "fol_B", 0.2f, 0.85f),
        new TS("willow", "trunk_brown", "fol_D", 0.4f, 0.8f),
        new TS("acacia", "trunk_brown", "fol_D", 0.25f, 1f),
        new TS("baobab", "trunk_baobab", "fol_A", 2f, 0.55f),
        new TS("palm", "trunk_brown", "fol_B", 0.2f, 0.9f),
        new TS("eucalyptus", "trunk_light", "fol_E", 0.2f, 0.5f),
    };
    static readonly Dictionary<string, Color> TreeCols = new Dictionary<string, Color>
    {
        { "trunk_brown",  new Color(0.52f, 0.31f, 0.17f) },
        { "trunk_light",  new Color(0.9f, 0.84f, 0.7f) },
        { "trunk_baobab", new Color(0.68f, 0.52f, 0.4f) },
        { "fol_A", new Color(0.18f, 0.7f, 0.16f) },
        { "fol_B", new Color(0.42f, 0.88f, 0.2f) },
        { "fol_C", new Color(0.08f, 0.5f, 0.28f) },
        { "fol_D", new Color(0.66f, 0.86f, 0.2f) },
        { "fol_E", new Color(0.22f, 0.72f, 0.55f) },
    };
    class TreeData { public Mesh mesh; public Matrix4x4 toRoot; public int trunkSub, folSub; }
    static readonly Dictionary<string, TreeData> treeData = new Dictionary<string, TreeData>();
    static readonly Dictionary<string, MB> treeChunks = new Dictionary<string, MB>();
    static readonly Dictionary<string, List<Vector4>> treeCols = new Dictionary<string, List<Vector4>>();
    static readonly string[] innerPick = { "oak", "oak", "oak", "oak", "oak", "plane_tree", "plane_tree", "plane_tree", "birch", "birch", "birch", "acacia", "acacia",
        "palm", "palm", "poplar", "poplar", "eucalyptus", "baobab", "pine", "spruce", "cypress" };
    static readonly string[] outerPick = { "pine", "pine", "pine", "pine", "pine", "pine", "spruce", "spruce", "spruce", "spruce", "spruce", "poplar", "poplar",
        "cypress", "cypress", "oak", "oak", "oak", "birch", "birch", "plane_tree", "eucalyptus" };
    static System.Random rng;
    static float labelK = 1f;

    // ---------------------------------------------------------------- yardimcilar
    class MB
    {
        public List<CombineInstance> l = new List<CombineInstance>();
        public void Add(Mesh m, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            l.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, scale) });
        }
        public void AddSub(Mesh m, int sub, Matrix4x4 t)
        {
            l.Add(new CombineInstance { mesh = m, subMeshIndex = sub, transform = t });
        }
        public float tile;   // > 0 ise kutu yuzeylerine dunya olcekli (tile metre) UV verilir
        public void Box(Vector3 pos, Vector3 size, Quaternion rot)
        {
            if (tile > 0f) Add(MakeBox(size, tile), pos, rot, Vector3.one);
            else Add(cubeMesh, pos, rot, size);
        }
        public Mesh Build(string n)
        {
            var m = new Mesh { name = n, indexFormat = IndexFormat.UInt32 };
            m.CombineMeshes(l.ToArray(), true, true);
            m.RecalculateBounds();
            return m;
        }
    }

    // ---------------------------------------------------------------- doku sistemi (Poly Haven, CC0)
    class TexDef { public string id; public float tile; public Color? tint; }
    static readonly Dictionary<string, TexDef> TexMap = new Dictionary<string, TexDef>
    {
        // Dokular gri tonlamaya cevrilip (StyleTex) canli renklerle carpilir -> cizgi film / boyali gorunum
        { "road",          new TexDef { id = "dirt_aerial_02", tile = 7f, tint = new Color(0.56f, 0.6f, 0.66f) } },
        { "paving",        new TexDef { id = "concrete_pavers_02", tile = 5f, tint = new Color(1.0f, 0.95f, 0.8f) } },
        { "wall_cream",    new TexDef { id = "white_plaster_02", tile = 6f, tint = new Color(1.1f, 0.98f, 0.76f) } },
        { "wall_Aquarium", new TexDef { id = "white_plaster_02", tile = 6f, tint = new Color(0.9f, 1.05f, 1.15f) } },
        { "wall_ReptileHouse", new TexDef { id = "white_plaster_02", tile = 6f, tint = new Color(0.95f, 1.1f, 0.8f) } },
        { "wall_gray",     new TexDef { id = "white_plaster_02", tile = 6f, tint = new Color(0.55f, 0.95f, 0.95f) } },
        { "roof_brown",    new TexDef { id = "roof_tiles", tile = 4f, tint = new Color(1.1f, 0.45f, 0.3f) } },
        { "roof_ReptileHouse", new TexDef { id = "grey_roof_tiles", tile = 4f, tint = new Color(0.35f, 0.95f, 0.5f) } },
        { "stone",         new TexDef { id = "old_stone_wall", tile = 4f, tint = new Color(1.05f, 0.95f, 0.8f) } },
        { "floor_tile",    new TexDef { id = "large_floor_tiles_02", tile = 4f, tint = new Color(0.8f, 1.0f, 1.05f) } },
        { "wood",          new TexDef { id = "brown_planks_03", tile = 3f, tint = new Color(1.1f, 0.72f, 0.4f) } },
        { "fence_wood",    new TexDef { id = "brown_planks_03", tile = 2f, tint = new Color(1.1f, 0.72f, 0.4f) } },
        { "floor_pen",     new TexDef { id = "dirt_aerial_02", tile = 6f, tint = new Color(1.0f, 0.76f, 0.5f) } },
        { "floor_aviary",  new TexDef { id = "dry_ground_01", tile = 6f, tint = new Color(1.05f, 0.9f, 0.6f) } },
        { "floor_farm",    new TexDef { id = "grass_path_2", tile = 5f, tint = new Color(0.6f, 1.05f, 0.3f) } },
        { "trunk",         new TexDef { id = "tree_bark_03", tile = 1f, tint = new Color(0.85f, 0.55f, 0.32f) } },
        { "trunk_brown",   new TexDef { id = "tree_bark_03", tile = 1f, tint = new Color(0.8f, 0.52f, 0.32f) } },
        { "trunk_light",   new TexDef { id = "tree_bark_03", tile = 1f, tint = new Color(1.3f, 1.22f, 1.05f) } },
        { "trunk_baobab",  new TexDef { id = "tree_bark_03", tile = 1f, tint = new Color(1.0f, 0.74f, 0.56f) } },
    };
    static readonly Dictionary<Material, float> tileOf = new Dictionary<Material, float>();

    static Texture2D LoadTex(string id, string suffix)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Zoo/Textures/" + id + "/" + id + "_" + suffix + ".jpg");
    }

    static void PrepTextures()
    {
        AssetDatabase.Refresh();
        var ids = new HashSet<string> { "grass_path_2", "sand_01" };
        foreach (var d in TexMap.Values) ids.Add(d.id);
        foreach (var id in ids)
        {
            var nor = AssetImporter.GetAtPath("Assets/Zoo/Textures/" + id + "/" + id + "_nor.jpg") as TextureImporter;
            if (nor != null && nor.textureType != TextureImporterType.NormalMap)
            { nor.textureType = TextureImporterType.NormalMap; nor.SaveAndReimport(); }
            var dif = AssetImporter.GetAtPath("Assets/Zoo/Textures/" + id + "/" + id + "_diff.jpg") as TextureImporter;
            if (dif != null && dif.anisoLevel < 8)
            { dif.anisoLevel = 8; dif.SaveAndReimport(); }
        }
    }

    // Poly Haven dokusunu gri tonlamaya cevirir (yalniz detay kalir), renk materyal/arazi tintinden gelir.
    static Texture2D StyleTex(string id)
    {
        string p = "Assets/Zoo/Stylized/" + id + "_s.png";
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        if (t != null) return t;
        EnsureFolder("Assets/Zoo/Stylized");
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        src.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "Zoo/Textures/" + id + "/" + id + "_diff.jpg")));
        var px = src.GetPixels32();
        double sum = 0;
        for (int i = 0; i < px.Length; i++) sum += (0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b) / 255f;
        float mean = (float)(sum / px.Length);
        for (int i = 0; i < px.Length; i++)
        {
            float lum = (0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b) / 255f;
            byte v = (byte)(Mathf.Clamp01(0.9f + (lum - mean) * 0.8f) * 255f);
            px[i] = new Color32(v, v, v, 255);
        }
        src.SetPixels32(px);
        src.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Zoo/Stylized/" + id + "_s.png"), src.EncodeToPNG());
        Object.DestroyImmediate(src);
        AssetDatabase.ImportAsset(p);
        var imp = AssetImporter.GetAtPath(p) as TextureImporter;
        if (imp != null) { imp.anisoLevel = 8; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static TerrainLayer TLayer(string name, string id, float size, Color tint)
    {
        var l = new TerrainLayer { diffuseTexture = StyleTex(id), normalMapTexture = LoadTex(id, "nor"), tileSize = new Vector2(size, size) };
        l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        AssetDatabase.CreateAsset(l, "Assets/Zoo/layer_" + name + ".terrainlayer");
        return l;
    }

    static Mesh MakeBox(Vector3 s, float tile)
    {
        var V = new List<Vector3>(); var UV = new List<Vector2>(); var N = new List<Vector3>(); var T = new List<int>();
        Vector3 h = s * 0.5f;
        Vector3[] ns = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] us = { Vector3.up, Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up };
        Vector3[] ws = { Vector3.forward, Vector3.up, Vector3.right, Vector3.forward, Vector3.up, Vector3.right };
        for (int f = 0; f < 6; f++)
        {
            Vector3 n = ns[f], u = Vector3.Scale(us[f], s), w = Vector3.Scale(ws[f], s);
            Vector3 o = Vector3.Scale(n, h) - u * 0.5f - w * 0.5f;
            int b = V.Count;
            Vector3[] c = { o, o + u, o + u + w, o + w };
            foreach (var p in c)
            {
                V.Add(p); N.Add(n);
                Vector2 q = f < 2 ? new Vector2(p.z, p.y) : f < 4 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                UV.Add(q / tile);
            }
            T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3);
        }
        var m = new Mesh();
        m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
        m.RecalculateBounds();
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }

    static Material Mat(string name, Color c, float alpha = 1f, float emission = 0f)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        string path = "Assets/Zoo/Materials/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        TexDef td;
        if (TexMap.TryGetValue(name, out td))
        {
            if (td.tint.HasValue) c = td.tint.Value;
            m.SetTexture("_BaseMap", StyleTex(td.id));
            var nrm = LoadTex(td.id, "nor");
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            tileOf[m] = td.tile;
        }
        c.a = alpha;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.15f);
        m.SetFloat("_Metallic", 0f);
        if (alpha < 0.99f)
        {
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetFloat("_Cull", 0);
        }
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 lp, Vector3 scale, Material m, bool col = true, Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localScale = scale;
        if (rot.HasValue) go.transform.localRotation = rot.Value;
        go.GetComponent<Renderer>().sharedMaterial = m;
        float tl;
        if (t == PrimitiveType.Cube && tileOf.TryGetValue(m, out tl))
        {
            go.GetComponent<MeshFilter>().sharedMesh = MakeBox(scale, tl);
            go.transform.localScale = Vector3.one;
            var bcol = go.GetComponent<BoxCollider>();
            if (bcol != null) bcol.size = scale;
        }
        if (!col) { var c = go.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }
        return go;
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 size, Material m, bool col = true)
    {
        return Prim(PrimitiveType.Cube, name, parent, lp, size, m, col);
    }

    static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, bool collider = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    static float TextWidth(string text, int fontSize, float cs)
    {
        font.RequestCharactersInTexture(text, fontSize);
        float w = 0f;
        foreach (char ch in text)
        {
            CharacterInfo ci;
            if (font.GetCharacterInfo(ch, out ci, fontSize)) w += ci.advance;
        }
        return w * cs * 0.1f;
    }

    static void Label(Transform parent, string text, Vector3 lp, float yRot, float charSize, float maxWidth, Color col)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = Quaternion.Euler(0, yRot, 0);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = col;
        tm.text = text;
        float cs = charSize * labelK;
        float w = TextWidth(text, 64, cs);
        if (w > maxWidth && w > 0f) cs *= maxWidth / w;
        tm.characterSize = cs;
        go.GetComponent<MeshRenderer>().sharedMaterial = LabelMat();
        go.AddComponent<LabelBinder>();
    }

    // Derinlik testi yapan (duvarlarin arkasindan gorunmeyen) yazi fontu
    static Font MakeFont()
    {
        Font f = AssetDatabase.LoadAssetAtPath<Font>("Assets/Zoo/Fonts/GameFont.ttf");
        if (f != null) return f;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    static Material LabelMat()
    {
        const string path = "Assets/Zoo/Materials/label.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Zoo/LabelText"));
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    static string Pretty(string id)
    {
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
    }

    static bool InObb(Vector2 p, float pad)
    {
        foreach (var o in obbs)
        {
            Vector2 d = p - o.c;
            float lx = Vector2.Dot(d, o.ax);
            float lz = Vector2.Dot(d, new Vector2(-o.ax.y, o.ax.x));
            if (Mathf.Abs(lx) < o.hw + pad && Mathf.Abs(lz) < o.hd + pad) return true;
        }
        foreach (var r in rects)
            if (p.x > r.xMin - pad && p.x < r.xMax + pad && p.y > r.yMin - pad && p.y < r.yMax + pad) return true;
        return false;
    }

    static bool OnRoad(Vector2 p)
    {
        float r = p.magnitude;
        if (Mathf.Abs(r - R1) < 11f || Mathf.Abs(r - R2) < 11f || r < 122f) return true;
        foreach (float d in SpokeDeg)
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float along = Vector2.Dot(p, u);
            float end = d == 270f ? 470f : 350f;
            if (along > 190f && along < end)
            {
                float perp = Mathf.Abs(p.x * u.y - p.y * u.x);
                if (perp < 10f) return true;
            }
        }
        foreach (float d in new float[] { 0f, 90f, 180f })
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float along = Vector2.Dot(p, u);
            if (along > 105f && along < 200f && Mathf.Abs(p.x * u.y - p.y * u.x) < 8f) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- arazi
    static float GroundY(float x, float z)
    {
        float g = 0f;
        float r = Mathf.Sqrt(x * x + z * z);
        float t = Mathf.Clamp01((108f - r) / 48f);
        g -= 8f * t * t * (3f - 2f * t);
        float ix = x - 30f, iz = z - 12f;
        float ir = Mathf.Sqrt(ix * ix + iz * iz);
        float it = Mathf.Clamp01((20f - ir) / 10f);
        if (it > 0f) g = Mathf.Max(g, -8f + 9f * it * it * (3f - 2f * it));
        float c = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        float rt = Mathf.Clamp01((c - 480f) / 120f);
        if (rt > 0f) g += 38f * rt * rt * (0.35f + 0.65f * Mathf.PerlinNoise(x * 0.008f + 10f, z * 0.008f + 10f));
        return g;
    }

    static TerrainLayer MakeLayer(string name, Color c)
    {
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float n = (Mathf.PerlinNoise(x * 0.18f, y * 0.18f) - 0.5f) * 0.12f;
                tex.SetPixel(x, y, new Color(c.r + n, c.g + n, c.b + n, 1f));
            }
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply();
        AssetDatabase.CreateAsset(tex, "Assets/Zoo/tex_" + name + ".asset");
        var layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(12, 12) };
        AssetDatabase.CreateAsset(layer, "Assets/Zoo/layer_" + name + ".terrainlayer");
        return layer;
    }

    static void BuildTerrain(Transform root)
    {
        const int res = 513;
        const float size = 1200f, height = 60f, baseY = 12f;
        var td = new TerrainData { heightmapResolution = res };
        td.size = new Vector3(size, height, size);
        var h = new float[res, res];
        for (int zi = 0; zi < res; zi++)
            for (int xi = 0; xi < res; xi++)
            {
                float wx = -MAP + xi * size / (res - 1), wz = -MAP + zi * size / (res - 1);
                h[zi, xi] = (GroundY(wx, wz) + baseY) / height;
            }
        td.SetHeights(0, 0, h);
        td.terrainLayers = new[]
        {
            TLayer("grassA", "grass_path_2", 10f, new Color(0.26f, 0.72f, 0.08f)),
            TLayer("grassB", "grass_path_2", 14f, new Color(0.46f, 0.86f, 0.14f)),
            TLayer("sandV", "sand_01", 10f, new Color(1.1f, 0.95f, 0.6f))
        };
        td.alphamapResolution = 512;
        var am = new float[512, 512, 3];
        for (int zi = 0; zi < 512; zi++)
            for (int xi = 0; xi < 512; xi++)
            {
                float wx = -MAP + (xi + 0.5f) * size / 512f, wz = -MAP + (zi + 0.5f) * size / 512f;
                float s = Mathf.Clamp01((0.6f - GroundY(wx, wz)) / 0.9f);
                float n = Mathf.PerlinNoise(wx * 0.011f + 40f, wz * 0.011f + 40f) * 0.7f + Mathf.PerlinNoise(wx * 0.05f + 7f, wz * 0.05f + 7f) * 0.3f;
                n = Mathf.SmoothStep(0.25f, 0.75f, n);
                am[zi, xi, 0] = (1f - s) * (1f - n);
                am[zi, xi, 1] = (1f - s) * n;
                am[zi, xi, 2] = s;
            }
        td.SetAlphamaps(0, 0, am);
        AssetDatabase.CreateAsset(td, "Assets/Zoo/ZooTerrain.asset");
        var tgo = Terrain.CreateTerrainGameObject(td);
        tgo.name = "Terrain";
        tgo.transform.SetParent(root);
        tgo.transform.position = new Vector3(-MAP, -baseY, -MAP);
        tgo.GetComponent<Terrain>().drawInstanced = true;
    }

    static void BuildLake(Transform root)
    {
        var lake = new GameObject("Lake").transform;
        lake.SetParent(root, false);
        Prim(PrimitiveType.Cylinder, "Water", lake, new Vector3(0, -0.5f, 0), new Vector3(208f, 0.02f, 208f),
             LakeWater(), false);
        EnvPier(lake);
    }

    // ---------------------------------------------------------------- yollar
    static void RingBoxes(MB mb, float radius, float width, int seg)
    {
        float len = 2f * Mathf.PI * radius / seg * 1.1f;
        for (int i = 0; i < seg; i++)
        {
            float a = i * 2f * Mathf.PI / seg;
            Vector3 pos = new Vector3(Mathf.Cos(a) * radius, 0.04f, Mathf.Sin(a) * radius);
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            mb.Box(pos, new Vector3(width, 0.08f, len), Quaternion.LookRotation(tangent));
        }
    }

    static void SpokeBox(MB mb, float deg, float r0, float r1, float width)
    {
        float a = deg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        float len = r1 - r0;
        mb.Box(dir * (r0 + len / 2f) + Vector3.up * 0.04f, new Vector3(width, 0.08f, len), Quaternion.LookRotation(dir));
    }

    // Yollar: tek parca duz seritler (dunya olcekli UV). Katmanlar farkli yukseklikte, ust uste binen
    // es-duzlemli yuzey olmadigi icin titreme (z-fighting) yok.
    class RB
    {
        public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>();
        public float tile = 6f;
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float y)
        {
            int i = V.Count;
            foreach (var p in new[] { a, b, c, d }) { V.Add(new Vector3(p.x, y, p.z)); UV.Add(new Vector2(p.x / tile, p.z / tile)); }
            if (Vector3.Cross(b - a, c - a).y > 0f) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
            else { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
        }
        public void Ring(float r, float hw, int seg, float y)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * 2f * Mathf.PI / seg, a1 = (i + 1) * 2f * Mathf.PI / seg;
                Quad(new Vector3(Mathf.Cos(a0) * (r - hw), 0, Mathf.Sin(a0) * (r - hw)), new Vector3(Mathf.Cos(a0) * (r + hw), 0, Mathf.Sin(a0) * (r + hw)),
                     new Vector3(Mathf.Cos(a1) * (r + hw), 0, Mathf.Sin(a1) * (r + hw)), new Vector3(Mathf.Cos(a1) * (r - hw), 0, Mathf.Sin(a1) * (r - hw)), y);
            }
        }
        public void Spoke(float deg, float r0, float r1, float w, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), perp = new Vector3(-dir.z, 0, dir.x) * (w / 2f);
            Quad(dir * r0 - perp, dir * r0 + perp, dir * r1 + perp, dir * r1 - perp, y);
        }
        public void Rect(float x0, float z0, float x1, float z1, float y)
        {
            Quad(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), y);
        }
        public Mesh Build(string n)
        {
            var m = new Mesh { name = n, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(V); m.SetUVs(0, UV); m.SetTriangles(T, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    static void BuildRoads(Transform root)
    {
        var roads = new GameObject("Roads").transform;
        roads.SetParent(root, false);
        const float yRing = 0.08f, ySpoke = 0.14f;
        var asp = new RB { tile = 7f };
        asp.Ring(R1, ROAD / 2f, 240, yRing);
        asp.Ring(R2, ROAD / 2f, 360, yRing);
        foreach (float d in SpokeDeg)
        {
            asp.Spoke(d, R1 + ROAD / 2f - 0.3f, R2 - ROAD / 2f + 0.3f, 9f, ySpoke);
            if (d == 270f) asp.Spoke(d, R2 + ROAD / 2f - 0.3f, 425f, 9f, ySpoke);
        }
        MeshObj("Road", roads, asp.Build("road"), Mat("road", new Color(0.8f, 0.6f, 0.4f)), true);
        var pav = new RB { tile = 5f };
        pav.Ring(114f, 2.5f, 120, yRing);
        foreach (float d in new float[] { 0f, 90f, 180f })
            pav.Spoke(d, 116.3f, R1 - ROAD / 2f + 0.3f, 5f, ySpoke);
        pav.Rect(-48f, -465f, 48f, -425f, ySpoke);
        pav.Rect(-15f, -193f, 15f, -183f, ySpoke);
        MeshObj("Paving", roads, pav.Build("paving"), Mat("paving", new Color(0.9f, 0.85f, 0.7f)), true);
        rects.Add(new Rect(-48f, -465f, 96f, 40f));
    }

    static void BuildRoadsOld(Transform root)
    {
        var roads = new GameObject("Roads").transform;
        roads.SetParent(root, false);
        var asphalt = new MB { tile = 8f };
        RingBoxes(asphalt, R1, ROAD, 240);
        RingBoxes(asphalt, R2, ROAD, 360);
        foreach (float d in SpokeDeg)
            SpokeBox(asphalt, d, R1 - 5f, d == 270f ? 445f : R2 + 5f, 9f);
        MeshObj("Asphalt", roads, asphalt.Build("asphalt"), Mat("road", new Color(0.27f, 0.28f, 0.3f)), false);
        var pave = new MB { tile = 6f };
        RingBoxes(pave, 114f, 5f, 120);
        foreach (float d in new float[] { 0f, 90f, 180f })
            SpokeBox(pave, d, 114f, R1 - 5f, 5f);
        pave.Box(new Vector3(0, 0.05f, -445f), new Vector3(96f, 0.1f, 40f), Quaternion.identity);
        pave.Box(new Vector3(0, 0.05f, -150f - 38f), new Vector3(30f, 0.1f, 10f), Quaternion.identity);
        MeshObj("Paving", roads, pave.Build("paving"), Mat("paving", new Color(0.66f, 0.63f, 0.57f)), false);
        rects.Add(new Rect(-48f, -465f, 96f, 40f));
    }

    // ---------------------------------------------------------------- binalar
    static void SimpleBuilding(Transform parent, string name, string label, Vector3 center, Vector3 frontDir, float frontW, float depth, float h, Material wallMat, Material roofMat)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.position = center;
        root.rotation = Quaternion.LookRotation(frontDir);
        Box(root, "Walls", new Vector3(0, h / 2f, 0), new Vector3(frontW, h, depth), wallMat);
        Box(root, "Roof", new Vector3(0, h + 0.35f, 0), new Vector3(frontW + 1.5f, 0.7f, depth + 1.5f), roofMat);
        Box(root, "Door", new Vector3(0, 1.6f, depth / 2f + 0.06f), new Vector3(Mathf.Min(5f, frontW * 0.4f), 3.2f, 0.15f), Mat("door_dark", new Color(0.12f, 0.1f, 0.09f)), false);
        Box(root, "SignBoard", new Vector3(0, h - 1.4f, depth / 2f + 0.2f), new Vector3(frontW * 0.8f, 1.5f, 0.3f), Mat("sign_dark", new Color(0.07f, 0.27f, 0.3f)), false);
        Label(root, label, new Vector3(0, h - 1.4f, depth / 2f + 0.4f), 180f, 0.06f, frontW * 0.74f, Color.white);
        Vector3 e = Quaternion.LookRotation(frontDir) * new Vector3(frontW, 0, depth);
        rects.Add(new Rect(center.x - Mathf.Abs(e.x) / 2f, center.z - Mathf.Abs(e.z) / 2f, Mathf.Abs(e.x), Mathf.Abs(e.z)));
    }

    static void BuildEntrance(Transform root)
    {
        var ent = new GameObject("Entrance").transform;
        ent.SetParent(root, false);
        EnvEntrance(ent);
        var spawn = new GameObject("PlayerStart");
        spawn.transform.SetParent(ent, false);
        spawn.transform.position = new Vector3(0, 0.3f, -432f);
        rects.Add(new Rect(-50f, -470f, 140f, 50f));
    }

    static void BuildPerimeter(Transform root)
    {
        var p = new GameObject("Perimeter").transform;
        p.SetParent(root, false);
        EnvPerimeter(p);
    }

    // ---------------------------------------------------------------- akvaryum ve teraryum salonlari
    static void BuildHall(Transform parent, string name, Vector3 center, float rotY, float W, float D, float H, float doorW,
                          List<Sp> sps, string title, Color water, Color accent, bool waves, float slot)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.position = center;
        root.rotation = Quaternion.Euler(0, rotY, 0);
        EnvShell(root, name, title, doorW);

        int cnt = (int)((W - 4f) / slot);
        int idx = 0;
        for (int row = 0; row < 2; row++)
        {
            int dirSign = row == 0 ? 1 : -1;       // 1 = kuzey duvar, -1 = guney duvar
            for (int i = 0; i < cnt; i++)
            {
                float x = -((cnt - 1) * slot) / 2f + i * slot;
                if (row == 1 && Mathf.Abs(x) - slot / 2f < doorW / 2f) continue;
                if (idx >= sps.Count) break;
                MakeTank(root, null, null, null, sps[idx], x, dirSign, D, slot);
                idx++;
            }
        }
        if (idx < sps.Count) Debug.LogWarning(name + ": " + (sps.Count - idx) + " tur sigmadi");
        for (int i = 0; i < 6; i++)
        {
            var lg = new GameObject("Light" + i);
            lg.transform.SetParent(root, false);
            lg.transform.localPosition = new Vector3(-W / 2f + (i + 0.5f) * W / 6f, H - 1.5f, 0);
            var li = lg.AddComponent<Light>();
            li.type = LightType.Point;
            li.color = new Color(0.78f, 0.9f, 1f);
            li.intensity = 14f;
            li.range = 26f;
        }
        Vector3 ext = Quaternion.Euler(0, rotY, 0) * new Vector3(W, 0, D);
        rects.Add(new Rect(center.x - Mathf.Abs(ext.x) / 2f, center.z - Mathf.Abs(ext.z) / 2f, Mathf.Abs(ext.x), Mathf.Abs(ext.z)));
    }

    static void MakeTank(Transform hall, Material wat, Material dark, Material glass, Sp sp, float x, int s, float D, float slot)
    {
        var t = new GameObject("Tank_" + sp.id).transform;
        t.SetParent(hall, false);
        t.localPosition = new Vector3(x, 0, 0);
        EnvTank(t, sp, s, slot);
        float fz = EnvTankFront;
        var darkM = Mat("tank_dark", new Color(0.1f, 0.12f, 0.14f));
        Box(t, "NameBoard", new Vector3(0, 4.7f, fz + s * 0.15f), new Vector3(slot - 0.2f, 0.9f, 0.1f), darkM, false);
        Label(t, Pretty(sp.id), new Vector3(0, 4.7f, fz + s * 0.06f), s == 1 ? 0f : 180f, 0.05f, slot - 0.5f, Color.white);
        var enc = t.gameObject.AddComponent<Enclosure>();
        enc.speciesId = sp.id; enc.displayName = Pretty(sp.id); enc.kind = sp.type;
        var dz = new GameObject("DropZone");
        dz.transform.SetParent(t, false);
        dz.transform.localPosition = new Vector3(0, 1.6f, fz - s * 1.2f);
        var bc = dz.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(slot, 3.2f, 2.4f);
    }

    // ---------------------------------------------------------------- kafesler
    static void Dims(Sp s, out float w, out float d, out float h)
    {
        switch (s.size)
        {
            case "XL": w = 40; d = 34; h = 4f; break;
            case "L": w = 30; d = 26; h = 3.2f; break;
            case "M": w = 22; d = 20; h = 2.4f; break;
            default: w = 15; d = 14; h = 1.8f; break;
        }
        if (s.type == "aviary") h = 7f;
        if (s.style == "wood") h = 1.3f;
    }

    static void Side(MB mb, Vector3 a, Vector3 b, float h, bool bars, bool wood)
    {
        Vector3 d = b - a;
        float L = d.magnitude;
        if (L < 0.01f) return;
        Vector3 dir = d / L;
        float sp = bars ? 0.5f : 2.4f;
        int n = Mathf.Max(1, Mathf.RoundToInt(L / sp));
        float pw = bars ? 0.06f : (wood ? 0.28f : 0.22f);
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = a + dir * (L * i / n);
            mb.Box(p + Vector3.up * (h / 2f), new Vector3(pw, h, pw), Quaternion.identity);
        }
        Quaternion q = Quaternion.LookRotation(dir);
        Vector3 mid = (a + b) / 2f;
        float rt = bars ? 0.12f : 0.1f;
        mb.Box(mid + Vector3.up * h, new Vector3(rt, rt, L), q);
        mb.Box(mid + Vector3.up * (h * 0.5f), new Vector3(rt, rt, L), q);
        if (wood) mb.Box(mid + Vector3.up * (h * 0.75f), new Vector3(rt, rt, L), q);
    }

    static TS TreeStyleOf(string id)
    {
        foreach (var t in TreeTable) if (t.id == id) return t;
        return TreeTable[0];
    }

    static TreeData GetTree(string id)
    {
        TreeData td;
        if (treeData.TryGetValue(id, out td)) return td;
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/" + id + ".fbx");
        var mf = go.GetComponentInChildren<MeshFilter>();
        var mr = mf.GetComponent<MeshRenderer>();
        TreeReadable("Assets/Trees/" + id + ".fbx");
        td = new TreeData { mesh = mf.sharedMesh, toRoot = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix, trunkSub = 1, folSub = 0 };
        for (int i = 0; i < mr.sharedMaterials.Length; i++)
        {
            string n = mr.sharedMaterials[i] != null ? mr.sharedMaterials[i].name : "";
            if (n.Contains("trunk")) td.trunkSub = i; else td.folSub = i;
        }
        td.mesh = TreeMesh(mf.sharedMesh, td.trunkSub, td.folSub, id);
        treeData[id] = td;
        return td;
    }

    static void ChunkAdd(int cx, int cz, string mat, Mesh m, int sub, Matrix4x4 t)
    {
        string key = cx + "_" + cz + "|" + mat;
        MB mb;
        if (!treeChunks.TryGetValue(key, out mb)) { mb = new MB(); treeChunks[key] = mb; }
        mb.AddSub(m, sub, t);
    }

    static void PlaceTree(string id, Vector3 pos, float scale, float yaw, bool collide)
    {
        if (UserTrees && PlaceUserTree(id, pos, scale, yaw, collide)) return;
        var td = GetTree(id);
        var ts = TreeStyleOf(id);
        Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), Vector3.one * scale) * td.toRoot;
        int cx = Mathf.FloorToInt(pos.x / 100f), cz = Mathf.FloorToInt(pos.z / 100f);
        ChunkAdd(cx, cz, ts.trunk, td.mesh, td.trunkSub, m);
        ChunkAdd(cx, cz, ts.fol, td.mesh, td.folSub, m);
        if (collide)
        {
            string k = cx + "_" + cz;
            List<Vector4> l;
            if (!treeCols.TryGetValue(k, out l)) { l = new List<Vector4>(); treeCols[k] = l; }
            l.Add(new Vector4(pos.x, pos.y, pos.z, ts.r * scale));
        }
    }

    static void PlaceAuto(Vector3 pos, bool inner)
    {
        var pick = inner ? innerPick : outerPick;
        string id = pick[rng.Next(pick.Length)];
        var ts = TreeStyleOf(id);
        float sc = inner ? ts.kin * (0.8f + 0.35f * (float)rng.NextDouble()) : 0.9f + 0.5f * (float)rng.NextDouble();
        PlaceTree(id, pos, sc, (float)rng.NextDouble() * 360f, inner);
    }

    // kafes ici kucuk agac
    static void AddTree(Vector3 pos, float s)
    {
        string[] pick = { "oak", "birch", "acacia", "palm", "oak" };
        PlaceTree(pick[rng.Next(pick.Length)], pos, 0.4f * s + 0.1f * (float)rng.NextDouble(), (float)rng.NextDouble() * 360f, true);
    }

    static void BuildEnclosure(Transform parent, Sp sp, Vector3 pos, Quaternion rot, float w, float d, float h)
    {
        var root = new GameObject("Enc_" + sp.id).transform;
        root.SetParent(parent, false);
        root.position = pos;
        root.rotation = rot;
        var enc = root.gameObject.AddComponent<Enclosure>();
        enc.speciesId = sp.id; enc.displayName = Pretty(sp.id); enc.kind = sp.type;
        Material floorMat = sp.type == "aviary" ? Mat("floor_aviary", new Color(0.62f, 0.56f, 0.44f))
                          : sp.style == "wood" ? Mat("floor_farm", new Color(0.42f, 0.5f, 0.24f))
                          : Mat("floor_pen", new Color(0.46f, 0.4f, 0.3f));
        Box(root, "Floor", new Vector3(0, 0.1f, 0), new Vector3(w - 0.8f, 0.2f, d - 0.8f), floorMat);
        float gate = 6f;
        EnvFence(root, sp, w, d, h);
        EnvInterior(root, sp, w, d);
        var dz = new GameObject("DropZone");
        dz.transform.SetParent(root, false);
        dz.transform.localPosition = new Vector3(0, 1.5f, d / 2f - 2.5f);
        var bc = dz.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(gate, 3f, 5f);
        if (sp.type == "open" && sp.size != "S")
        {
            int nt = sp.size == "XL" ? 3 : 2;
            for (int i = 0; i < nt; i++)
            {
                float lx = (i == 0 ? -1f : i == 1 ? 1f : 0f) * (w / 2f - 4f);       // arka koseler ve orta
                float lz = -d / 2f + 3.5f + (i == 2 ? 3f : 0f);
                AddTree(root.TransformPoint(new Vector3(lx, 0.2f, lz)), 0.8f + (float)rng.NextDouble() * 0.4f);
            }
        }
        obbs.Add(new Obb { c = new Vector2(pos.x, pos.z), ax = new Vector2((rot * Vector3.right).x, (rot * Vector3.right).z), hw = w / 2f, hd = d / 2f });
    }

    static void PlaceRing(Transform parent, List<Sp> list, ref int idx, float ringR, int side, float startDeg)
    {
        float theta = startDeg * Mathf.Deg2Rad;
        float endT = theta + 350f * Mathf.Deg2Rad;
        while (idx < list.Count)
        {
            Sp sp = list[idx];
            float w, d, h;
            Dims(sp, out w, out d, out h);
            float rc = ringR + side * (ROAD / 2f + 3f + d / 2f);
            float dtheta = w / rc;
            bool moved;
            int guard = 0;
            do
            {
                moved = false;
                foreach (float sd in SpokeDeg)
                {
                    float phi = sd * Mathf.Deg2Rad;
                    float clear = 7f / rc;
                    float phin = phi;
                    while (phin < theta - Mathf.PI) phin += 2f * Mathf.PI;
                    while (phin > theta + Mathf.PI) phin -= 2f * Mathf.PI;
                    if (phin > theta - clear && phin < theta + dtheta + clear)
                    {
                        theta = phin + clear;
                        moved = true;
                    }
                }
            } while (moved && ++guard < 20);
            if (theta + dtheta > endT) break;
            float mid = theta + dtheta / 2f;
            Vector3 u = new Vector3(Mathf.Cos(mid), 0, Mathf.Sin(mid));
            Vector3 pos = u * rc;
            Vector3 toRoad = side > 0 ? -u : u;
            BuildEnclosure(parent, sp, pos, Quaternion.LookRotation(toRoad), w, d, h);
            theta += dtheta + 3f / rc;
            idx++;
        }
    }

    // ---------------------------------------------------------------- ana akis
    static List<Sp> LoadSpecies()
    {
        var res = new List<Sp>();
        foreach (string line in File.ReadAllLines(Path.Combine(Application.dataPath, "Data/species.csv")).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var c = line.Trim().Split(',');
            res.Add(new Sp { id = c[0], group = int.Parse(c[1]), type = c[2], size = c[3], style = c[4] });
        }
        return res;
    }

    // ---------------------------------------------------------------- tasarlanmis agac yerlesimi
    static float Rnd() { return (float)rng.NextDouble(); }

    static bool Plant(string id, float x, float z, float jitter, float pad, bool checkRoad)
    {
        var p = new Vector2(x, z);
        if (checkRoad && OnRoad(p)) return false;
        if (InObb(p, pad)) return false;
        var ts = TreeStyleOf(id);
        PlaceTree(id, new Vector3(x, 0f, z), ts.kin * (1f - jitter + 2f * jitter * Rnd()), Rnd() * 360f, true);
        return true;
    }

    // Yolun iki yaninda duzenli, dar tacli agac sirasi (a ve b turleri sirayla)
    static void SpokeAvenue(float deg, float r0, float r1, float off, float step, string a, string b)
    {
        float ang = deg * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), perp = new Vector2(-dir.y, dir.x);
        int n = Mathf.FloorToInt((r1 - r0) / step);
        for (int i = 0; i <= n; i++)
        {
            float r = r0 + i * step;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                Vector2 p = dir * r + perp * (side * off);
                Plant(((i + s) % 2 == 0) ? a : b, p.x, p.y, 0.05f, 0.5f, false);
            }
        }
    }

    // Halka seklinde esit aralikli sira (yol kollarinin yakini atlanir)
    static void RingAvenue(float r, float stepM, string id)
    {
        int n = Mathf.RoundToInt(2f * Mathf.PI * r / stepM);
        for (int i = 0; i < n; i++)
        {
            float a = (i + 0.5f) * 2f * Mathf.PI / n;
            float deg = a * Mathf.Rad2Deg;
            bool nearSpoke = false;
            foreach (float d in SpokeDeg)
                if (Mathf.Abs(Mathf.DeltaAngle(deg, d)) * Mathf.Deg2Rad * r < 9f) nearSpoke = true;
            if (nearSpoke) continue;
            Plant(id, Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.05f, 3f, false);
        }
    }

    // Korulik: ortada uzun, kenara dogru kucuk agaclar (ay cicegi dizilimi)
    static void Grove(float cx, float cz, float rad, int n, string[] mix)
    {
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            float rr = rad * Mathf.Sqrt(t);
            float th = i * 2.39996f + Rnd() * 0.3f;
            float x = cx + Mathf.Cos(th) * rr, z = cz + Mathf.Sin(th) * rr;
            string id = rr < rad * 0.45f ? mix[0] : mix[1 + (i % (mix.Length - 1))];
            var ts = TreeStyleOf(id);
            var p = new Vector2(x, z);
            if (OnRoad(p) || InObb(p, 2.5f)) continue;
            PlaceTree(id, new Vector3(x, 0, z), ts.kin * Mathf.Lerp(1.05f, 0.7f, rr / rad) * (0.92f + 0.16f * Rnd()), Rnd() * 360f, true);
        }
    }

    // Iki katli kusak: kesintisiz, duzenli (arka sira kozalakli, on sira genis yapraklilar)
    static void PerimeterBand()
    {
        string[] broad = { "oak", "plane_tree", "birch" };
        string[] conif = { "pine", "spruce" };
        for (int side = 0; side < 4; side++)
        {
            int i = 0;
            for (float v = -460f; v <= 460f; v += 11f, i++)
            {
                Vector2 p = Side2(side, v, 452f);
                if (side == 1 && Mathf.Abs(v) < 24f) continue;
                Plant(broad[(i / 5) % 3], p.x, p.y, 0.07f, 4f, false);
            }
            i = 0;
            for (float v = -458f; v <= 458f; v += 8f, i++)
            {
                Vector2 p = Side2(side, v + 4f, 462f);
                if (side == 1 && Mathf.Abs(v) < 24f) continue;
                Plant(conif[(i / 6) % 2], p.x, p.y, 0.1f, 4f, false);
            }
        }
    }

    static Vector2 Side2(int side, float v, float u)
    {
        switch (side)
        {
            case 0: return new Vector2(v, u);      // kuzey
            case 1: return new Vector2(v, -u);     // guney (giris)
            case 2: return new Vector2(u, v);      // dogu
            default: return new Vector2(-u, v);    // bati
        }
    }

    static void PlantDesigned()
    {
        // 1) Yol kollari boyunca dar agaclardan cadde (selvi - kavak)
        foreach (float d in SpokeDeg)
            SpokeAvenue(d, R1 + 12f, R2 - 12f, 5.9f, 12f, "cypress", "poplar");
        SpokeAvenue(270f, 392f, 420f, 10f, 18f, "plane_tree", "plane_tree");       // girise giden genis cinar yolu
        foreach (float d in new float[] { 0f, 90f, 180f })
            SpokeAvenue(d, 126f, 188f, 5.2f, 14f, "birch", "cypress");             // gol yuruyus yolu
        // 2) Gol cevresinde esit aralikli salkim sogut, ic halka yolunda cinar sirasi
        RingAvenue(125f, 16f, "willow");
        RingAvenue(188f, 22f, "plane_tree");
        // 3) Gol parkinda korulikler
        string[] parkMix = { "oak", "birch", "plane_tree" };
        for (int k = 0; k < 4; k++)
        {
            float a = (45f + 90f * k) * Mathf.Deg2Rad;
            Grove(Mathf.Cos(a) * 156f, Mathf.Sin(a) * 156f, 17f, 11, parkMix);
        }
        for (int k = 0; k < 8; k++)
        {
            float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
            Grove(Mathf.Cos(a) * 166f, Mathf.Sin(a) * 166f, 10f, 6, new[] { "birch", "poplar", "oak" });
        }
        // 4) Kafes halkalari arasindaki yesil kusakta her dilime ayri turde cift sira
        string[] wedge = { "oak", "plane_tree", "birch", "acacia", "oak", "plane_tree", "palm", "birch" };
        for (int k = 0; k < 8; k++)
        {
            float a0 = 45f * k, a1 = 45f * (k + 1);
            for (int row = 0; row < 2; row++)
            {
                float r = row == 0 ? 266f : 277f;
                float stepDeg = 15f / r * Mathf.Rad2Deg;
                for (float a = a0 + 8f / r * Mathf.Rad2Deg + (row == 1 ? stepDeg * 0.5f : 0f); a < a1 - 8f / r * Mathf.Rad2Deg; a += stepDeg)
                {
                    float rad = a * Mathf.Deg2Rad;
                    Plant(wedge[k], Mathf.Cos(rad) * r, Mathf.Sin(rad) * r, 0.07f, 2f, false);
                }
            }
        }
        // 5) Giris meydani: simetrik selvi sirasi, akvaryum onunde iki palmiye
        for (int s = -1; s <= 1; s += 2)
        {
            PlaceTree("palm", new Vector3(20f * s, 0f, -190f), 0.9f, Rnd() * 360f, true);
        }
        // 6) Dis kose ve kenar korulikleri
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Grove(415f * sx, 415f * sz, 24f, 16, new[] { "oak", "plane_tree", "birch", "palm" });
        for (int k = 0; k < 8; k++)
        {
            float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
            Grove(Mathf.Cos(a) * 412f, Mathf.Sin(a) * 412f, 18f, 10, new[] { "oak", "birch", "plane_tree", "acacia" });
        }
        // 7) Cevre duvari boyunca iki katli kusak
        PerimeterBand();
        // 8) Duvarin disinda dogal orman: ayri koruluklar ve aciklikler, yukseklige gore tur
        for (float gx = -596f; gx <= 596f; gx += 15f)
            for (float gz = -596f; gz <= 596f; gz += 15f)
            {
                float x = gx + (Rnd() - 0.5f) * 9f, z = gz + (Rnd() - 0.5f) * 9f;
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 478f) continue;
                if (Mathf.Abs(x) < 30f && z < -470f) continue;
                float n = Mathf.PerlinNoise(x * 0.012f + 90f, z * 0.012f + 90f);
                if (n < 0.38f) continue;
                float h = GroundY(x, z);
                float m = Mathf.PerlinNoise(x * 0.02f + 300f, z * 0.02f + 300f);
                float q = Rnd();
                string id;
                if (h > 14f || m > 0.6f) id = q < 0.45f ? "pine" : q < 0.8f ? "spruce" : q < 0.92f ? "poplar" : "cypress";
                else id = q < 0.35f ? "oak" : q < 0.55f ? "birch" : q < 0.7f ? "plane_tree" : q < 0.85f ? "pine" : "spruce";
                PlaceTree(id, new Vector3(x, h, z), 0.95f + 0.45f * Rnd(), Rnd() * 360f, false);
            }
    }

    [MenuItem("Zoo/Build Zoo")]
    public static void Build()
    {
        rng = new System.Random(1234);
        obbs.Clear(); rects.Clear(); mats.Clear(); tileOf.Clear();
        EnsureFolder("Assets/Zoo/Materials");
        PrepTextures();
        EnsureFolder("Assets/Scenes");
        cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        cylMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        sphMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        font = MakeFont();
        treeData.Clear(); treeChunks.Clear(); treeCols.Clear();
        EnvPrep();
        GrassPrep();

        var all = LoadSpecies();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("Zoo").transform;

        BuildTerrain(root);
        BuildLake(root);
        BuildRoads(root);
        BuildEntrance(root);
        BuildPerimeter(root);
        EnvPark(root);

        var aq = all.Where(s => s.type == "aquarium").ToList();
        var rep = all.Where(s => s.type == "reptile").ToList();
        BuildHall(root, "Aquarium", new Vector3(0, 0, -150f), 0f, 134f, 56f, 9f, 14f, aq, "AQUARIUM",
                  new Color(0.2f, 0.75f, 1f), new Color(0.08f, 0.52f, 0.95f), true, 3.6f);
        BuildHall(root, "ReptileHouse", new Vector3(27f, 0, -270f), 90f, 48f, 22f, 8f, 6f, rep, "REPTILE HOUSE",
                  new Color(0.35f, 0.85f, 0.45f), new Color(0.2f, 0.65f, 0.35f), false, 4f);

        var encRoot = new GameObject("Enclosures").transform;
        encRoot.SetParent(root, false);
        var land = all.Where(s => s.type == "open" || s.type == "aviary")
                      .OrderBy(s => s.style == "wood" ? 0 : s.type == "aviary" ? 2 : 1)
                      .ThenBy(s => s.group).ToList();
        int idx = 0;
        PlaceRing(encRoot, land, ref idx, R1, +1, 280f);
        PlaceRing(encRoot, land, ref idx, R2, -1, 280f);
        PlaceRing(encRoot, land, ref idx, R2, +1, 280f);
        if (idx < land.Count) Debug.LogWarning("Yerlestirilemeyen kafes: " + (land.Count - idx));

        // agaçlar
        PlantDesigned();
        var props = new GameObject("Trees").transform;
        props.SetParent(root, false);
        foreach (var kv in treeChunks)
        {
            string matName = kv.Key.Substring(kv.Key.IndexOf('|') + 1);
            MeshObj("Trees_" + kv.Key.Replace('|', '_'), props, kv.Value.Build(kv.Key), (matName.StartsWith("ut_") ? UtMat(matName) : matName.StartsWith("fol_") ? FoliageMat(matName, TreeCols[matName]) : Mat(matName, TreeCols[matName])), false);
        }
        EnvScatter();
        GrassScatter();
        EnvFurniture();
        EFlush(root);
        GrassFlush(root);
        foreach (var kv in treeCols)
        {
            var g = new GameObject("TreeCol_" + kv.Key);
            g.transform.SetParent(props, false);
            foreach (var v in kv.Value)
            {
                var cap = g.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(v.x, v.y + 2f, v.z);
                cap.radius = Mathf.Max(0.2f, v.w);
                cap.height = 4f;
            }
        }

        // Su shader'i yuzey altindaki derinligi okur: URP derinlik dokusunu ac
        var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null) { urpAsset.supportsCameraDepthTexture = true; EditorUtility.SetDirty(urpAsset); }

        var cam = Camera.main;
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0, 0.1f, -432f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.55f;
        player.AddComponent<ZooPlayer>();
        if (cam != null)
        {
            cam.transform.SetParent(player.transform, false);
            cam.transform.localPosition = new Vector3(0, 1.65f, 0);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true;
            cd.requiresDepthTexture = true;
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null && sun.type == LightType.Directional)
        {
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0);
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.96f, 0.86f);
            sun.shadows = LightShadows.Soft;
        }
        // Canli, cizgi film tonlu atmosfer: mavi gokyuzu isigi, sicak zemin yansimasi, hafif sis
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.6f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.3f, 0.22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.72f, 0.87f, 1f);
        RenderSettings.fogDensity = 0.0009f;
        var vgo = new GameObject("PostProcessing");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = prof.Add<ColorAdjustments>(true);
        ca.saturation.Override(14f); ca.contrast.Override(14f); ca.postExposure.Override(-0.1f);
        var tmap = prof.Add<Tonemapping>(true);
        tmap.mode.Override(TonemappingMode.Neutral);
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(1.0f); bloom.intensity.Override(0.25f);
        AssetDatabase.CreateAsset(prof, "Assets/Zoo/ZooVolume.asset");
        vol.sharedProfile = prof;

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/ZooMain.unity");
        Debug.Log("Zoo hazir: " + all.Count + " tur, kafes " + idx + "/" + land.Count + ", tank " + aq.Count + ", teraryum " + rep.Count);
    }
}
