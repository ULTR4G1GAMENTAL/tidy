using UnityEngine;

// TextMesh yazisinin font dokusunu ozel (derinlik testli) materyale baglar.
[ExecuteAlways]
[RequireComponent(typeof(TextMesh))]
public class LabelBinder : MonoBehaviour
{
    TextMesh tm;
    MeshRenderer mr;
    static MaterialPropertyBlock pb;

    void OnEnable()
    {
        Font.textureRebuilt += OnRebuilt;
        Bind();
    }

    void OnDisable() { Font.textureRebuilt -= OnRebuilt; }

    void Start() { Bind(); }

    void OnRebuilt(Font f)
    {
        if (tm != null && f == tm.font) Bind();
    }

    void Bind()
    {
        tm = GetComponent<TextMesh>();
        mr = GetComponent<MeshRenderer>();
        if (tm == null || mr == null || tm.font == null || tm.font.material == null) return;
        if (pb == null) pb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(pb);
        pb.SetTexture("_MainTex", tm.font.material.mainTexture);
        mr.SetPropertyBlock(pb);
    }
}
