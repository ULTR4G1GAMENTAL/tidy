using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Blender'dan gelen ortam varliklarini (Assets/Env/<paket>/<ad>.fbx) haritaya yerlestirir.
public static partial class ZooBuilder
{
    // ---------------------------------------------------------------- model yukleme
    class EnvModel { public Mesh mesh; public string[] mats; public Color[] cols; public Bounds b; }
    static readonly Dictionary<string, EnvModel> envModels = new Dictionary<string, EnvModel>();

    // ---------------------------------------------------------------- bina kaplamalari (Poly Haven, UV'siz modellere duzlem tabanli UV)
    static bool IsBld(string id)
    {
        // Bellek icin hafif/hayvan/bitki paketleri disinda her model kaplanir
        return !(id.StartsWith("K1/") || id.StartsWith("K2/") || id.StartsWith("N1/") || id.StartsWith("A3/"));
    }

    static void RegisterBldTex()
    {
        System.Action<string, string, float, Color> reg = (n, tex, tile, tint) => TexMap["env_bld_" + n] = new TexDef { id = tex, tile = tile, tint = tint };
        reg("plaster_cream", "white_plaster_02", 6f, new Color(1.1f, 0.98f, 0.76f));
        reg("plaster_white", "white_plaster_02", 6f, new Color(1.0f, 1.02f, 1.05f));
        reg("roof_red", "roof_tiles", 4f, new Color(1.1f, 0.45f, 0.3f));
        reg("roof_teal", "grey_roof_tiles", 4f, new Color(0.3f, 0.95f, 0.95f));
        reg("deep_blue", "metal_plate", 3f, new Color(0.12f, 0.5f, 1.0f));
        reg("water_blue", "metal_plate", 3f, new Color(0.2f, 0.78f, 1.1f));
        reg("stone_sand", "old_stone_wall", 4f, new Color(1.05f, 0.95f, 0.8f));
        reg("stone_gray", "large_floor_tiles_02", 4f, new Color(0.82f, 1.0f, 1.05f));
        reg("wood_warm", "brown_planks_03", 3f, new Color(1.1f, 0.72f, 0.4f));
        reg("wood_dark", "brown_planks_03", 3f, new Color(0.7f, 0.42f, 0.22f));
        reg("iron_teal", "metal_plate", 2f, new Color(0.16f, 0.32f, 0.34f));
        reg("metal_gray", "metal_plate", 2f, new Color(0.7f, 0.75f, 0.78f));
        reg("grass_dark", "grass_path_2", 3f, new Color(0.2f, 0.7f, 0.25f));
        reg("sand_cream", "sand_01", 3f, new Color(1.1f, 0.95f, 0.7f));
        reg("soil_orange", "dirt_aerial_02", 3f, new Color(1.0f, 0.7f, 0.45f));
    }

    static int QKey(Vector3 p) { return (Mathf.RoundToInt(p.x * 50f) * 73856093) ^ (Mathf.RoundToInt(p.y * 50f) * 19349663) ^ (Mathf.RoundToInt(p.z * 50f) * 83492791); }

