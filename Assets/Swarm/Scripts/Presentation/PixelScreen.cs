using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Pixel-perfect вывод: мир и UI рендерятся в низкорезолюционный RenderTexture
    /// (~216 px по ширине в портрете), затем растягиваются на экран с целым масштабом.
    /// Работает на любом разрешении/соотношении сторон Android, без пакета Pixel Perfect.
    /// </summary>
    public sealed class PixelScreen
    {
        public const int LayerWorld = 0;
        public const int LayerOutput = 2;  // встроенный «Ignore Raycast»
        public const int LayerUI = 5;      // встроенный «UI»
        const int TargetWidth = 216;

        public int Scale { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public RenderTexture Target { get; private set; }
        public Camera WorldCam { get; private set; }
        public Camera UiCam { get; private set; }
        public Camera OutputCam { get; private set; }
        public float SafeTop { get; private set; }     // инсет «чёлки» в RT-пикселях
        public float SafeBottom { get; private set; }
        public Vector2 WorldCamPos;                    // центр камеры в мире (в пикселях)

        int _screenW, _screenH;
        Rect _safe;
        Mesh _outputMesh;
        Material _outputMat;
        Transform _outputQuad;
        readonly Transform _root;

        public static readonly Color ClearColor = new Color32(0x3f, 0x77, 0x3b, 0xff);

        public PixelScreen(Transform root)
        {
            _root = root;
            WorldCam = MakeCam("WorldCamera", 0, ~((1 << LayerUI) | (1 << LayerOutput)), CameraClearFlags.SolidColor);
            WorldCam.backgroundColor = ClearColor;
            UiCam = MakeCam("UICamera", 1, 1 << LayerUI, CameraClearFlags.Depth);
            OutputCam = MakeCam("OutputCamera", 2, 1 << LayerOutput, CameraClearFlags.SolidColor);
            OutputCam.backgroundColor = Color.black;
            OutputCam.gameObject.AddComponent<AudioListener>();

            var shader = Resources.Load<Shader>("SwarmSprite");
            _outputMat = SpriteAtlas.MakeMaterial(shader, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero, null);
            var go = new GameObject("PixelOutputQuad") { layer = LayerOutput };
            go.transform.SetParent(_root, false);
            _outputQuad = go.transform;
            _outputMesh = new Mesh { name = "PixelOutputQuad" };
            _outputMesh.vertices = new[] { new Vector3(0, 0), new Vector3(0, 1), new Vector3(1, 1), new Vector3(1, 0) };
            _outputMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            _outputMesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            _outputMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = _outputMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _outputMat;
            go.transform.position = new Vector3(0f, 0f, -50000f);
            Refresh(true);
        }

        Camera MakeCam(string name, int depth, int mask, CameraClearFlags clear)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.depth = depth;
            cam.cullingMask = mask;
            cam.clearFlags = clear;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            return cam;
        }

        /// <summary>Пересоздаёт RT при смене разрешения/безопасной зоны.</summary>
        public void Refresh(bool force = false)
        {
            int sw = Screen.width, sh = Screen.height;
            Rect safe = Screen.safeArea;
            if (!force && sw == _screenW && sh == _screenH && safe == _safe) return;
            _screenW = sw; _screenH = sh; _safe = safe;

            Scale = Mathf.Max(1, Mathf.RoundToInt(sw / (float)TargetWidth));
            Width = Mathf.CeilToInt(sw / (float)Scale);
            Height = Mathf.CeilToInt(sh / (float)Scale);
            SafeTop = Mathf.Ceil((sh - safe.yMax) / Scale);
            SafeBottom = Mathf.Ceil(safe.yMin / Scale);

            if (Target != null) { Target.Release(); Object.Destroy(Target); }
            Target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                name = "SwarmPixelRT",
            };
            Target.Create();
            _outputMat.mainTexture = Target;

            WorldCam.targetTexture = Target;
            WorldCam.orthographicSize = Height * 0.5f;
            UiCam.targetTexture = Target;
            UiCam.orthographicSize = Height * 0.5f;
            // UI-камера смотрит на область (0..Width, 0..Height), спрятанную далеко от мира
            UiCam.transform.position = new Vector3(Width * 0.5f + UiOrigin.x, Height * 0.5f + UiOrigin.y, -10f);

            // выходной квад: RT * Scale, по центру экрана (обрезка остатка < Scale px)
            OutputCam.orthographicSize = sh * 0.5f;
            OutputCam.transform.position = new Vector3(0f, 0f, -50010f);
            float qw = Width * Scale, qh = Height * Scale;
            _outputQuad.localScale = new Vector3(qw, qh, 1f);
            _outputQuad.position = new Vector3(-qw * 0.5f, -qh * 0.5f, -50000f);
        }

        /// <summary>UI живёт в отдельной области мировых координат, чтобы не пересекаться с картой.</summary>
        public static readonly Vector2 UiOrigin = new Vector2(-20000f, -20000f);

        public void PlaceWorldCamera(Vector2 center)
        {
            WorldCamPos = center;
            // целые пиксели + полпикселя при нечётном размере RT, чтобы тексели совпадали с пикселями
            float ox = (Width & 1) == 1 ? 0.5f : 0f;
            float oy = (Height & 1) == 1 ? 0.5f : 0f;
            WorldCam.transform.position = new Vector3(Mathf.Round(center.x) + ox, Mathf.Round(center.y) + oy, -10f);
        }

        /// <summary>Экранные пиксели → пиксели RT (0..Width, 0..Height).</summary>
        public Vector2 ScreenToPixel(Vector2 screen)
        {
            float ox = (_screenW - Width * Scale) * 0.5f;
            float oy = (_screenH - Height * Scale) * 0.5f;
            return new Vector2((screen.x - ox) / Scale, (screen.y - oy) / Scale);
        }

        public Vector2 PixelToWorld(Vector2 px)
        {
            Vector3 c = WorldCam.transform.position;
            return new Vector2(c.x + px.x - Width * 0.5f, c.y + px.y - Height * 0.5f);
        }

        public Vector2 WorldToPixel(Vector2 w)
        {
            Vector3 c = WorldCam.transform.position;
            return new Vector2(w.x - c.x + Width * 0.5f, w.y - c.y + Height * 0.5f);
        }

        public Rect WorldViewRect(float margin)
        {
            Vector3 c = WorldCam.transform.position;
            return new Rect(c.x - Width * 0.5f - margin, c.y - Height * 0.5f - margin, Width + margin * 2f, Height + margin * 2f);
        }
    }
}
