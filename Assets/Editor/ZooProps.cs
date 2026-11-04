using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Kullanicinin verdigi kaya, cesme, heykel, vazo ve cicek modelleri.
// Eski "yapay" modellerin yerine ayni olculerde yerlestirilir (Env() icinde yonlendirme).
public static partial class ZooBuilder
{
    const string PropDir = "Assets/Env/Props/";

    // id -> (kaya modeli, taban boyutu, yukseklik)
    static readonly Dictionary<string, object[]> PropRocks = new Dictionary<string, object[]>
    {
        { "D1/rock_S_a", new object[] { "rock_b", 1.0f, 0.7f } }, { "D1/rock_S_b", new object[] { "rock_a", 1.2f, 0.6f } }, { "D1/rock_S_c", new object[] { "rock_b", 0.9f, 0.9f } },
        { "D1/rock_M_a", new object[] { "rock_a", 2.5f, 1.6f } }, { "D1/rock_M_b", new object[] { "rock_a", 2.8f, 1.4f } }, { "D1/rock_M_c", new object[] { "rock_b", 2.2f, 1.9f } },
        { "D1/rock_L_a", new object[] { "rock_a", 5.0f, 3.0f } }, { "D1/rock_L_b", new object[] { "rock_b", 5.5f, 3.2f } },
        { "D1/rock_cluster_a", new object[] { "rock_a", 6.0f, 2.5f } }, { "D1/rock_cluster_b", new object[] { "rock_b", 6.0f, 3.0f } },
        { "N2/boulder_big_a", new object[] { "rock_a", 5.0f, 3.2f } }, { "N2/boulder_big_b", new object[] { "rock_b", 6.0f, 4.0f } },
        { "N2/cliff_rock_a", new object[] { "rock_b", 12.0f, 9.0f } }, { "N2/cliff_rock_b", new object[] { "rock_a", 14.0f, 9.5f } },
        { "N2/island_rock_group", new object[] { "rock_b", 6.0f, 2.6f } },
        { "N2/lake_rock_a", new object[] { "rock_a", 1.2f, 0.8f } }, { "N2/lake_rock_b", new object[] { "rock_b", 2.0f, 1.3f } },
        { "N2/lake_rock_c", new object[] { "rock_a", 3.0f, 1.6f } }, { "N2/lake_rock_d", new object[] { "rock_b", 0.8f, 0.6f } },
    };

