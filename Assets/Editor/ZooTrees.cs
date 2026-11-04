using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Kullanicinin verdigi "this tree is growing" agaci (gövde + yaprak/cicek örtüsü).
// Tek bir hafifletilmis mesh olarak hazirlanir, tum agac noktalarinda kullanilir.
public static partial class ZooBuilder
{
    static bool UserTrees = true;
    const string TreeAssetDir = "Assets/Env/TreeAssets/";
    static Mesh utMesh;
    const int UtTrunkTris = 1800, UtFolTris = 2600;
    const float UtHeight = 8.5f;   // olcek 1'de agac boyu (m)

    // ---- UV'yi koruyan tepe noktasi kumeleme ile sadelestirme
    static void UtDecimate(List<Vector3> P, List<Vector2> UV, List<int> T, int target, int uvGrid, out List<Vector3> oP, out List<Vector2> oUV, out List<int> oT)
    {
        Vector3 lo = P[0], hi = P[0];
        foreach (var p in P) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        Vector3 ext = hi - lo;
        float g = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z)) / Mathf.Sqrt(target / 2f);
        Dictionary<long, int> map = null; List<int> t = null; int cnt = 0;
        for (int iter = 0; iter < 24; iter++)
        {
            map = new Dictionary<long, int>(); var id = new int[P.Count];
            for (int i = 0; i < P.Count; i++)
            {
                long kx = (long)Mathf.Floor((P[i].x - lo.x) / g), ky = (long)Mathf.Floor((P[i].y - lo.y) / g), kz = (long)Mathf.Floor((P[i].z - lo.z) / g);
                long ku = (long)Mathf.Floor(UV[i].x * uvGrid), kv = (long)Mathf.Floor(UV[i].y * uvGrid);
                long key = ((kx * 1315423911L + ky) * 2654435761L + kz) * 97531L + ku * 8191L + kv;
                int c; if (!map.TryGetValue(key, out c)) { c = map.Count; map[key] = c; }
                id[i] = c;
            }
            t = new List<int>(); var seen = new HashSet<long>();
            for (int i = 0; i + 2 < T.Count; i += 3)
            {
                int a = id[T[i]], b = id[T[i + 1]], c2 = id[T[i + 2]];
                if (a == b || b == c2 || a == c2) continue;
                int s0 = Mathf.Min(a, Mathf.Min(b, c2)), s2 = Mathf.Max(a, Mathf.Max(b, c2)), s1 = a + b + c2 - s0 - s2;
                long k = ((long)s0 * 73856093L) ^ ((long)s1 * 19349663L) ^ ((long)s2 * 83492791L);
                if (!seen.Add(k)) continue;
                t.Add(a); t.Add(b); t.Add(c2);
            }
            cnt = t.Count / 3;
            if (Mathf.Abs(cnt - target) < target * 0.2f) { UtMapId = id; break; }
            UtMapId = id;
            g *= Mathf.Sqrt((float)cnt / target);
        }
        int n = map.Count;
        var sp = new Vector3[n]; var su = new Vector2[n]; var sc = new int[n];
        for (int i = 0; i < P.Count; i++) { int c = UtMapId[i]; sp[c] += P[i]; su[c] += UV[i]; sc[c]++; }
        oP = new List<Vector3>(n); oUV = new List<Vector2>(n);
        for (int i = 0; i < n; i++) { oP.Add(sp[i] / Mathf.Max(1, sc[i])); oUV.Add(su[i] / Mathf.Max(1, sc[i])); }
        oT = t;
    }
    static int[] UtMapId;

    static void UtCollect(Mesh src, Matrix4x4 M, List<Vector3> P, List<Vector2> UV, List<int> T)
    {
        var v = src.vertices; var uv = src.uv; int b = P.Count;
        for (int i = 0; i < v.Length; i++) { P.Add(M.MultiplyPoint3x4(v[i])); UV.Add(uv.Length > i ? uv[i] : Vector2.zero); }
        foreach (int i in src.triangles) T.Add(b + i);
    }

    static Mesh UserTreeMesh()
    {
        if (utMesh != null) return utMesh;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreeAssetDir + "milestone.fbx");
        if (prefab == null) { Debug.LogWarning("milestone.fbx yok"); return null; }
        var inst = (GameObject)Object.Instantiate(prefab);
        try
        {
            MeshFilter tmf = null, fmf = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>()) { if (mf.name == "tree") tmf = mf; else if (mf.name == "foliage") fmf = mf; }
            if (tmf == null || fmf == null) { Debug.LogWarning("tree/foliage dugumleri yok"); return null; }
            // govde
            var tP = new List<Vector3>(); var tU = new List<Vector2>(); var tT = new List<int>();
            UtCollect(tmf.sharedMesh, tmf.transform.localToWorldMatrix, tP, tU, tT);
            Vector3 lo = tP[0], hi = tP[0];
            foreach (var p in tP) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            // alttaki ucan kaya adasi kesilir: govde kokunden basla
            float yCut = lo.y + 2.55f;
            { var nP = new List<Vector3>(); var nU = new List<Vector2>(); var nT = new List<int>(); var rm = new int[tP.Count];
              for (int i = 0; i < rm.Length; i++) rm[i] = -1;
              for (int i = 0; i + 2 < tT.Count; i += 3)
              {
                  float cy = (tP[tT[i]].y + tP[tT[i + 1]].y + tP[tT[i + 2]].y) / 3f;
                  if (cy < yCut) continue;
                  for (int k = 0; k < 3; k++) { int v = tT[i + k]; if (rm[v] < 0) { rm[v] = nP.Count; nP.Add(tP[v]); nU.Add(tU[v]); } nT.Add(rm[v]); }
              }
              tP = nP; tU = nU; tT = nT; }
            Vector3 bsum = Vector3.zero; int bn = 0;
            foreach (var p in tP) if (p.y < yCut + 0.5f) { bsum += p; bn++; }
            Vector3 axis = new Vector3(bsum.x / Mathf.Max(1, bn), yCut - 0.15f, bsum.z / Mathf.Max(1, bn));
            float h = hi.y - yCut;
            // yaprak: yalnizca govdeye yakin parcalar (yerdeki dagınık cicek kumeleri atilir)
            var fP = new List<Vector3>(); var fU = new List<Vector2>(); var fT = new List<int>();
            UtCollect(fmf.sharedMesh, fmf.transform.localToWorldMatrix, fP, fU, fT);
            int[] par = new int[fP.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
            System.Func<int, int> find = null; find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
            var byPos = new Dictionary<long, int>();
            for (int i = 0; i < fP.Count; i++)
            {
                long k = ((long)Mathf.RoundToInt(fP[i].x * 400f) * 73856093L) ^ ((long)Mathf.RoundToInt(fP[i].y * 400f) * 19349663L) ^ ((long)Mathf.RoundToInt(fP[i].z * 400f) * 83492791L);
                int o; if (byPos.TryGetValue(k, out o)) par[find(i)] = find(o); else byPos[k] = i;
            }
            for (int i = 0; i + 2 < fT.Count; i += 3) { par[find(fT[i + 1])] = find(fT[i]); par[find(fT[i + 2])] = find(fT[i]); }
            var sum = new Dictionary<int, Vector3>(); var num = new Dictionary<int, int>();
            for (int i = 0; i < fP.Count; i++) { int r = find(i); Vector3 s; sum.TryGetValue(r, out s); sum[r] = s + fP[i]; int c; num.TryGetValue(r, out c); num[r] = c + 1; }
            var keep = new HashSet<int>();
            foreach (var kv in sum)
            {
                Vector3 c = kv.Value / num[kv.Key];
                float dx = c.x - axis.x, dz = c.z - axis.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) < 2.6f && c.y > yCut - 0.3f) keep.Add(kv.Key);
            }
            var fP2 = new List<Vector3>(); var fU2 = new List<Vector2>(); var fT2 = new List<int>(); var remap = new int[fP.Count];
            for (int i = 0; i < fP.Count; i++) { remap[i] = -1; if (keep.Contains(find(i))) { remap[i] = fP2.Count; fP2.Add(fP[i]); fU2.Add(fU[i]); } }
            for (int i = 0; i + 2 < fT.Count; i += 3) if (remap[fT[i]] >= 0) { fT2.Add(remap[fT[i]]); fT2.Add(remap[fT[i + 1]]); fT2.Add(remap[fT[i + 2]]); }
            Debug.Log("Agac: govde " + tT.Count / 3 + " ucgen, yaprak (filtreli) " + fT2.Count / 3 + " ucgen, boy " + h);

            List<Vector3> dP; List<Vector2> dU; List<int> dT;
            var all = new List<Vector3>(); var allU = new List<Vector2>(); var allC = new List<Color32>(); var tri0 = new List<int>(); var tri1 = new List<int>(); var allN = new List<Vector3>();
            float scale = 1.4f;
            for (int part = 0; part < 2; part++)
            {
                if (part == 0) UtDecimate(tP, tU, tT, UtTrunkTris, 24, out dP, out dU, out dT);
                else UtDecimate(fP2, fU2, fT2, UtFolTris, 24, out dP, out dU, out dT);
                int b = all.Count;
                // normaller
                var nn = new Vector3[dP.Count];
                for (int i = 0; i + 2 < dT.Count; i += 3)
                {
                    Vector3 n = Vector3.Cross(dP[dT[i + 1]] - dP[dT[i]], dP[dT[i + 2]] - dP[dT[i]]);
                    nn[dT[i]] += n; nn[dT[i + 1]] += n; nn[dT[i + 2]] += n;
                }
                for (int i = 0; i < dP.Count; i++)
                {
                    Vector3 q = (dP[i] - axis) * scale;
                    all.Add(q); allU.Add(dU[i]);
                    Vector3 nv = nn[i].sqrMagnitude < 1e-12f ? Vector3.up : nn[i].normalized;
                    if (part == 1) nv = (nv * 0.5f + Vector3.up * 0.7f).normalized;
                    allN.Add(nv);
                    float w = part == 1 ? Mathf.Clamp01(q.y / (h * scale)) * 0.9f : 0f;
                    allC.Add(new Color32(255, 255, 255, (byte)(w * 255f)));
                }
                foreach (int i in dT) (part == 0 ? tri0 : tri1).Add(b + i);
            }
            var m = new Mesh { name = "UserTree", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(all); m.SetNormals(allN); m.SetUVs(0, allU); m.SetColors(allC);
            m.subMeshCount = 2; m.SetTriangles(tri0, 0); m.SetTriangles(tri1, 1);
            m.RecalculateBounds();
            Debug.Log("Agac mesh: " + all.Count + " kose, " + (tri0.Count + tri1.Count) / 3 + " ucgen, bounds " + m.bounds.size);
            utMesh = m;
        }
        finally { Object.DestroyImmediate(inst); }
        return utMesh;
    }

    static Texture2D UtTex(string file)
    {
        string p = TreeAssetDir + file;
        var imp = AssetImporter.GetAtPath(p) as TextureImporter;
        if (imp != null && imp.maxTextureSize != 1024)
        { imp.maxTextureSize = 1024; imp.anisoLevel = 4; imp.mipmapEnabled = true; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static Material UtMat(string name)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        if (name == "ut_trunk")
        {
            m = Mat("ut_trunk", new Color(1.1f, 1.05f, 1f));
            m.SetTexture("_BaseMap", UtTex("tree_baked.jpg")); m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m); mats[name] = m; return m;
        }
        var sh = Shader.Find("Zoo/Foliage");
        string path = "Assets/Zoo/Materials/ut_foliage.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", UtTex("foliage_baked_BC.jpg"));
        m.SetColor("_BaseColor", new Color(1.15f, 1.2f, 1.05f, 1f));
        m.SetFloat("_Cutoff", 0.05f); m.SetFloat("_Sway", 1.2f); m.SetFloat("_Variation", 0.25f);
        m.renderQueue = 2450;
        EditorUtility.SetDirty(m); mats[name] = m; return m;
    }

    static bool PlaceUserTree(string id, Vector3 pos, float scale, float yaw, bool collide)
    {
        var mesh = UserTreeMesh(); if (mesh == null) return false;
        var ts = TreeStyleOf(id);
        float k = scale * 1.15f;
        float r1 = (float)((id.GetHashCode() ^ (int)(pos.x * 31f) ^ (int)(pos.z * 17f)) & 255) / 255f;
        float sxz = k * (0.85f + 0.35f * r1), sy = k * (0.9f + 0.3f * (1f - r1));
        Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), new Vector3(sxz, sy, sxz));
        int cx = Mathf.FloorToInt(pos.x / 100f), cz = Mathf.FloorToInt(pos.z / 100f);
        ChunkAdd(cx, cz, "ut_trunk", mesh, 0, m);
        ChunkAdd(cx, cz, "ut_foliage", mesh, 1, m);
        if (collide)
        {
            string key = cx + "_" + cz;
            List<Vector4> l; if (!treeCols.TryGetValue(key, out l)) { l = new List<Vector4>(); treeCols[key] = l; }
            l.Add(new Vector4(pos.x, pos.y, pos.z, 0.45f * sxz));
        }
        return true;
    }
}
