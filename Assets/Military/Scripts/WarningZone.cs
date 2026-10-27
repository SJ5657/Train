using UnityEngine;

// 버텍스 컬러 + 알파 재질과 원형 메시 도우미 (BloodSplat·PassiveItems·PoliceUnit이 공유해서 씀).
public static class WarningZone
{
    public static Material material; // 버텍스 컬러 + 알파 지원 (Sprites/Default)

    public static Mesh Annulus(float inner, float outer, Color cIn, Color cOut)
    {
        int seg = 72;
        var v = new Vector3[seg * 2]; var c = new Color[seg * 2]; var t = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
            c[i * 2] = cIn; c[i * 2 + 1] = cOut;
            int n = (i + 1) % seg;
            t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
            t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        return m;
    }

    public static Mesh Wall(Color bottom, Color top)
    {
        int seg = 72;
        var v = new Vector3[seg * 2]; var c = new Color[seg * 2]; var t = new int[seg * 6];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v[i * 2] = d; v[i * 2 + 1] = d + Vector3.up;
            c[i * 2] = bottom; c[i * 2 + 1] = top;
            int n = (i + 1) % seg;
            t[i * 6] = i * 2; t[i * 6 + 1] = i * 2 + 1; t[i * 6 + 2] = n * 2;
            t[i * 6 + 3] = n * 2; t[i * 6 + 4] = i * 2 + 1; t[i * 6 + 5] = n * 2 + 1;
        }
        var m = new Mesh { vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        return m;
    }
}
