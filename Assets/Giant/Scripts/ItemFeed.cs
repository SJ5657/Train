using System.Collections.Generic;
using UnityEngine;

// 아이템 습득 알림: 얻을 때마다 화면 왼쪽에 아이콘 + 이름이 한 줄씩 쌓였다가 몇 초 뒤 사라짐.
// ItemFeed.Show("이름", 아이콘, 설명)으로 사용. 아이콘은 ItemFeed.RenderIcon(프리팹)으로 모델을 찍어 만들 수 있음.
public class ItemFeed : MonoBehaviour
{
    class Entry { public string name, desc; public Texture icon; public Color color; public float at; }

    static ItemFeed instance;
    readonly List<Entry> entries = new List<Entry>();
    const float Life = 4.5f, Fade = 0.6f;
    const int MaxShown = 5;

    static ItemFeed Get()
    {
        if (!instance)
        {
            var go = new GameObject("ItemFeed");
            instance = go.AddComponent<ItemFeed>();
        }
        return instance;
    }

    public static void Show(string name, Texture icon, string desc = null, Color? color = null)
    {
        var f = Get();
        f.entries.Add(new Entry { name = name, desc = desc, icon = icon, color = color ?? new Color(1f, 0.9f, 0.55f), at = Time.time });
        if (f.entries.Count > MaxShown) f.entries.RemoveAt(0);
    }

    // 프리팹을 화면 밖에 잠깐 놓고 전용 카메라로 찍어 아이콘 텍스처를 만듦 (배경 투명)
    public static Texture2D RenderIcon(GameObject prefab, int size = 96, float yaw = 30f)
    {
        if (!prefab) return null;
        var root = new GameObject("IconStage");
        root.transform.position = new Vector3(0f, -5000f, 0f);
        var obj = Instantiate(prefab, root.transform);
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (var c in obj.GetComponentsInChildren<Collider>()) c.enabled = false;
        var rs = obj.GetComponentsInChildren<Renderer>();
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(root.transform.position, Vector3.one);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var camGo = new GameObject("IconCam");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.01f; cam.farClipPlane = 1000f;
        float dist = b.extents.magnitude / Mathf.Sin(15f * Mathf.Deg2Rad) * 1.05f;
        cam.transform.position = b.center + new Vector3(0f, 0.45f, -1f).normalized * dist;
        cam.transform.LookAt(b.center);
        var lightGo = new GameObject("IconLight"); lightGo.transform.SetParent(root.transform, false);
        var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        var rt = RenderTexture.GetTemporary(size, size, 16, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        DestroyImmediate(root);
        return tex;
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || entries.Count == 0) return;
        float x = 20f, y = Screen.height * 0.32f, rowH = 54f;
        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
        var descStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            float age = Time.time - e.at;
            if (age > Life) { entries.RemoveAt(i); continue; }
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            float age = Time.time - e.at;
            float a = Mathf.Clamp01((Life - age) / Fade) * Mathf.Clamp01(age / 0.15f);
            float slide = (1f - Mathf.Clamp01(age / 0.2f)) * -40f; // 왼쪽에서 미끄러져 들어옴
            var r = new Rect(x + slide, y + i * rowH, 320f, rowH - 6f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f * a); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(e.color.r, e.color.g, e.color.b, a); GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, a);
            if (e.icon) GUI.DrawTexture(new Rect(r.x + 8f, r.y + 2f, r.height - 4f, r.height - 4f), e.icon, ScaleMode.ScaleToFit);
            float tx = r.x + 8f + r.height + 4f;
            nameStyle.normal.textColor = new Color(e.color.r, e.color.g, e.color.b, a);
            GUI.Label(new Rect(tx, r.y + 3f, r.width - (tx - r.x), 22f), "+ " + e.name, nameStyle);
            if (!string.IsNullOrEmpty(e.desc))
            {
                descStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, a);
                GUI.Label(new Rect(tx, r.y + 24f, r.width - (tx - r.x), 20f), e.desc, descStyle);
            }
        }
        GUI.color = Color.white;
    }
}
