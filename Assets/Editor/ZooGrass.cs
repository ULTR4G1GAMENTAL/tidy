using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Kullanicinin verdigi Grass.fbx (rozet + dikey bicak kartlari) ile cim sistemi.
// Haritadaki tum cim tutamlari bu modelden uretilir; 50 m'lik parcalara birlestirilir.
public static partial class ZooBuilder
{
    const string GrassDir = "Assets/Env/Grass/";
    const float GrassS = 9f;   // model ~0.1 m'lik parcalar: oyun olcegine buyut

    class GPiece { public Vector3[] v; public Vector2[] uv; public float[] w; public int[] t; public int mat; }
    static List<GPiece> gRos, gCardSm, gCardBig;
    static readonly Dictionary<string, GB> grassChunks = new Dictionary<string, GB>();
    static Mesh grassHero, grassHeroTall;

    class GB
    {
        public List<Vector3> v = new List<Vector3>(); public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>(); public List<Color32> c = new List<Color32>(); public List<int> t = new List<int>();
    }

    static Texture2D GrassTex(string file)
    {
        string p = GrassDir + file;
        var imp = AssetImporter.GetAtPath(p) as TextureImporter;
        if (imp != null && (!imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp || imp.maxTextureSize != 512))
        {
            imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.4f;
            imp.wrapMode = TextureWrapMode.Clamp; imp.anisoLevel = 4; imp.maxTextureSize = 512;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static Material GrassMat(int which)
    {
        string name = which == 0 ? "grass_rosette" : "grass_blades";
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        var sh = Shader.Find("Zoo/Foliage");
        string path = "Assets/Zoo/Materials/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", GrassTex(which == 0 ? "Grassblade_Texture.png" : "Grassblade_Texture_02.png"));
        m.SetColor("_BaseColor", which == 0 ? new Color(1.25f, 1.35f, 0.8f, 1f) : new Color(1.45f, 1.6f, 0.85f, 1f));
        m.SetFloat("_Cutoff", 0.4f); m.SetFloat("_Sway", 1.6f); m.SetFloat("_Variation", 0.5f);
        m.renderQueue = 2450;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static GPiece GrassPiece(GameObject root, string node, int mat, Vector3 centre, float ymin)
    {
        var tr = root.transform.Find(node);
        if (tr == null) return null;
        var mf = tr.GetComponent<MeshFilter>(); if (mf == null) return null;
        var M = root.transform.worldToLocalMatrix * tr.localToWorldMatrix;
        var src = mf.sharedMesh;
        var sv = src.vertices; var suv = src.uv;
        var p = new GPiece { v = new Vector3[sv.Length], uv = suv, t = src.triangles, mat = mat, w = new float[sv.Length] };
        for (int i = 0; i < sv.Length; i++)
        {
            var q = M.MultiplyPoint3x4(sv[i]);
            p.v[i] = new Vector3(q.x - centre.x, q.y - ymin, q.z - centre.z);
        }
        return p;
    }

    static void GrassWeights(List<GPiece> list, bool radial)
    {
        float maxY = 0.0001f, maxR = 0.0001f;
        foreach (var p in list) foreach (var v in p.v) { maxY = Mathf.Max(maxY, v.y); maxR = Mathf.Max(maxR, new Vector2(v.x, v.z).magnitude); }
        foreach (var p in list)
            for (int i = 0; i < p.v.Length; i++)
                p.w[i] = radial ? Mathf.Clamp01(new Vector2(p.v[i].x, p.v[i].z).magnitude / maxR) * 0.8f : Mathf.Clamp01(p.v[i].y / maxY);
    }

    static void GrassPrep()
    {
        grassChunks.Clear(); grassHero = null; grassHeroTall = null;
        string fbx = GrassDir + "Grass.fbx";
        var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (imp == null) { Debug.LogWarning("Grass.fbx yok"); gRos = null; return; }
        if (!imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var root = (GameObject)Object.Instantiate(prefab);
        try
        {
            // rozet: pPlane1 (egri, 121 kose) ortalanmis
            Bounds rb = root.transform.Find("pPlane1").GetComponent<Renderer>().bounds;
            gRos = new List<GPiece>();
            var pr = GrassPiece(root, "pPlane1", 0, rb.center, rb.min.y); if (pr != null) gRos.Add(pr);
            GrassWeights(gRos, true);
            // dikey bicak kartlari (pPlane16-23)
            Bounds cb = new Bounds(); bool f = true;
            for (int i = 16; i <= 23; i++)
            {
                var r = root.transform.Find("pPlane" + i).GetComponent<Renderer>();
                if (f) { cb = r.bounds; f = false; } else cb.Encapsulate(r.bounds);
            }
            gCardSm = new List<GPiece>(); gCardBig = new List<GPiece>();
            for (int i = 16; i <= 23; i++)
            {
                var a = GrassPiece(root, "pPlane" + i, 1, cb.center, cb.min.y);
                if (a == null) continue;
                gCardBig.Add(a);
                if (i <= 19) gCardSm.Add(a);
            }
            GrassWeights(gCardSm, false); GrassWeights(gCardBig, false);
        }
        finally { Object.DestroyImmediate(root); }
        grassHero = GrassHeroMesh(1f, "GrassHero");
        grassHeroTall = GrassHeroMesh(1.8f, "GrassHeroTall");
    }

    static void GrassEmit(GB g, List<GPiece> list, int mat, Matrix4x4 M, Color32 col)
    {
        foreach (var p in list)
        {
            if (p.mat != mat) continue;
            int b = g.v.Count;
            for (int i = 0; i < p.v.Length; i++)
            {
                g.v.Add(M.MultiplyPoint3x4(p.v[i])); g.n.Add(Vector3.up); g.uv.Add(p.uv[i]);
                g.c.Add(new Color32(col.r, col.g, col.b, (byte)(p.w[i] * 255f)));
            }
            for (int i = 0; i < p.t.Length; i++) g.t.Add(b + p.t[i]);
        }
    }

    static Mesh BuildGrassMesh(GB g0, GB g1, string name)
    {
        var m = new Mesh { name = name };
        var all = new GB();
        int[] off = new int[2];
        var gs = new[] { g0, g1 };
        foreach (var g in gs) { }
        m.indexFormat = (g0.v.Count + g1.v.Count) > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        all.v.AddRange(g0.v); all.v.AddRange(g1.v); all.n.AddRange(g0.n); all.n.AddRange(g1.n);
        all.uv.AddRange(g0.uv); all.uv.AddRange(g1.uv); all.c.AddRange(g0.c); all.c.AddRange(g1.c);
        m.SetVertices(all.v); m.SetNormals(all.n); m.SetUVs(0, all.uv); m.SetColors(all.c);
        m.subMeshCount = 2;
        m.SetTriangles(g0.t, 0);
        var t1 = new List<int>(g1.t.Count); int d = g0.v.Count; foreach (int i in g1.t) t1.Add(i + d);
        m.SetTriangles(t1, 1);
        m.RecalculateBounds();
        return m;
    }

    // Ayri nesne olarak (kafes ici) yerlestirilen tam tutam: rozet + buyuk kart demeti
    static Mesh GrassHeroMesh(float tall, string name)
    {
        var g0 = new GB(); var g1 = new GB();
        var white = new Color32(255, 255, 255, 255);
        var M = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(GrassS, GrassS * tall, GrassS));
        GrassEmit(g0, gRos, 0, M, white);
        GrassEmit(g1, gCardBig, 1, M, white);
        return BuildGrassMesh(g0, g1, name);
    }

    static GameObject GrassPlace(Transform parent, Vector3 lp, float yaw, float s, bool tall)
    {
        if (grassHero == null) return null;
        var go = new GameObject("GrassTuft");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = Vector3.one * s;
        go.AddComponent<MeshFilter>().sharedMesh = tall ? grassHeroTall : grassHero;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { GrassMat(0), GrassMat(1) };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // Dunya uzayinda toplu cim: kind 0 = kucuk kartlar, 1 = buyuk kartlar, 2 = rozet+kartlar
    static void GrassAdd(Vector3 pos, int kind, float yaw, float s, float h)
    {
        if (gRos == null) return;
        string key = Mathf.FloorToInt(pos.x / 50f) + "_" + Mathf.FloorToInt(pos.z / 50f);
        GB g;
        if (!grassChunks.TryGetValue(key + "|0", out g)) { g = new GB(); grassChunks[key + "|0"] = g; }
        GB g1;
        if (!grassChunks.TryGetValue(key + "|1", out g1)) { g1 = new GB(); grassChunks[key + "|1"] = g1; }
        // renk degisimi: acik/koyu, sari-yesil/mavi-yesil
        float j = 0.78f + Rnd() * 0.3f, hue = Rnd();
        var col = new Color32((byte)(255f * Mathf.Min(1f, j * Mathf.Lerp(0.85f, 1f, hue))), (byte)(255f * Mathf.Min(1f, j)), (byte)(255f * j * Mathf.Lerp(1f, 0.8f, hue)), 255);
        var M = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), new Vector3(GrassS * s, GrassS * s * h, GrassS * s));
        if (kind == 2) GrassEmit(g, gRos, 0, M, col);
        GrassEmit(g1, kind == 1 ? gCardBig : gCardSm, 1, M, col);
    }

    static void GrassFlush(Transform root)
    {
        var t = new GameObject("Grass").transform; t.SetParent(root, false);
        foreach (var kv in grassChunks)
        {
            var g = kv.Value;
            if (g.v.Count == 0) continue;
            int mat = kv.Key.EndsWith("|0") ? 0 : 1;
            var m = new Mesh { name = "Grass_" + kv.Key };
            m.indexFormat = g.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(g.v); m.SetNormals(g.n); m.SetUVs(0, g.uv); m.SetColors(g.c); m.SetTriangles(g.t, 0);
            m.RecalculateBounds();
            var go = MeshObj("Grass_" + kv.Key.Replace('|', '_'), t, m, GrassMat(mat), false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.isStatic = true;
        }
        grassChunks.Clear();
    }

    // Haritaya cim yay: tarz sabit, yogunluk dogal yamalar halinde
    // Kullanici kendi assetlerini koyana kadar: sahne bos baslar (false yapinca eski dekor geri gelir)
    static bool CleanSlate = true;

    static void GrassScatter()
    {
        if (gRos == null || CleanSlate) return;
        const float step = 2.5f;
        for (float gx = -470f; gx <= 470f; gx += step)
            for (float gz = -470f; gz <= 470f; gz += step)
            {
                float x = gx + (Rnd() - 0.5f) * step * 1.1f, z = gz + (Rnd() - 0.5f) * step * 1.1f;
                var p = new Vector2(x, z);
                if (OnRoadTight(p, 0.8f) || InObb(p, 0.8f)) continue;
                float n = Mathf.PerlinNoise(x * 0.045f + 11f, z * 0.045f + 11f);
                float dens = Mathf.Lerp(0.3f, 1f, n);
                if (Rnd() > dens) continue;
                float y = GroundY(x, z);
                if (y < -0.2f) continue;
                var pos = new Vector3(x, y - 0.02f, z);
                float r = Rnd();
                bool patch = n > 0.62f;
                float s = 0.8f + Rnd() * 0.7f;
                if (r < (patch ? 0.28f : 0.14f)) GrassAdd(pos, 2, Rnd() * 360f, s, 0.9f + Rnd() * 0.3f);
                else if (r < (patch ? 0.62f : 0.4f)) GrassAdd(pos, 1, Rnd() * 360f, s, (patch ? 1.3f : 1f) + Rnd() * 0.3f);
                else GrassAdd(pos, 0, Rnd() * 360f, s, 0.8f + Rnd() * 0.3f);
            }
    }
}
