using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Один меш = один draw call на целый слой спрайтов. Вершины пишутся в преаллоцированные
    /// массивы, загрузка в меш — через перегрузки с диапазоном, без аллокаций в кадре.
    /// Позиции округляются до целых пикселей — основа pixel-perfect картинки.
    /// </summary>
    public sealed class QuadBatch
    {
        public readonly int Capacity;
        readonly Vector3[] _v;
        readonly Vector2[] _uv;
        readonly Color32[] _c;
        readonly int[] _idx;
        readonly Mesh _mesh;
        public readonly GameObject Go;
        readonly MeshRenderer _renderer;
        int _count;
        bool _overflowWarned;
        public float Z;

        public int Count => _count;

        public QuadBatch(string name, int capacity, Material mat, int sortingOrder, int layer, Transform parent)
        {
            Capacity = capacity;
            _v = new Vector3[capacity * 4];
            _uv = new Vector2[capacity * 4];
            _c = new Color32[capacity * 4];
            _idx = new int[capacity * 6];
            for (int q = 0; q < capacity; q++)
            {
                int v = q * 4, i = q * 6;
                _idx[i] = v; _idx[i + 1] = v + 1; _idx[i + 2] = v + 2;
                _idx[i + 3] = v; _idx[i + 4] = v + 2; _idx[i + 5] = v + 3;
            }
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            Go = new GameObject(name) { layer = layer };
            Go.transform.SetParent(parent, false);
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = Go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = mat;
            _renderer.sortingOrder = sortingOrder;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        public void SetVisible(bool v) => _renderer.enabled = v;

        public void Begin() => _count = 0;

        public void End()
        {
            _mesh.Clear();
            if (_count == 0) return;
            int vc = _count * 4;
            _mesh.SetVertices(_v, 0, vc);
            _mesh.SetUVs(0, _uv, 0, vc);
            _mesh.SetColors(_c, 0, vc);
            _mesh.SetTriangles(_idx, 0, _count * 6, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 1000f));
        }

        bool Reserve(out int v)
        {
            if (_count >= Capacity)
            {
                if (!_overflowWarned) { Debug.LogWarning("[SWARM] QuadBatch overflow: " + _mesh.name); _overflowWarned = true; }
                v = 0;
                return false;
            }
            v = _count * 4;
            _count++;
            return true;
        }

        /// <summary>Спрайт по пивоту, без вращения. scale — целое увеличение (для крупного текста/логотипа).</summary>
        public void Add(int spr, float x, float y, Color32 col, bool flipX = false, int scale = 1)
        {
            if (!Reserve(out int v)) return;
            int w = SpriteAtlas.W(spr) * scale, h = SpriteAtlas.H(spr) * scale;
            int px = SpriteAtlas.PivotX(spr) * scale, py = SpriteAtlas.PivotY(spr) * scale;
            float x0 = Mathf.Round(x) - (flipX ? w - px : px);
            float y0 = Mathf.Round(y) - py;
            float x1 = x0 + w, y1 = y0 + h;
            _v[v] = new Vector3(x0, y0, Z);
            _v[v + 1] = new Vector3(x0, y1, Z);
            _v[v + 2] = new Vector3(x1, y1, Z);
            _v[v + 3] = new Vector3(x1, y0, Z);
            Vector2 a = SpriteAtlas.UvMin(spr), b = SpriteAtlas.UvMax(spr);
            if (flipX) { float t = a.x; a.x = b.x; b.x = t; }
            _uv[v] = new Vector2(a.x, a.y);
            _uv[v + 1] = new Vector2(a.x, b.y);
            _uv[v + 2] = new Vector2(b.x, b.y);
            _uv[v + 3] = new Vector2(b.x, a.y);
            _c[v] = _c[v + 1] = _c[v + 2] = _c[v + 3] = col;
        }

        public void Add(Spr spr, float x, float y, Color32 col, bool flipX = false, int scale = 1) =>
            Add((int)spr, x, y, col, flipX, scale);

        /// <summary>Спрайт, растянутый на прямоугольник (левый нижний угол + размер).</summary>
        public void AddStretched(int spr, float x, float y, float w, float h, Color32 col)
        {
            if (!Reserve(out int v)) return;
            float x0 = Mathf.Round(x), y0 = Mathf.Round(y);
            float x1 = x0 + Mathf.Round(w), y1 = y0 + Mathf.Round(h);
            _v[v] = new Vector3(x0, y0, Z);
            _v[v + 1] = new Vector3(x0, y1, Z);
            _v[v + 2] = new Vector3(x1, y1, Z);
            _v[v + 3] = new Vector3(x1, y0, Z);
            Vector2 a = SpriteAtlas.UvMin(spr), b = SpriteAtlas.UvMax(spr);
            _uv[v] = new Vector2(a.x, a.y);
            _uv[v + 1] = new Vector2(a.x, b.y);
            _uv[v + 2] = new Vector2(b.x, b.y);
            _uv[v + 3] = new Vector2(b.x, a.y);
            _c[v] = _c[v + 1] = _c[v + 2] = _c[v + 3] = col;
        }

        /// <summary>Часть спрайта (в пикселях спрайта) — для 9-slice панелей.</summary>
        public void AddSub(int spr, int sx, int sy, int sw, int sh, float x, float y, float w, float h, Color32 col)
        {
            if (sw <= 0 || sh <= 0 || w <= 0 || h <= 0) return;
            if (!Reserve(out int v)) return;
            float x0 = Mathf.Round(x), y0 = Mathf.Round(y);
            float x1 = x0 + Mathf.Round(w), y1 = y0 + Mathf.Round(h);
            _v[v] = new Vector3(x0, y0, Z);
            _v[v + 1] = new Vector3(x0, y1, Z);
            _v[v + 2] = new Vector3(x1, y1, Z);
            _v[v + 3] = new Vector3(x1, y0, Z);
            Vector2 a = SpriteAtlas.UvMin(spr), b = SpriteAtlas.UvMax(spr);
            float du = (b.x - a.x) / SpriteAtlas.W(spr), dv = (b.y - a.y) / SpriteAtlas.H(spr);
            float u0 = a.x + sx * du, u1 = a.x + (sx + sw) * du;
            float v0 = a.y + sy * dv, v1 = a.y + (sy + sh) * dv;
            _uv[v] = new Vector2(u0, v0);
            _uv[v + 1] = new Vector2(u0, v1);
            _uv[v + 2] = new Vector2(u1, v1);
            _uv[v + 3] = new Vector2(u1, v0);
            _c[v] = _c[v + 1] = _c[v + 2] = _c[v + 3] = col;
        }

        /// <summary>Повёрнутый спрайт вокруг пивота (стрелки-индикаторы).</summary>
        public void AddRotated(int spr, float x, float y, float angle, Color32 col)
        {
            if (!Reserve(out int v)) return;
            float w = SpriteAtlas.W(spr), h = SpriteAtlas.H(spr);
            float px = SpriteAtlas.PivotX(spr), py = SpriteAtlas.PivotY(spr);
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            x = Mathf.Round(x); y = Mathf.Round(y);
            Corner(v, -px, -py, c, s, x, y);
            Corner(v + 1, -px, h - py, c, s, x, y);
            Corner(v + 2, w - px, h - py, c, s, x, y);
            Corner(v + 3, w - px, -py, c, s, x, y);
            Vector2 a = SpriteAtlas.UvMin(spr), b = SpriteAtlas.UvMax(spr);
            _uv[v] = new Vector2(a.x, a.y);
            _uv[v + 1] = new Vector2(a.x, b.y);
            _uv[v + 2] = new Vector2(b.x, b.y);
            _uv[v + 3] = new Vector2(b.x, a.y);
            _c[v] = _c[v + 1] = _c[v + 2] = _c[v + 3] = col;
        }

        void Corner(int v, float lx, float ly, float c, float s, float x, float y)
        {
            _v[v] = new Vector3(x + lx * c - ly * s, y + lx * s + ly * c, Z);
        }

        public void Rect(float x, float y, float w, float h, Color32 col) => AddStretched((int)Spr.Pixel, x, y, w, h, col);
    }
}
