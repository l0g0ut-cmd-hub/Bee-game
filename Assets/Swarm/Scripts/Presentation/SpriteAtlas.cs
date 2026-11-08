using UnityEngine;
using UnityEngine.Rendering;

namespace Swarm.Presentation
{
    /// <summary>Атлас и материалы. Данные прямоугольников генерируются tools/art/gen_art.py.</summary>
    public static class SpriteAtlas
    {
        public static Texture2D Texture { get; private set; }
        public static Material Alpha { get; private set; }
        public static Material Additive { get; private set; }

        static Vector2[] _uvMin, _uvMax;
        static int[] _w, _h, _px, _py;
        public static int SpriteCount { get; private set; }

        public static bool Ready => Texture != null;

        public static void Init()
        {
            if (Ready) return;
            Texture = Resources.Load<Texture2D>("SwarmAtlas");
            if (Texture == null)
            {
                Debug.LogError("[SWARM] Resources/SwarmAtlas.png не найден");
                Texture = Texture2D.whiteTexture;
            }
            Texture.filterMode = FilterMode.Point;
            Texture.wrapMode = TextureWrapMode.Clamp;

            var shader = Resources.Load<Shader>("SwarmSprite");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            Alpha = MakeMaterial(shader, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, Texture);
            Additive = MakeMaterial(shader, BlendMode.SrcAlpha, BlendMode.One, Texture);

            var r = AtlasData.Rects;
            int n = r.Length / 6;
            SpriteCount = n;
            _uvMin = new Vector2[n];
            _uvMax = new Vector2[n];
            _w = new int[n]; _h = new int[n]; _px = new int[n]; _py = new int[n];
            float inv = 1f / AtlasData.Size;
            for (int i = 0; i < n; i++)
            {
                int x = r[i * 6], y = r[i * 6 + 1], w = r[i * 6 + 2], h = r[i * 6 + 3];
                _w[i] = w; _h[i] = h; _px[i] = r[i * 6 + 4]; _py[i] = r[i * 6 + 5];
                _uvMin[i] = new Vector2(x * inv, y * inv);
                _uvMax[i] = new Vector2((x + w) * inv, (y + h) * inv);
            }
        }

        public static Material MakeMaterial(Shader shader, BlendMode src, BlendMode dst, Texture tex)
        {
            var m = new Material(shader) { mainTexture = tex };
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            return m;
        }

        public static int W(int s) => _w[s];
        public static int H(int s) => _h[s];
        public static int PivotX(int s) => _px[s];
        public static int PivotY(int s) => _py[s];
        public static Vector2 UvMin(int s) => _uvMin[s];
        public static Vector2 UvMax(int s) => _uvMax[s];

        public static int W(Spr s) => _w[(int)s];
        public static int H(Spr s) => _h[(int)s];
    }
}