    static Texture2D PropTex(string file, bool normal)
    {
        string p = PropDir + file;
        var imp = AssetImporter.GetAtPath(p) as TextureImporter;
        if (imp != null)
        {
            bool want = normal ? imp.textureType == TextureImporterType.NormalMap : imp.textureType == TextureImporterType.Default;
            if (!want || imp.maxTextureSize != 2048)
            {
                imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.maxTextureSize = 2048; imp.anisoLevel = 4; imp.mipmapEnabled = true;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static Material PropMat(string name, Color c)
    {
        Material m;
        if (mats.TryGetValue(name, out m) && m != null) return m;
        string baseTex = null, nrmTex = null; float smooth = 0.2f; bool twoSided = false; Color tint = Color.white;
        switch (name)
        {
            case "prop_rock_a": baseTex = "rock_a_base.png"; nrmTex = "rock_a_nrm.png"; smooth = 0.2f; tint = new Color(1.08f, 1.06f, 0.98f); break;
            case "prop_rock_b": baseTex = "rock_b_base.jpg"; nrmTex = "rock_b_nrm.png"; smooth = 0.2f; tint = new Color(1.1f, 1.1f, 1.0f); break;
            case "prop_fountain": baseTex = "fountain_base.jpg"; smooth = 0.35f; tint = new Color(1.1f, 1.08f, 1.05f); break;
            case "prop_breath": baseTex = "breath_base.jpg"; nrmTex = "breath_nrm.jpg"; smooth = 0.15f; tint = new Color(1.12f, 1.1f, 1.04f); break;
            case "prop_pot": baseTex = "pot_base.jpg"; nrmTex = "pot_nrm.jpg"; smooth = 0.25f; tint = new Color(1.1f, 1.08f, 1.02f); break;
            case "prop_purple": baseTex = "purple_base.png"; smooth = 0.3f; twoSided = true; tint = new Color(1.1f, 1.05f, 1.1f); break;
            case "prop_lily": baseTex = "flowers_base.png"; smooth = 0.1f; twoSided = true; tint = new Color(1.15f, 1.05f, 1.05f); break;
            case "prop_mg_green": c = new Color(0.22f, 0.78f, 0.18f); twoSided = true; break;
            case "prop_mg_white": c = new Color(1f, 1f, 0.97f); twoSided = true; break;
            case "prop_mg_yellow": c = new Color(1f, 0.82f, 0.1f); twoSided = true; break;
        }
        m = Mat("env_" + name, baseTex != null ? tint : c);
        if (baseTex != null)
        {
            m.SetTexture("_BaseMap", PropTex(baseTex, false));
            if (nrmTex != null) { m.SetTexture("_BumpMap", PropTex(nrmTex, true)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
        }
        m.SetFloat("_Smoothness", smooth);
        if (twoSided) m.SetFloat("_Cull", 0f);
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    class PB
    {
        public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>();
        public Dictionary<string, List<int>> t = new Dictionary<string, List<int>>();
        public void Add(Mesh src, string[] matNames, Matrix4x4 M)
        {
            var sv = src.vertices; var sn = src.normals; var su = src.uv; int b = v.Count;
            Matrix4x4 N = M.inverse.transpose;
            for (int i = 0; i < sv.Length; i++)
            {
                v.Add(M.MultiplyPoint3x4(sv[i]));
                n.Add(sn.Length > i ? N.MultiplyVector(sn[i]).normalized : Vector3.up);
                uv.Add(su.Length > i ? su[i] : Vector2.zero);
            }
            for (int s = 0; s < src.subMeshCount && s < matNames.Length; s++)
            {
                List<int> l; if (!t.TryGetValue(matNames[s], out l)) { l = new List<int>(); t[matNames[s]] = l; }
                foreach (int i in src.GetTriangles(s)) l.Add(b + i);
            }
        }
        public EnvModel Build(string name)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = t.Count; var names = new string[t.Count]; var cols = new Color[t.Count]; int k = 0;
            foreach (var kv in t) { m.SetTriangles(kv.Value, k); names[k] = kv.Key; cols[k] = Color.white; k++; }
            m.RecalculateBounds(); m.RecalculateTangents();
            return new EnvModel { mesh = m, mats = names, cols = cols, b = m.bounds };
        }
    }

    class PropSrc { public Mesh mesh; public string[] mats; public Bounds b; }
    static readonly Dictionary<string, PropSrc> propSrc = new Dictionary<string, PropSrc>();

    static PropSrc PS(string file, string[] mats)
    {
        PropSrc p; if (propSrc.TryGetValue(file, out p)) return p;
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + file);
        var mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null) { Debug.LogWarning("Prop yok: " + file); propSrc[file] = null; return null; }
        p = new PropSrc { mesh = mf.sharedMesh, mats = mats, b = mf.sharedMesh.bounds };
        propSrc[file] = p; return p;
    }

    // modelin tabani y=0, xz merkezli ve verilen olcekte
    static Matrix4x4 PBase(PropSrc p, Vector3 sc, Vector3 pos, float yaw)
    {
        Vector3 c = p.b.center; float y0 = p.b.min.y;
        return Matrix4x4.TRS(pos, Quaternion.Euler(0, yaw, 0), sc) * Matrix4x4.Translate(new Vector3(-c.x, -y0, -c.z));
    }

    static string[] MgMats = { "prop_mg_green", "prop_mg_white", "prop_mg_yellow" };

    static void PFlower(PB pb, System.Random r, Vector3 pos, bool lily, float jit)
    {
        float s = 0.85f + (float)r.NextDouble() * 0.4f; float yaw = (float)r.NextDouble() * 360f;
        if (lily)
        {
            var p = PS("flowers.fbx", new[] { "prop_lily" }); if (p == null) return;
            float k = 0.5f / p.b.size.y * s * jit;
            pb.Add(p.mesh, p.mats, PBase(p, new Vector3(k, k, k), pos, yaw));
        }
        else
        {
            var p = PS("margarita.fbx", MgMats); if (p == null) return;
            float k = 0.5f / p.b.size.y * s * jit;
            pb.Add(p.mesh, p.mats, PBase(p, new Vector3(k, k, k), pos, yaw));
        }
    }

    static EnvModel PropModel(string id)
    {
        object[] rk;
        if (PropRocks.TryGetValue(id, out rk))
        {
            string file = (string)rk[0]; float fp = (float)rk[1], h = (float)rk[2];
            var p = PS(file + ".obj", new[] { "prop_" + file }); if (p == null) return null;
            float s = fp / Mathf.Max(p.b.size.x, p.b.size.z);
            float sy = Mathf.Min(s * 1.05f, h * 1.25f / p.b.size.y);
            var pb = new PB();
            pb.Add(p.mesh, p.mats, PBase(p, new Vector3(s, sy, s), Vector3.zero, 0f));
            return pb.Build(id);
        }
        var r = new System.Random(id.GetHashCode());
        switch (id)
        {
            case "A1/aquarium_forecourt_fountain":
            case "S2/fountain_round":
            {
                var p = PS("fountain.obj", new[] { "prop_fountain" }); if (p == null) return null;
                float s = 8.8f / Mathf.Max(p.b.size.x, p.b.size.z);
                var pb = new PB(); pb.Add(p.mesh, p.mats, PBase(p, new Vector3(s, s, s), Vector3.zero, 0f)); return pb.Build(id);
            }
            case "A1/aquarium_whale_sculpture":
            case "A3/aq_sunken_statue":
            {
                var p = PS("breath.obj", new[] { "prop_breath" }); if (p == null) return null;
                float s = (id == "A3/aq_sunken_statue" ? 1.6f : 4.2f) / p.b.size.y;
                var pb = new PB(); pb.Add(p.mesh, p.mats, PBase(p, new Vector3(s, s, s), Vector3.zero, 0f)); return pb.Build(id);
            }
            case "P/purple":
            {
                var p = PS("purple.obj", new[] { "prop_purple" }); if (p == null) return null;
                float s = 2.2f / p.b.size.y;
                var pb = new PB(); pb.Add(p.mesh, p.mats, PBase(p, new Vector3(s, s, s), Vector3.zero, 0f)); return pb.Build(id);
            }
            case "S1/planter_box_large":
            {
                var pot = PS("flowerpot.fbx", new[] { "prop_pot" }); if (pot == null) return null;
                var pb = new PB();
                for (int i = -1; i <= 1; i += 2)
                {
                    float ps = 0.85f / pot.b.size.y;
                    pb.Add(pot.mesh, pot.mats, PBase(pot, new Vector3(ps, ps, ps), new Vector3(i * 0.55f, 0, 0), i * 30f));
                    for (int f = 0; f < 5; f++)
                    {
                        float a = f * 1.26f; float rr = f == 0 ? 0f : 0.09f;
                        PFlower(pb, r, new Vector3(i * 0.55f + Mathf.Cos(a) * rr, 0.62f, Mathf.Sin(a) * rr), f % 2 == 1, 0.9f);
                    }
                }
                return pb.Build(id);
            }
            case "N1/flower_bed_round":
            {
                var pb = new PB();
                var rock = PS("rock_b.obj", new[] { "prop_rock_b" });
                for (int f = 0; f < 52; f++)
                {
                    float a = (float)r.NextDouble() * 6.283f, rr = Mathf.Sqrt((float)r.NextDouble()) * 1.45f;
                    PFlower(pb, r, new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr), f % 3 == 0, 1.1f);
                }
                if (rock != null)
                    for (int i = 0; i < 9; i++)
                    {
                        float a = i / 9f * 6.283f; float k = 0.42f / Mathf.Max(rock.b.size.x, rock.b.size.z) * (0.8f + (float)r.NextDouble() * 0.5f);
                        pb.Add(rock.mesh, rock.mats, PBase(rock, new Vector3(k * 1.4f, k, k * 1.4f), new Vector3(Mathf.Cos(a) * 1.75f, -0.05f, Mathf.Sin(a) * 1.75f), a * 57f));
                    }
                return pb.Build(id);
            }
        }
        if (id.StartsWith("N1/flower_") && id.EndsWith("_patch") || id == "N1/flower_tulip_red" || id == "N1/flower_tulip_yellow")
        {
            var pb = new PB();
            bool daisy = id.Contains("daisy"); bool lilyHeavy = id.Contains("poppy") || id.Contains("tulip_red") || id.Contains("pink");
            int n = 9 + r.Next(5);
            for (int f = 0; f < n; f++)
            {
                float a = (float)r.NextDouble() * 6.283f, rr = Mathf.Sqrt((float)r.NextDouble()) * 0.85f;
                bool lily = !daisy && (lilyHeavy ? r.NextDouble() < 0.75 : r.NextDouble() < 0.4);
                PFlower(pb, r, new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr), lily, 0.9f);
            }
            return pb.Build(id);
        }
        return null;
    }

    static bool IsPropId(string id)
    {
        if (PropRocks.ContainsKey(id) || id == "P/purple") return true;
        return id == "A1/aquarium_forecourt_fountain" || id == "S2/fountain_round" || id == "A1/aquarium_whale_sculpture" || id == "A3/aq_sunken_statue"
            || id == "S1/planter_box_large" || id == "N1/flower_bed_round" || (id.StartsWith("N1/flower_") && (id.EndsWith("_patch") || id.EndsWith("tulip_red") || id.EndsWith("tulip_yellow")));
    }
}