    static Mesh UvMesh(Mesh src, string[] names, string id)
    {
        var P = src.vertices;
        // --- parcalari (baglantili bilesenler) bul: iskele bankalarini ve akvaryum arka kapisini kaldirmak icin
        HashSet<int> dropV = new HashSet<int>();
        if (id == "S2/pier_dock_lake")
        {
            int[] par = new int[P.Length];
            for (int i = 0; i < par.Length; i++) par[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
            var byPos = new Dictionary<int, int>();
            for (int i = 0; i < P.Length; i++) { int k = QKey(P[i]); int o; if (byPos.TryGetValue(k, out o)) par[find(i)] = find(o); else byPos[k] = i; }
            for (int s = 0; s < src.subMeshCount; s++)
            { int[] t = src.GetTriangles(s); for (int i = 0; i + 2 < t.Length; i += 3) { par[find(t[i + 1])] = find(t[i]); par[find(t[i + 2])] = find(t[i]); } }
            var bnd = new Dictionary<int, Bounds>();
            for (int i = 0; i < P.Length; i++)
            { int r = find(i); Bounds bb; if (bnd.TryGetValue(r, out bb)) { bb.Encapsulate(P[i]); bnd[r] = bb; } else bnd[r] = new Bounds(P[i], Vector3.zero); }
            var kill = new HashSet<int>();
            foreach (var kv in bnd)
            {
                Vector3 c = kv.Value.center, sz = kv.Value.size;
                if (c.z > 32.3f && c.z < 33.8f && c.y > 0.6f && c.y < 1.6f && Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) < 4.5f) kill.Add(kv.Key);
            }
            for (int i = 0; i < P.Length; i++) if (kill.Contains(find(i))) dropV.Add(i);
        }
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>();
        var subs = new List<int>[src.subMeshCount];
        for (int s = 0; s < src.subMeshCount; s++)
        {
            subs[s] = new List<int>();
            TexDef td; float tile = TexMap.TryGetValue("env_bld_" + names[s], out td) ? td.tile : 1f;
            bool glass = names[s] == "glass";
            int[] t = src.GetTriangles(s);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                if (dropV.Contains(t[i])) continue;
                Vector3 a = P[t[i]], b = P[t[i + 1]], c = P[t[i + 2]];
                if (id == "A1/aquarium_building" && names[s] == "iron_teal")
                {
                    Vector3 ce = (a + b + c) / 3f;
                    if (ce.x > -3.1f && ce.x < 3.1f && ce.y < 3.6f && ce.z > 26.6f && ce.z < 27.9f) continue; // arka kapi: acik birak
                }
                Vector3 n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude < 1e-10f ? Vector3.up : n.normalized;
                Vector3 u, v;
                if (Mathf.Abs(n.y) > 0.999f) { u = Vector3.right; v = Vector3.forward; }
                else { u = Vector3.Cross(Vector3.up, n).normalized; v = Vector3.Cross(n, u); }
                if (glass) { Vector3 sh = -n * 0.005f; a += sh; b += sh; c += sh; } // cerceveyle ayni duzlemde titremesin (z-fighting)
                foreach (var p in new[] { a, b, c })
                {
                    subs[s].Add(nv.Count);
                    nv.Add(p); nn.Add(n);
                    nuv.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / tile);
                }
            }
        }
        var m = new Mesh { name = src.name + "_uv", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nuv);
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) m.SetTriangles(subs[s], s);
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    // ---------------------------------------------------------------- su: dalga, yansima, derinlik rengi, kiyi kopugu
    static Material WaterMat(string name, Color shallow, Color deep, float scale, float speed, float strength, float depthRange, float foam, float opacity, float glint)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        var sh = Shader.Find("Zoo/Water");
        if (sh == null) { Debug.LogWarning("Zoo/Water shader bulunamadi"); return Mat(name, deep, 0.7f, 0.3f); }
        string path = "Assets/Zoo/Materials/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_Shallow", shallow); m.SetColor("_Deep", deep);
        m.SetFloat("_Scale", scale); m.SetFloat("_Speed", speed); m.SetFloat("_Strength", strength);
        m.SetFloat("_DepthRange", depthRange); m.SetFloat("_Foam", foam); m.SetFloat("_Opacity", opacity); m.SetFloat("_Glint", glint);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static Material LakeWater()
    {
        return WaterMat("water_lake", new Color(0.3f, 0.92f, 0.84f), new Color(0.03f, 0.38f, 0.84f), 0.15f, 0.45f, 0.2f, 5f, 0.6f, 0.9f, 1.2f);
    }

    static void EnvPrep()
    {
        RegisterBldTex();
        envModels.Clear(); envChunk.Clear();
        propSrc.Clear();
        utMesh = null;
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Env" });
        bool any = false;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var g in guids)
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as ModelImporter;
                if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); any = true; }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        if (any) AssetDatabase.Refresh();
    }

    static EnvModel Env(string id)
    {
        EnvModel m;
        if (envModels.TryGetValue(id, out m)) return m;
        if (id == "S1/lamp_post_classic" || id == "S1/lamp_post_modern") { m = LampKit(); envModels[id] = m; return m; }
        if (IsPropId(id)) { m = PropModel(id); if (m != null) { envModels[id] = m; return m; } }
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Env/" + id + ".fbx");
        MeshFilter mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null) { Debug.LogWarning("Env modeli yok: " + id); envModels[id] = null; return null; }
        var mr = mf.GetComponent<MeshRenderer>();
        var sm = mr != null ? mr.sharedMaterials : new Material[0];
        int n = mf.sharedMesh.subMeshCount;
        m = new EnvModel { mesh = mf.sharedMesh, b = mf.sharedMesh.bounds, mats = new string[n], cols = new Color[n] };
        var plain = new string[n];
        for (int i = 0; i < n; i++)
        {
            var s = i < sm.Length ? sm[i] : null;
            plain[i] = s != null ? s.name : "stone_gray";
            m.mats[i] = plain[i];
            m.cols[i] = (s != null && s.HasProperty("_BaseColor")) ? s.GetColor("_BaseColor") : Color.gray;
        }
        bool anyTex = false;
        for (int i = 0; i < n; i++) if (TexMap.ContainsKey("env_bld_" + plain[i])) anyTex = true;
        if (IsBld(id) && (anyTex || id == "S2/pier_dock_lake" || id == "A1/aquarium_building"))
        {
            m.mesh = UvMesh(mf.sharedMesh, plain, id);
            for (int i = 0; i < n; i++) if (TexMap.ContainsKey("env_bld_" + plain[i])) m.mats[i] = "bld_" + plain[i];
        }
        envModels[id] = m;
        return m;
    }

    static Material EMat(string name, Color c)
    {
        string key = "env_" + name;
        if (name.StartsWith("bld_")) name = name.Substring(4);
        if (name == "woodkit") return WoodKitMat();
        if (name.StartsWith("prop_")) return PropMat(name, c);
        if (name == "lampkit") return LampKitMat();
        if (name == "glass") return Mat("env_glass", c, 0.28f);
        if (name == "water")
            return WaterMat("env_water", new Color(0.3f, 0.92f, 0.84f), new Color(0.03f, 0.38f, 0.84f), 0.6f, 0.45f, 0.24f, 4f, 0.5f, 0.9f, 1.2f);
        if (name == "emissive_light") return Mat("env_emissive_light", c, 1f, 2.2f);
        var m = Mat(key, c);
        // otlar, yapraklar ve cicekler ince yuzeylerden olusur: iki tarafli ciz
        if (name.StartsWith("grass_") || name.StartsWith("leaf_") || name.StartsWith("flower_") || name == "accent_yellow" || name == "accent_red")
        { m.SetFloat("_Cull", 0f); EditorUtility.SetDirty(m); }
        return m;
    }

    // ---------------------------------------------------------------- birlestirilmis gruplar
    class EG
    {
        public Dictionary<string, MB> d = new Dictionary<string, MB>();
        public Dictionary<string, Color> col = new Dictionary<string, Color>();
        public void Add(EnvModel m, Vector3 pos, Quaternion rot, Vector3 sc)
        {
            if (m == null) return;
            var t = Matrix4x4.TRS(pos, rot, sc);
            for (int i = 0; i < m.mats.Length; i++)
            {
                MB mb;
                if (!d.TryGetValue(m.mats[i], out mb)) { mb = new MB(); d[m.mats[i]] = mb; col[m.mats[i]] = m.cols[i]; }
                mb.AddSub(m.mesh, i, t);
            }
        }
        public void Flush(Transform parent, string name)
        {
            foreach (var kv in d)
                MeshObj(name + "_" + kv.Key, parent, kv.Value.Build(name + "_" + kv.Key), EMat(kv.Key, col[kv.Key]), false);
        }
    }

    static readonly Dictionary<string, EG> envChunk = new Dictionary<string, EG>();

    // Dunya uzayinda, 100 m'lik parcalara birlestirilen (az draw call) dekor
    static void EDeco(string id, Vector3 pos, float yaw, float sc = 1f) { EDeco(id, pos, yaw, new Vector3(sc, sc, sc)); }
    static void EDeco(string id, Vector3 pos, float yaw, Vector3 sc)
    {
        var m = Env(id); if (m == null) return;
        string key = Mathf.FloorToInt(pos.x / 100f) + "_" + Mathf.FloorToInt(pos.z / 100f);
        EG g; if (!envChunk.TryGetValue(key, out g)) { g = new EG(); envChunk[key] = g; }
        g.Add(m, pos, Quaternion.Euler(0, yaw, 0), sc);
    }

    // Yuruyus carpismasi icin ince kapsul (agaclarla ayni sistem)
    static void ECol(Vector3 pos, float r)
    {
        string k = Mathf.FloorToInt(pos.x / 100f) + "_" + Mathf.FloorToInt(pos.z / 100f);
        List<Vector4> l; if (!treeCols.TryGetValue(k, out l)) { l = new List<Vector4>(); treeCols[k] = l; }
        l.Add(new Vector4(pos.x, pos.y, pos.z, r));
    }

    static void EFlush(Transform root)
    {
        var t = new GameObject("EnvDecor").transform; t.SetParent(root, false);
        foreach (var kv in envChunk) kv.Value.Flush(t, "Deco_" + kv.Key);
        envChunk.Clear();
    }

    // Tek, ayri nesne olarak yerlestirme (carpisma istege bagli)
    static GameObject EPlace(Transform parent, string id, Vector3 lp, Quaternion rot, Vector3 sc, bool col)
    {
        var m = Env(id); if (m == null) return null;
        var go = new GameObject(id.Substring(id.IndexOf('/') + 1));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localRotation = rot; go.transform.localScale = sc;
        go.AddComponent<MeshFilter>().sharedMesh = m.mesh;
        var arr = new Material[m.mats.Length];
        for (int i = 0; i < arr.Length; i++) arr[i] = EMat(m.mats[i], m.cols[i]);
        go.AddComponent<MeshRenderer>().sharedMaterials = arr;
        if (col) go.AddComponent<MeshCollider>().sharedMesh = m.mesh;
        return go;
    }
    static GameObject EPlace(Transform parent, string id, Vector3 lp, float yaw, float sc = 1f, bool col = false)
    {
        return EPlace(parent, id, lp, Quaternion.Euler(0, yaw, 0), new Vector3(sc, sc, sc), col);
    }

    static float YawOf(Vector3 dir) { return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }

    // ---------------------------------------------------------------- bina
    // Modelin on yuzu -Z'dedir; frontDir yonune baktirir.
    static GameObject EBuilding(Transform parent, string id, Vector3 pos, Vector3 frontDir, string label, float labelY, float labelW, float charSize = 0.1f)
    {
        var m = Env(id); if (m == null) return null;
        Quaternion q = Quaternion.LookRotation(-frontDir);
        var go = EPlace(parent, id, pos, q, Vector3.one, true);
        Vector3 e = q * m.b.size;
        rects.Add(new Rect(pos.x - Mathf.Abs(e.x) / 2f, pos.z - Mathf.Abs(e.z) / 2f, Mathf.Abs(e.x), Mathf.Abs(e.z)));
        if (!string.IsNullOrEmpty(label))
            Label(go.transform, label, new Vector3(0, labelY, m.b.min.z - 0.2f), 0f, charSize, labelW, Color.white);
        return go;
    }

    // ---------------------------------------------------------------- giris, cevre duvari, iskele
    static void EnvEntrance(Transform ent)
    {
        var arch = EPlace(ent, "B1/entrance_gate_arch", new Vector3(0, 0, -470f), 0f, 1f, true);
        Label(ent, "ZOO", new Vector3(0, 9.2f, -471.2f), 0f, 0.45f, 18f, Color.white);
        Label(ent, "ZOO", new Vector3(0, 9.2f, -468.8f), 180f, 0.45f, 18f, Color.white);

        for (int i = 0; i < 4; i++)
            EPlace(ent, "B1/entrance_turnstile", new Vector3((i < 2 ? -1 : 1) * (4.5f + (i % 2) * 3f), 0, -463f), 0f, 1f, false);
        EnvAnimalRow(ent);
        EPlace(ent, "S1/flagpole_banner", new Vector3(-34f, 0, -462f), 0f, 1f, false);
        EPlace(ent, "S1/flagpole_banner", new Vector3(34f, 0, -462f), 0f, 1f, false);
        for (int s = -1; s <= 1; s += 2)
        {
            EPlace(ent, "S1/map_board_stand", new Vector3(30f * s, 0, -420f), s > 0 ? 200f : 160f, 1f, false);
            if (!CleanSlate) EPlace(ent, "S1/planter_box_large", new Vector3(10f * s, 0, -420f), 0f, 1f, false);
            if (!CleanSlate) EPlace(ent, "N1/flower_bed_round", new Vector3(14f * s, 0, -448f), 0f, 1.4f, false);
        }
        // girisin yanlarindaki cimlere mor bitkiler
        float[][] pp = { new[] { 16f, -410f }, new[] { 27f, -416f }, new[] { 39f, -409f }, new[] { 22f, -400f }, new[] { 33f, -398f } };
        for (int i = 0; i < pp.Length; i++)
            for (int sg = -1; sg <= 1; sg += 2)
            { var q = new Vector3(pp[i][0] * sg, 0, pp[i][1]); q.y = GroundY(q.x, q.z); EDeco("P/purple", q, i * 70f + (sg > 0 ? 20f : 0f), 0.85f + 0.12f * i); }
    }

    static void EnvPerimeter(Transform p)
    {
        var wall = Env("S2/stone_wall_module");
        var pillar = Env("S2/stone_wall_corner_pillar");
        float L = ZOO * 2f;
        // kuzey / bati / dogu: tam; guney: giris acikligi (+-13) birakilir
        System.Action<Vector3, Vector3, float> run = (a, b, yaw) =>
        {
            float len = (b - a).magnitude; if (len < 1f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            float sx = len / n / 2.5f;
            Vector3 dir = (b - a) / len;
            for (int i = 0; i < n; i++)
                EDeco("S2/stone_wall_module", a + dir * (len * (i + 0.5f) / n), yaw, new Vector3(sx, 1f, 1f));
        };
        run(new Vector3(-ZOO, 0, ZOO), new Vector3(ZOO, 0, ZOO), 0f);
        run(new Vector3(-ZOO, 0, -ZOO), new Vector3(-ZOO, 0, ZOO), 90f);
        run(new Vector3(ZOO, 0, -ZOO), new Vector3(ZOO, 0, ZOO), 90f);
        run(new Vector3(-ZOO, 0, -ZOO), new Vector3(-13f, 0, -ZOO), 0f);
        run(new Vector3(13f, 0, -ZOO), new Vector3(ZOO, 0, -ZOO), 0f);
        foreach (var c in new[] { new Vector2(-ZOO, ZOO), new Vector2(ZOO, ZOO), new Vector2(-ZOO, -ZOO), new Vector2(ZOO, -ZOO) })
            EDeco("S2/stone_wall_corner_pillar", new Vector3(c.x, 0, c.y), 0f, 1f);
        // gorunmeyen carpisma kutulari
        var cg = new GameObject("PerimeterColliders"); cg.transform.SetParent(p, false);
        System.Action<Vector3, Vector3> box = (c, s) => { var b = cg.AddComponent<BoxCollider>(); b.center = c; b.size = s; };
        box(new Vector3(0, 1.6f, ZOO), new Vector3(L, 3.2f, 0.6f));
        box(new Vector3(-ZOO, 1.6f, 0), new Vector3(0.6f, 3.2f, L));
        box(new Vector3(ZOO, 1.6f, 0), new Vector3(0.6f, 3.2f, L));
        float part = (L - 26f) / 2f;
        box(new Vector3(-(13f + part / 2f), 1.6f, -ZOO), new Vector3(part, 3.2f, 0.6f));
        box(new Vector3(13f + part / 2f, 1.6f, -ZOO), new Vector3(part, 3.2f, 0.6f));
    }

    static void EnvPier(Transform lake)
    {
        var m = Env("S2/pier_dock_lake"); if (m == null) return;
        // kara tarafi ucu z=-123'te, goll icine +Z'ye dogru uzanir
        float z = -123f - m.b.min.z;
        EPlace(lake, "S2/pier_dock_lake", new Vector3(0, 0, z), 0f, 1f, true);
        EPlace(lake, "S2/small_boat_rowing", new Vector3(5.5f, -0.45f, -96f), 70f, 1f, false);
        EPlace(lake, "S2/small_boat_rowing", new Vector3(-24f, -0.45f, -60f), 15f, 1f, false);
    }

    // ---------------------------------------------------------------- park binalari ve mobilya
    static void EnvPark(Transform root)
    {
        var park = new GameObject("Park").transform; park.SetParent(root, false);
        if (!CleanSlate)
        {
        // dogu yuruyus yolu: kafe ve hediyelik dukkan karsilikli
        EBuilding(park, "B2/cafe_building", new Vector3(150f, 0, 18f), Vector3.back, "CAFE", 4.1f, 8f);
        EBuilding(park, "B2/souvenir_shop", new Vector3(150f, 0, -18f), Vector3.forward, "GIFT SHOP", 4.6f, 9f);
        // bati yuruyus yolu: bilgi merkezi ve tuvaletler
        EBuilding(park, "B2/information_center", new Vector3(-150f, 0, 16f), Vector3.back, "INFO", 3.9f, 6f);
        EBuilding(park, "B2/restroom_building", new Vector3(-150f, 0, -14f), Vector3.forward, "RESTROOMS", 2.7f, 3f);
        // kuzey yuruyus yolu: piknik kosku
        EPlace(park, "B2/picnic_pavilion", new Vector3(-16f, 0, 152f), 90f, 1f, false);
        rects.Add(new Rect(-24f, 144f, 16f, 16f));
        EPlace(park, "B2/ice_cream_kiosk", new Vector3(118f, 0, 8f), 270f, 1f, true);
        EPlace(park, "B2/ice_cream_kiosk", new Vector3(-8f, 0, 118f), 180f, 1f, true);
        EPlace(park, "B2/food_cart", new Vector3(120f, 0, -9f), 250f, 1f, true);
        }
        // akvaryum onu: balina heykeli ve cesme
        EPlace(park, "A1/aquarium_whale_sculpture", new Vector3(32f, 0, -187f), 0f, 1f, true);
        EPlace(park, "A1/aquarium_forecourt_fountain", new Vector3(-32f, 0, -187f), 0f, 1f, true);
        rects.Add(new Rect(26f, -192f, 12f, 10f)); rects.Add(new Rect(-38f, -192f, 12f, 10f));
        // yuruyus yollari uzerinde cicekli kemerler
        foreach (float d in new float[] { 0f, 90f, 180f })
        {
            float a = d * Mathf.Deg2Rad;
            Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (!CleanSlate) EPlace(park, "S2/flower_arch", u * 132f, YawOf(u), 1f, false).transform.localScale = new Vector3(1.8f, 1.2f, 1.2f);
        }
        // gol adasi
        EPlace(park, "N2/island_rock_group", new Vector3(30f, GroundY(30f, 12f), 12f), 20f, 1f, true);
    }

    static void EnvFurniture()
    {
        // fener direkleri: girisi, kollari ve halka yollari
        for (float z = -462f; z <= -426f; z += 12f)
            foreach (float s in new[] { -1f, 1f })
            { EDeco("S1/lamp_post_classic", new Vector3(s * 7f, 0, z), 0f); ECol(new Vector3(s * 7f, 0, z), 0.3f); }
        foreach (float d in SpokeDeg)
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), pp = new Vector2(-u.y, u.x);
            float end = d == 270f ? 420f : R2 - 14f;
            for (float r = R1 + 16f; r <= end; r += 36f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 p = u * r + pp * (s * 6.2f);
                    EDeco("S1/lamp_post_modern", new Vector3(p.x, 0, p.y), 0f);
                    ECol(new Vector3(p.x, 0, p.y), 0.3f);
                }
        }
        foreach (float d in new float[] { 0f, 90f, 180f })
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), pp = new Vector2(-u.y, u.x);
            for (float r = 130f; r <= 190f; r += 20f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 p = u * r + pp * (s * 3.6f);
                    EDeco("S1/lamp_post_classic", new Vector3(p.x, 0, p.y), 0f); ECol(new Vector3(p.x, 0, p.y), 0.3f);
                    Vector2 q = u * (r + 10f) + pp * (s * 3.9f);
                    EDeco("S1/bench_wood", new Vector3(q.x, 0, q.y), YawOf(new Vector3(-pp.x * s, 0, -pp.y * s)) + 180f);
                }
        }
        // gol yuruyus halkasi: banklar gole bakar, cop kutulari
        int nb = 22;
        for (int i = 0; i < nb; i++)
        {
            float a = (i + 0.5f) * 2f * Mathf.PI / nb;
            float deg = a * Mathf.Rad2Deg;
            bool near = false; foreach (float d in new float[] { 0f, 90f, 180f, 270f }) if (Mathf.Abs(Mathf.DeltaAngle(deg, d)) < 7f) near = true;
            if (near) continue;
            Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 pos = u * 119f;
            EDeco(i % 3 == 0 ? "S1/bench_curved" : "S1/bench_stone", pos, YawOf(u));
            EDeco("S1/trash_bin_green", u * 120.5f + Vector3.Cross(Vector3.up, u) * 2.6f, 0f);
            EDeco("S1/lamp_post_classic", u * 110f, 0f); ECol(u * 110f, 0.3f);
        }
        // yol kavsaklarinda yon tabelalari
        foreach (float d in SpokeDeg)
        {
            float a = (d + 4f) * Mathf.Deg2Rad;
            Vector3 u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            EDeco("S1/signpost_arrows", u * (R1 - 8f), d + 90f); ECol(u * (R1 - 8f), 0.3f);
        }
    }

    // ---------------------------------------------------------------- cimen, cicek, gol bitkileri
    static bool OnRoadTight(Vector2 p, float m)
    {
        float r = p.magnitude;
        if (Mathf.Abs(r - R1) < ROAD / 2f + m || Mathf.Abs(r - R2) < ROAD / 2f + m || r < 124f) return true;
        foreach (float d in SpokeDeg)
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float along = Vector2.Dot(p, u);
            float end = d == 270f ? 470f : 350f;
            if (along > 190f && along < end && Mathf.Abs(p.x * u.y - p.y * u.x) < 4.5f + m) return true;
        }
        foreach (float d in new float[] { 0f, 90f, 180f })
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float along = Vector2.Dot(p, u);
            if (along > 105f && along < 200f && Mathf.Abs(p.x * u.y - p.y * u.x) < 2.5f + m) return true;
        }
        if (p.y > -466f && p.y < -424f && Mathf.Abs(p.x) < 49f) return true;
        if (p.y > -194f && p.y < -182f && Mathf.Abs(p.x) < 16f + m) return true;
        return false;
    }

    static void EnvScatter()
    {
        string[] tuft = { "N1/grass_tuft_a", "N1/grass_tuft_b", "N1/grass_tuft_c" };
        string[] tall = { "N1/grass_tall_a", "N1/grass_tall_b" };
        string[] flo = { "N1/flower_daisy_patch", "N1/flower_tulip_red", "N1/flower_tulip_yellow", "N1/flower_lavender_patch", "N1/flower_pink_patch", "N1/flower_poppy_patch" };
        string[] bush = { "N1/bush_round_a", "N1/bush_round_b", "N1/bush_flat", "N1/shrub_flowering_pink", "N1/shrub_flowering_yellow", "N1/fern_a", "N1/fern_b" };
        for (float gx = -464f; gx <= 464f; gx += 3.4f)
            for (float gz = -464f; gz <= 464f; gz += 3.4f)
            {
                float x = gx + (Rnd() - 0.5f) * 3.2f, z = gz + (Rnd() - 0.5f) * 3.2f;
                var p = new Vector2(x, z);
                if (OnRoadTight(p, 1.2f) || InObb(p, 1.2f)) continue;
                float n = Mathf.PerlinNoise(x * 0.045f + 11f, z * 0.045f + 11f);
                float k = Mathf.PerlinNoise(x * 0.2f + 70f, z * 0.2f + 70f);
                float dens = Mathf.Lerp(0.35f, 1f, n);
                if (Rnd() > dens) continue;
                float y = GroundY(x, z);
                if (y < -0.2f) continue;
                Vector3 pos = new Vector3(x, y, z);
                if (!CleanSlate && k > 0.66f && Rnd() < 0.7f) EDeco(flo[(int)(k * 977f) % flo.Length], pos + new Vector3(Rnd() - 0.5f, 0, Rnd() - 0.5f) * 2f, Rnd() * 360f, 0.9f + Rnd() * 0.5f);
                if (!CleanSlate && k < 0.2f && Rnd() < 0.18f) EDeco(bush[(int)(Rnd() * bush.Length) % bush.Length], pos, Rnd() * 360f, 0.9f + Rnd() * 0.6f);
            }
        // yol kenarlarinda bosluk dolduran seritler: yol kollarinin iki yaninda alcak cit ve cicek
        if (!CleanSlate) foreach (float d in new float[] { 0f, 90f, 180f })
        {
            float a = d * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), pp = new Vector2(-u.y, u.x);
            for (float r = 134f; r < 186f; r += 2.5f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 c = u * r + pp * (s * 5.4f);
                    EDeco("N1/hedge_segment", new Vector3(c.x, 0, c.y), YawOf(new Vector3(u.x, 0, u.y)) + 90f);
                }
        }
        // gol kiyisi: saz, kamis, kayalar, nilufer
        for (int i = 0; i < 70; i++)
        {
            float a = Rnd() * Mathf.PI * 2f, r = 98f + Rnd() * 4f;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            if (Mathf.Abs(x) < 8f && z < -88f) continue;
            string id = i % 3 == 0 ? "N2/cattail_cluster" : (i % 3 == 1 ? "N2/reed_cluster_a" : "N2/reed_cluster_b");
            EDeco(id, new Vector3(x, GroundY(x, z), z), Rnd() * 360f, 0.9f + Rnd() * 0.5f);
        }
        string[] lr = { "N2/lake_rock_a", "N2/lake_rock_b", "N2/lake_rock_c", "N2/lake_rock_d", "N2/shore_stones_set" };
        for (int i = 0; i < 46; i++)
        {
            float a = Rnd() * Mathf.PI * 2f, r = 99f + Rnd() * 6f;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            if (Mathf.Abs(x) < 8f && z < -88f) continue;
            EDeco(lr[i % lr.Length], new Vector3(x, GroundY(x, z) - 0.1f, z), Rnd() * 360f, 0.8f + Rnd() * 0.9f);
        }
        for (int i = 0; i < 36; i++)
        {
            float a = Rnd() * Mathf.PI * 2f, r = 20f + Rnd() * 76f;
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            if (Mathf.Abs(x) < 6f && z < -86f) continue;
            if (Mathf.Sqrt((x - 30f) * (x - 30f) + (z - 12f) * (z - 12f)) < 22f) continue;
            EDeco(i % 2 == 0 ? "N2/lily_pad_set_a" : "N2/lily_pad_set_b", new Vector3(x, -0.44f, z), Rnd() * 360f, 1f + Rnd() * 0.5f);
        }
        // cevre ormaninda kaya ve devrik agaclar
        for (int i = 0; i < 90; i++)
        {
            float x = (Rnd() * 2f - 1f) * 590f, z = (Rnd() * 2f - 1f) * 590f;
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < 482f) continue;
            float h = GroundY(x, z);
            string id = i % 5 == 0 ? "N2/cliff_rock_a" : i % 5 == 1 ? "N2/cliff_rock_b" : i % 5 == 2 ? "N2/boulder_big_a" : i % 5 == 3 ? "N2/boulder_big_b" : "N2/fallen_tree";
            EDeco(id, new Vector3(x, h - 0.3f, z), Rnd() * 360f, 0.9f + Rnd() * 0.6f);
        }
    }

    // ---------------------------------------------------------------- kafes citi, kapi, tabela
    static void EnvFence(Transform root, Sp sp, float w, float d, float h)
    {
        bool aviary = sp.type == "aviary", wood = sp.style == "wood";
        string cls = sp.size == "XL" ? "XL" : sp.size == "L" ? "L" : sp.size == "M" ? "M" : "S";
        string pnl = aviary ? "K2/aviary_mesh_panel" : wood ? "K2/wood_panel" : "K1/bars_panel_" + cls;
        string pst = aviary ? "K2/aviary_post" : wood ? "K2/wood_post" : "K1/bars_post_" + cls;
        string leaf = aviary ? "K2/aviary_door_leaf" : wood ? "K2/wood_gate_leaf" : "K1/bars_gate_leaf_" + cls;
        float postH = aviary ? 7.3f : wood ? 1.5f : cls == "S" ? 2f : cls == "M" ? 2.7f : cls == "L" ? 3.6f : 4.6f;
        EnvModel P = Env(pnl), Po = Env(pst), Lf = Env(leaf), Bm = Env("K3/gate_sign_beam");
        bool kit = wood && !aviary;
        float pl = 2.5f;
        if (kit) { P = WoodKit(false); Po = WoodKit(true); pl = P.b.size.x; postH = Po.b.size.y; }
        var g = new EG();
        var posts = new HashSet<Vector2Int>();
        float gate = 6f;
        System.Action<Vector3, Vector3, Vector3> side = (a, b, nin) =>
        {
            Vector3 dv = b - a; float L = dv.magnitude; if (L < 0.1f) return;
            Vector3 dir = dv / L;
            int n = Mathf.Max(1, Mathf.RoundToInt(L / pl));
            float sx = L / n / pl;
            Quaternion q = Quaternion.LookRotation(nin);
            for (int i = 0; i < n; i++) g.Add(P, a + dir * (L * (i + 0.5f) / n), q, new Vector3(sx, 1f, 1f));
            for (int i = 0; i <= n; i++)
            {
                Vector3 pp = a + dir * (L * i / n);
                if (posts.Add(new Vector2Int(Mathf.RoundToInt(pp.x * 10f), Mathf.RoundToInt(pp.z * 10f)))) g.Add(Po, pp, Quaternion.identity, Vector3.one);
            }
        };
        side(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, -d / 2f), Vector3.forward);
        side(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(-w / 2f, 0, d / 2f), Vector3.right);
        side(new Vector3(w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f), Vector3.left);
        side(new Vector3(-w / 2f, 0, d / 2f), new Vector3(-gate / 2f, 0, d / 2f), Vector3.back);
        side(new Vector3(gate / 2f, 0, d / 2f), new Vector3(w / 2f, 0, d / 2f), Vector3.back);
        // kapi: yuksek direkler, dis tarafa acik kanatlar, uzerinde tabela
        float beamY = Mathf.Max(postH, 2.7f);
        float ps = beamY / postH;
        g.Add(Po, new Vector3(-gate / 2f, 0, d / 2f), Quaternion.identity, new Vector3(1f, ps, 1f));
        g.Add(Po, new Vector3(gate / 2f, 0, d / 2f), Quaternion.identity, new Vector3(1f, ps, 1f));
        if (kit)
        {
            // kapi kanatlari: dis tarafa dogru acik, yeni tahta panelden
            float ll = gate / 2f - 0.3f;
            g.Add(P, new Vector3(-gate / 2f, 0, d / 2f + ll / 2f), Quaternion.Euler(0, -90f, 0), new Vector3(ll / pl, 1f, 1f));
            g.Add(P, new Vector3(gate / 2f, 0, d / 2f + ll / 2f), Quaternion.Euler(0, -90f, 0), new Vector3(ll / pl, 1f, 1f));
            g.Add(Po, new Vector3(-gate / 2f, 0, d / 2f + ll), Quaternion.identity, Vector3.one);
            g.Add(Po, new Vector3(gate / 2f, 0, d / 2f + ll), Quaternion.identity, Vector3.one);
        }
        else
        {
            g.Add(Lf, new Vector3(-gate / 2f, 0, d / 2f), Quaternion.Euler(0, 90f, 0), Vector3.one);
            g.Add(Lf, new Vector3(gate / 2f, 0, d / 2f), Quaternion.Euler(0, 90f, 0), Vector3.one);
        }
        g.Add(Bm, new Vector3(0, beamY, d / 2f), Quaternion.Euler(0, 180f, 0), Vector3.one);
        if (aviary)
        {
            EnvModel R = Env("K2/aviary_roof_panel");
            int nx = Mathf.Max(1, Mathf.RoundToInt(w / 4f)), nz = Mathf.Max(1, Mathf.RoundToInt(d / 4f));
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                    g.Add(R, new Vector3(-w / 2f + (ix + 0.5f) * w / nx, 7f, -d / 2f + (iz + 0.5f) * d / nz), Quaternion.identity, new Vector3(w / nx / 2.6f, 1f, d / nz / 2.6f));
        }
        g.Flush(root, "Fence");
        Label(root, Pretty(sp.id), new Vector3(0, beamY + 0.82f, d / 2f + 0.26f), 180f, 0.075f, 5.1f, Color.white);
        // gorunmeyen carpisma kutulari
        var cg = new GameObject("FenceColliders"); cg.transform.SetParent(root, false);
        float hh = aviary ? 7f : h;
        System.Action<Vector3, Vector3> box = (c, s) => { var b = cg.AddComponent<BoxCollider>(); b.center = c; b.size = s; };
        box(new Vector3(0, hh / 2f, -d / 2f), new Vector3(w, hh, 0.4f));
        box(new Vector3(-w / 2f, hh / 2f, 0), new Vector3(0.4f, hh, d));
        box(new Vector3(w / 2f, hh / 2f, 0), new Vector3(0.4f, hh, d));
        float fw = (w - gate) / 2f;
        box(new Vector3(-(gate / 2f + fw / 2f), hh / 2f, d / 2f), new Vector3(fw, hh, 0.4f));
        box(new Vector3(gate / 2f + fw / 2f, hh / 2f, d / 2f), new Vector3(fw, hh, 0.4f));
    }

    // ---------------------------------------------------------------- kafes ici dekor
    static readonly HashSet<string> thSnow = new HashSet<string> { "polar_bear", "arctic_fox", "snowy_owl", "reindeer", "muskox", "snow_leopard", "wolverine", "alpine_ibex", "yak", "moose" };
    static readonly HashSet<string> thDesert = new HashSet<string> { "bactrian_camel", "fennec_fox", "meerkat", "gemsbok", "ostrich", "dingo", "emu", "greater_rhea" };
    static readonly HashSet<string> thSavanna = new HashSet<string> { "elephant", "giraffe", "zebra", "white_rhinoceros", "african_buffalo", "blue_wildebeest", "warthog", "impala", "cheetah", "lion", "spotted_hyena", "greater_kudu", "common_eland", "thomsons_gazelle", "okapi", "secretary_bird" };
    static readonly HashSet<string> thJungle = new HashSet<string> { "gorilla", "orangutan", "chimpanzee", "olive_baboon", "mandrill", "spider_monkey", "tufted_capuchin", "ring_tailed_lemur", "jaguar", "ocelot", "malayan_tapir", "sun_bear", "binturong", "komodo_dragon", "scarlet_macaw", "toco_toucan", "great_hornbill", "indian_peafowl", "white_nosed_coati", "indian_rhinoceros", "tiger" };
    static readonly HashSet<string> thBamboo = new HashSet<string> { "giant_panda", "red_panda" };
    static readonly HashSet<string> thPrimate = new HashSet<string> { "gorilla", "orangutan", "chimpanzee", "olive_baboon", "mandrill", "spider_monkey", "tufted_capuchin", "ring_tailed_lemur" };
    static readonly HashSet<string> thMud = new HashSet<string> { "elephant", "white_rhinoceros", "indian_rhinoceros", "warthog", "wild_boar", "domestic_pig", "malayan_tapir", "african_buffalo", "american_bison", "european_bison" };
    static readonly HashSet<string> thWater = new HashSet<string> { "flamingo", "great_white_pelican", "mute_swan", "grey_heron", "mallard", "white_stork", "grey_crowned_crane", "shoebill", "domestic_goose" };
    static readonly HashSet<string> thNest = new HashSet<string> { "bald_eagle", "golden_eagle", "griffon_vulture", "white_stork", "secretary_bird", "eurasian_eagle_owl", "snowy_owl", "great_hornbill" };
    static readonly HashSet<string> thCat = new HashSet<string> { "lion", "tiger", "leopard", "jaguar", "puma", "cheetah", "snow_leopard", "brown_bear", "american_black_bear", "polar_bear", "sun_bear", "gray_wolf", "eurasian_lynx", "spotted_hyena", "serval", "caracal", "ocelot", "wolverine", "tasmanian_devil" };

    static void EnvInterior(Transform root, Sp sp, float w, float d)
    {
        int cls = sp.size == "XL" ? 3 : sp.size == "L" ? 2 : sp.size == "M" ? 1 : 0;
        bool aviary = sp.type == "aviary", wood = sp.style == "wood";
        string id = sp.id;
        System.Func<float, float, Vector3> spot = (fx, fz) => new Vector3(fx * (w / 2f - 4.5f), 0.2f, Mathf.Lerp(-d / 2f + 3.2f, d / 2f - 6.5f, (fz + 1f) / 2f));
        System.Action<string, float, float, float, float, bool> put = (m, fx, fz, yaw, sc, col) => { if (CleanSlate && !m.StartsWith("D1/rock_")) return; EPlace(root, m, spot(fx, fz), yaw, sc, col); };
        float rs = 0.8f + 0.12f * cls;

        if (aviary)
        {
            put("D2/bird_perch_tall", -0.6f, -0.3f, Rnd() * 360f, 1f, false);
            put("D2/bird_perch_tall", 0.6f, 0.2f, Rnd() * 360f, 1f, false);
            if (thNest.Contains(id)) put("D2/nest_platform", 0f, -0.7f, 0f, 1f, false);
            if (thWater.Contains(id)) { put("D2/pond_round_small", -0.3f, 0.1f, 0f, cls == 0 ? 0.8f : 1.1f, false); put("N2/reed_cluster_a", 0.05f, 0.1f, 40f, 1f, false); put("N2/reed_cluster_b", -0.7f, 0.3f, 10f, 1f, false); }
            else put("D1/rock_M_a", 0.7f, -0.7f, Rnd() * 360f, 0.8f, false);
            put("N1/bush_round_a", -0.9f, -0.9f, 0f, 1.4f, false);
            put("N1/bush_round_b", 0.9f, -0.9f, 0f, 1.4f, false);
            return;
        }
        // barinak
        string hut = wood ? (cls == 0 ? "D2/shelter_hut_S" : "D2/shelter_hut_M") : (cls <= 1 ? (cls == 0 ? "D2/shelter_hut_S" : "D2/shelter_hut_M") : "D2/shelter_hut_L");
        if (thCat.Contains(id) && cls >= 2 && !wood) put("D1/rock_cave", 0.55f, -1f, 180f, 1f, true);
        else put(hut, 0.45f, -1f, 180f, 1f, true);
        // su/yem
        if (wood)
        {
            put("D2/feed_trough_wood", -0.55f, -0.85f, 0f, 1f, false);
            put("D2/hay_rack", -0.2f, -1f, 180f, 1f, false);
            put("D2/hay_bale_round", 0.8f, 0.0f, 20f, 1f, true);
            put("D2/hay_bale_square", 0.75f, 0.35f, 70f, 1f, false);
            put("D2/water_trough_stone", -0.75f, 0.1f, 90f, 1f, false);
            if (!CleanSlate) { var gp = spot(0.0f, 0.2f); GrassPlace(root, gp, 0f, 1.8f, false); GrassPlace(root, spot(-0.4f, 0.5f), 80f, 1.4f, false); GrassPlace(root, spot(0.4f, -0.3f), 200f, 1.5f, false); }
            return;
        }
        put("D2/water_trough_stone", -0.5f, -0.9f, 0f, 1f, false);
        if (cls >= 1 || thWater.Contains(id)) put(cls >= 2 ? "D2/pond_round_large" : "D2/pond_round_small", -0.6f, 0.1f, 0f, cls >= 2 ? 0.9f : 1f, false);
        // kayalar
        string[] rk = cls == 0 ? new[] { "D1/rock_S_a", "D1/rock_S_b" } : cls == 1 ? new[] { "D1/rock_M_a", "D1/rock_S_c", "D1/rock_M_b" } : new[] { "D1/rock_L_a", "D1/rock_M_c", "D1/rock_L_b", "D1/rock_M_a" };
        float[][] rsp = { new[] { 0.8f, -0.3f }, new[] { 0.25f, 0.45f }, new[] { -0.2f, -0.05f }, new[] { 0.9f, 0.6f } };
        for (int i = 0; i < Mathf.Min(rk.Length, rsp.Length); i++)
            put(rk[i], rsp[i][0], rsp[i][1], Rnd() * 360f, 1f, rk[i].Contains("_M_") || rk[i].Contains("_L_"));
        // tema
        if (thSnow.Contains(id)) { put("D3/snow_mound", 0.2f, 0.2f, 0f, 1f, false); put("D3/snow_mound", -0.4f, 0.6f, 90f, 0.8f, false); if (id == "polar_bear" || id == "arctic_fox") put("D3/ice_block_cluster", 0.65f, 0.1f, 30f, 1f, true); }
        else if (thDesert.Contains(id)) { put("D3/sand_dune_a", 0.1f, 0.3f, 0f, 1f, false); put("D3/cactus_tall", -0.8f, -0.2f, 0f, 1f, true); put("D3/cactus_group", 0.5f, 0.5f, 0f, 1f, false); }
        else if (thBamboo.Contains(id)) { put("D3/bamboo_clump", -0.85f, -0.6f, 0f, 1f, false); put("D3/bamboo_clump", 0.9f, -0.2f, 90f, 1f, false); put("D3/mossy_log", 0.1f, 0.3f, 20f, 1f, false); }
        else if (thJungle.Contains(id)) { put("D3/jungle_big_leaves", -0.9f, -0.3f, 0f, 1f, false); put("D3/jungle_big_leaves", 0.9f, 0.1f, 80f, 1f, false); put("D3/mossy_log", 0.0f, 0.4f, 70f, 1f, false); }
        else if (thSavanna.Contains(id)) { put("D3/termite_mound", 0.1f, 0.4f, 0f, 1f, true); if (!CleanSlate) { GrassPlace(root, spot(-0.2f, 0.65f), 0f, 2f, true); GrassPlace(root, spot(0.6f, 0.7f), 120f, 2f, true); GrassPlace(root, spot(-0.7f, 0.2f), 40f, 1.7f, true); GrassPlace(root, spot(0.2f, 0.9f), 250f, 1.8f, true); } }
        else { put("D1/log_big", 0.0f, 0.5f, Rnd() * 360f, 1f, false); put("N1/bush_round_a", -0.85f, 0.55f, 0f, 1.3f, false); }
        if (thPrimate.Contains(id) && cls >= 1) { put("D2/climbing_tower_primate", -0.2f, 0.25f, 0f, 1f, true); put("D2/rope_swing_frame", 0.5f, 0.45f, 0f, 1f, false); }
        if (thMud.Contains(id) && cls >= 1) put("D2/mud_wallow", 0.1f, 0.05f, 0f, cls == 1 ? 0.8f : 1f, false);
        if (thCat.Contains(id) && !wood) put("D2/scratching_post", -0.9f, -0.5f, 0f, 1f, false);
    }

    // ---------------------------------------------------------------- akvaryum / surungen evi kabugu ve tanklar
    static float hallZN, hallZS; static string hallTankId; static float hallLabelY;
    static void EnvShell(Transform hall, string name, string title, float doorW)
    {
        bool aq = name == "Aquarium";
        string shell = aq ? "A1/aquarium_building" : "A1/reptile_house_building";
        hallZN = aq ? 26.7f : 9.6f; hallZS = aq ? -14.6f : -8.5f;
        hallTankId = aq ? "A2/tank_S" : "A4/terrarium_unit";
        var m = Env(shell);
        EPlace(hall, shell, Vector3.zero, 0f, 1f, true);
        if (aq) Label(hall, title, new Vector3(0, 8.4f, m.b.min.z - 0.2f), 0f, 0.2f, 12f, Color.white);
        else Label(hall, title, new Vector3(0, 7.2f, m.b.min.z - 0.2f), 0f, 0.12f, 7f, Color.white);
    }

    static void EnvTank(Transform t, Sp sp, int s, float slot)
    {
        var m = Env(hallTankId); if (m == null) return;
        float front = s > 0 ? hallZN - m.b.size.z : hallZS + m.b.size.z;     // on yuzun z'si
        float zpos = s > 0 ? hallZN - m.b.max.z : hallZS + m.b.max.z;
        float sx = (slot - 0.2f) / m.b.size.x;
        var go = EPlace(t, hallTankId, new Vector3(0, 0.3f, zpos), s > 0 ? 0f : 180f, 1f, false);
        go.transform.localScale = new Vector3(sx, 1f, 1f);
        var bc = go.AddComponent<BoxCollider>();
        bc.center = m.b.center; bc.size = m.b.size;
        EnvTankFront = front;
    }
    static float EnvTankFront;

    // ---------------------------------------------------------------- agaclar: yumusak tepe golgesi, yaprak dokusu, kabuk UV'si, ruzgar
    static void TreeReadable(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
    }

    static Texture2D LeafTex()
    {
        const string p = "Assets/Zoo/Stylized/leaf_cluster.png";
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        if (t != null) return t;
        EnsureFolder("Assets/Zoo/Stylized");
        int S = 256;
        var px = new Color32[S * S];
        var r = new System.Random(7);
        for (int k = 0; k < 120; k++)
        {
            float cx = (float)r.NextDouble() * S, cy = (float)r.NextDouble() * S;
            float len = 20f + (float)r.NextDouble() * 16f, wid = len * (0.36f + (float)r.NextDouble() * 0.14f);
            float ang = (float)r.NextDouble() * Mathf.PI * 2f, tone = 0.62f + (float)r.NextDouble() * 0.38f;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            int rad = (int)len + 2;
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    float rx = dx * ca + dy * sa, ry = -dx * sa + dy * ca;
                    float e = (rx / len) * (rx / len) + (ry / wid) * (ry / wid);
                    if (e >= 1f) continue;
                    float tip = rx / len * 0.5f + 0.5f;
                    float v = tone * (0.78f + 0.32f * tip) * (Mathf.Abs(ry) < 0.9f ? 0.82f : 1f);
                    byte bt = (byte)(Mathf.Clamp01(v) * 255f);
                    int x = ((int)cx + dx + S * 4) % S, y = ((int)cy + dy + S * 4) % S;
                    px[y * S + x] = new Color32(bt, bt, bt, 255);
                }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Zoo/Stylized/leaf_cluster.png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(p);
        var imp = AssetImporter.GetAtPath(p) as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f;
            imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static Material FoliageMat(string name, Color col)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        var sh = Shader.Find("Zoo/Foliage");
        if (sh == null) { Debug.LogWarning("Zoo/Foliage shader bulunamadi"); return Mat(name, col); }
        string path = "Assets/Zoo/Materials/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", LeafTex());
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Sway", 1f); m.SetFloat("_Variation", 0.35f);
        m.renderQueue = 2450;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static Mesh TreeMesh(Mesh src, int trunkSub, int folSub, string id)
    {
        var P = src.vertices; var N0 = src.normals;
        int nvt = P.Length;
        var isFol = new bool[nvt];
        foreach (var vi in src.GetTriangles(folSub)) isFol[vi] = true;
        Bounds fb = new Bounds(); bool any = false;
        for (int i = 0; i < nvt; i++) if (isFol[i]) { if (!any) { fb = new Bounds(P[i], Vector3.zero); any = true; } else fb.Encapsulate(P[i]); }
        Bounds all = src.bounds;
        // yaklasik pozisyon-kaynakli ortalama normal (yuzey yumusatma)
        var avg = new Dictionary<int, Vector3>();
        for (int i = 0; i < nvt; i++) { int k = QKey(P[i]); Vector3 a; avg[k] = avg.TryGetValue(k, out a) ? a + N0[i] : N0[i]; }
        var N = new Vector3[nvt]; var UV = new Vector2[nvt]; var C = new Color[nvt];
        float tile = Mathf.Max(0.5f, all.size.y * 0.3f);
        for (int i = 0; i < nvt; i++)
        {
            Vector3 n = N0[i];
            if (isFol[i])
            {
                Vector3 sm = avg[QKey(P[i])].normalized;
                Vector3 rad = (P[i] - fb.center).normalized;
                n = Vector3.Slerp(sm, rad, 0.55f).normalized;
                float ty = Mathf.Clamp01((P[i].y - fb.min.y) / Mathf.Max(0.01f, fb.size.y));
                float g = Mathf.Lerp(0.62f, 1.12f, Mathf.SmoothStep(0f, 1f, ty));
                C[i] = new Color(g, g, g * 0.97f, ty * ty);
            }
            else C[i] = new Color(1, 1, 1, 0);
            N[i] = n;
            Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            Vector2 uv;
            if (an.y >= an.x && an.y >= an.z) uv = new Vector2(P[i].x, P[i].z);
            else if (an.x >= an.z) uv = new Vector2(P[i].z, P[i].y);
            else uv = new Vector2(P[i].x, P[i].y);
            UV[i] = uv / tile;
        }
        var m = new Mesh { name = id + "_tree", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.vertices = P; m.normals = N; m.uv = UV; m.colors = C;
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) m.SetTriangles(src.GetTriangles(s), s);
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    // Kullanicinin gonderdigi hayvan modelleri: baslangic meydaninda sira halinde sergilenir
    static void EnvAnimalRow(Transform parent)
    {
        string[] names = { "penguin", "flamingo", "gorilla", "brown_bear", "lion", "tiger", "zebra", "giraffe", "elephant", "crocodile" };
        var row = new GameObject("StartAnimals").transform;
        row.SetParent(parent, false);
        float spacing = 6f, x0 = -(names.Length - 1) * spacing * 0.5f;
        for (int i = 0; i < names.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animals/" + names[i] + ".fbx");
            if (prefab == null) { Debug.LogWarning("Hayvan modeli yok: " + names[i]); continue; }
            var go = (GameObject)Object.Instantiate(prefab);
            go.name = names[i];
            go.transform.SetParent(row, false);
            float x = x0 + i * spacing, z = -418f;
            go.transform.position = new Vector3(x, GroundY(x, z), z);
            go.transform.rotation = Quaternion.Euler(0, 180f, 0);
            Label(row, Pretty(names[i]), new Vector3(x, GroundY(x, z) + 0.2f, z - 3.2f), 0f, 0.06f, 4.5f, Color.white);
        }
    }

    // ---------------------------------------------------------------- kullanicinin tahta cit seti (Assets/Env/Wood/low.fbx)
    static Material WoodKitMat()
    {
        Material m;
        if (mats.TryGetValue("env_woodkit", out m) && m != null) return m;
        string path = "Assets/Zoo/Materials/env_woodkit.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var nimp = AssetImporter.GetAtPath("Assets/Env/Wood/wood_nor.png") as TextureImporter;
        if (nimp != null && nimp.textureType != TextureImporterType.NormalMap) { nimp.textureType = TextureImporterType.NormalMap; nimp.SaveAndReimport(); }
        var bimp = AssetImporter.GetAtPath("Assets/Env/Wood/wood_base.png") as TextureImporter;
        if (bimp != null && bimp.anisoLevel < 8) { bimp.anisoLevel = 8; bimp.SaveAndReimport(); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Env/Wood/wood_base.png"));
        var nm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Env/Wood/wood_nor.png");
        if (nm != null) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
        m.SetColor("_BaseColor", new Color(1.35f, 1.2f, 1.05f, 1f));
        m.SetFloat("_Smoothness", 0.15f); m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        mats["env_woodkit"] = m;
        return m;
    }

    // low.fbx: iki direk + uc yatay tahta + civi. Tahta (post=false) ve direk (post=true) ayri model olarak,
    // uzunluk X ekseninde, dis yuzu -Z'de, yerde (y=0) olacak sekilde hazirlanir.
    static EnvModel WoodKit(bool post)
    {
        string key = "WOOD/" + (post ? "post" : "panel");
        EnvModel em;
        if (envModels.TryGetValue(key, out em)) return em;
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Env/Wood/low.fbx");
        if (go == null) { envModels[key] = null; return null; }
        const float S = 1.3f;
        Matrix4x4 w2r = go.transform.worldToLocalMatrix;
        System.Func<Vector3, Vector3> rot = v => new Vector3(-v.z, v.y, v.x);
        // referans: direk1'in z merkezi, tum parcalarin en alt y'si
        float ymin = float.MaxValue, zc = 0f;
        var parts = new List<KeyValuePair<MeshFilter, Matrix4x4>>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.name;
            bool isBeam = n.StartsWith("beam"), isPlank = n.StartsWith("plank");
            if (post ? n != "beam1_low" : !isPlank) { if (isBeam) { } else continue; }
            Matrix4x4 m = w2r * mf.transform.localToWorldMatrix;
            if (isBeam) { var b = mf.sharedMesh.bounds; var vs = new[] { b.min, b.max }; foreach (var v in vs) ymin = Mathf.Min(ymin, m.MultiplyPoint3x4(v).y); }
            if (post ? n == "beam1_low" : isPlank) parts.Add(new KeyValuePair<MeshFilter, Matrix4x4>(mf, m));
        }
        if (parts.Count == 0) { envModels[key] = null; return null; }
        Bounds zb = new Bounds(); bool f = true;
        foreach (var kv in parts) foreach (var v in kv.Key.sharedMesh.vertices) { var p = kv.Value.MultiplyPoint3x4(v); if (f) { zb = new Bounds(p, Vector3.zero); f = false; } else zb.Encapsulate(p); }
        zc = zb.center.z;
        var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var T = new List<int>();
        foreach (var kv in parts)
        {
            var sm = kv.Key.sharedMesh; var m = kv.Value;
            int o = V.Count;
            var vv = sm.vertices; var nn = sm.normals; var uu = sm.uv;
            for (int i = 0; i < vv.Length; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(vv[i]);
                p = new Vector3(p.x, p.y - ymin, p.z - zc);
                V.Add(rot(new Vector3(p.x, p.y, p.z)) * S);
                N.Add(rot(m.MultiplyVector(nn[i])).normalized);
                UV.Add(uu.Length > i ? uu[i] : Vector2.zero);
            }
            foreach (var t in sm.triangles) T.Add(o + t);
        }
        var mesh = new Mesh { name = key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV); mesh.SetTriangles(T, 0);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        em = new EnvModel { mesh = mesh, b = mesh.bounds, mats = new[] { "woodkit" }, cols = new[] { Color.white } };
        envModels[key] = em;
        return em;
    }

    // ---------------------------------------------------------------- sokak lambasi (Cyrus_JS, "Modern Chinese Street Lamp", CC-BY 4.0)
    static Material LampKitMat()
    {
        Material m;
        if (mats.TryGetValue("env_lampkit", out m) && m != null) return m;
        string path = "Assets/Zoo/Materials/env_lampkit.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        var nimp = AssetImporter.GetAtPath("Assets/Env/Lamp/lamp_nor.png") as TextureImporter;
        if (nimp != null && nimp.textureType != TextureImporterType.NormalMap) { nimp.textureType = TextureImporterType.NormalMap; nimp.SaveAndReimport(); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Env/Lamp/lamp_base.png"));
        var nm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Env/Lamp/lamp_nor.png");
        if (nm != null) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
        var em = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Env/Lamp/lamp_emis.png");
        if (em != null) { m.SetTexture("_EmissionMap", em); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(2f, 1.7f, 1.1f)); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.4f); m.SetFloat("_Metallic", 0.1f); m.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(m);
        mats["env_lampkit"] = m;
        return m;
    }

    static EnvModel LampKit()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Env/Lamp/lamp.obj");
        MeshFilter mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null) return null;
        const float S = 1.4f;
        var src = mf.sharedMesh;
        var V = src.vertices; var N = src.normals;
        float ymin = float.MaxValue;
        foreach (var v in V) ymin = Mathf.Min(ymin, v.y);
        for (int i = 0; i < V.Length; i++) V[i] = new Vector3(V[i].x, V[i].y - ymin, V[i].z) * S;
        var mesh = new Mesh { name = "lamp_kit", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = V; mesh.normals = N; mesh.uv = src.uv;
        mesh.subMeshCount = 1;
        mesh.SetTriangles(src.triangles, 0);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return new EnvModel { mesh = mesh, b = mesh.bounds, mats = new[] { "lampkit" }, cols = new[] { Color.white } };
    }
}
