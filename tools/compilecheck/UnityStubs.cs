// Стабы UnityEngine/UnityEditor для проверки компиляции вне Unity.
// Каждый член UnityEngine сверяется с реальными определениями Unity 6 скриптом check_api.py.
// Математика Vector2/Mathf берётся из tools/simtest/UnityMathStub.cs.
#pragma warning disable CS0067, CS0626, CS0649, CS0169, CS0414
using System;

namespace UnityEngine
{
    public partial struct Vector2
    {
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
    }


    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0f; }
        public static Vector3 zero => default;
    }

    public struct Vector4 { public float x, y, z, w; }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => new Color(0, 0, 0, 1);
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public bool Contains(Vector2 p) => p.x >= x && p.x < x + width && p.y >= y && p.y < y + height;
        public static bool operator ==(Rect a, Rect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override bool Equals(object o) => o is Rect r && r == this;
        public override int GetHashCode() => 0;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
    }

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static implicit operator bool(Object o) => o != null;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public int layer { get; set; }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => null;
        public void SetActive(bool value) { }
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localScale { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Vector2[] uv { get; set; }
        public Color32[] colors32 { get; set; }
        public int[] triangles { get; set; }
        public Bounds bounds { get; set; }
        public void MarkDynamic() { }
        public void Clear() { }
        public void SetVertices(Vector3[] inVertices, int start, int length) { }
        public void SetUVs(int channel, Vector2[] uvs, int start, int length) { }
        public void SetColors(Color32[] inColors, int start, int length) { }
        public void SetTriangles(int[] triangles, int trianglesStart, int trianglesLength, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
    }

    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public int sortingOrder { get; set; }
        public bool enabled { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public Rendering.LightProbeUsage lightProbeUsage { get; set; }
        public Rendering.ReflectionProbeUsage reflectionProbeUsage { get; set; }
    }

    public sealed class MeshRenderer : Renderer { }

    public class Shader : Object { public static Shader Find(string name) => null; }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Texture mainTexture { get; set; }
        public void SetFloat(string name, float value) { }
    }

    public class Texture : Object
    {
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public static Texture2D whiteTexture => null;
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void Apply() { }
        public Color32[] GetPixels32() => null;
        public void SetPixels32(Color32[] colors) { }
    }
    public enum TextureFormat { RGBA32 = 4 }
    public static class ImageConversion { public static byte[] EncodeToPNG(this Texture2D tex) => null; }
    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType => default;
        public static string graphicsDeviceName => "";
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { }
        public bool useMipMap { get; set; }
        public static RenderTexture active { get; set; }
        public int width => 0;
        public int height => 0;
        public bool Create() => true;
        public void Release() { }
    }

    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum RenderTextureFormat { ARGB32 }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }

    public sealed class Camera : Behaviour
    {
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public float depth { get; set; }
        public int cullingMask { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public bool allowHDR { get; set; }
        public bool allowMSAA { get; set; }
        public bool useOcclusionCulling { get; set; }
        public RenderTexture targetTexture { get; set; }
        public static Camera[] allCameras => new Camera[0];
        public void Render() { }
    }

    public sealed class AudioListener : Behaviour { public static float volume { get; set; } }

    public sealed class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => null;
        public bool SetData(float[] data, int offsetSamples) => true;
    }

    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public bool loop { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public bool playOnAwake { get; set; }
        public void Play() { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }

    public static class Handheld { public static void Vibrate() { } }

    public class AndroidJavaObject : IDisposable
    {
        public AndroidJavaObject(string className, params object[] args) { }
        public void Call(string methodName, params object[] args) { }
        public ReturnType Call<ReturnType>(string methodName, params object[] args) => default;
        public FieldType GetStatic<FieldType>(string fieldName) => default;
        public ReturnType CallStatic<ReturnType>(string methodName, params object[] args) => default;
        public void Dispose() { }
    }
    public class AndroidJavaClass : AndroidJavaObject { public AndroidJavaClass(string className) : base(className) { } }

    public static class JsonUtility
    {
        public static string ToJson(object obj) => "";
        public static T FromJson<T>(string json) => default(T);
    }
    public class TextAsset : Object { public string text => ""; }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
    }

    public static class Random { public static float value => 0f; }

    public static class Application
    {
        public static int targetFrameRate { get; set; }
        public static bool isMobilePlatform => false;
        public static bool isBatchMode => false;
        public static bool isPlaying => false;
        public static string dataPath => "";
        public static string version => "";
        public static void Quit() { }
    }

    public static class QualitySettings { public static int vSyncCount { get; set; } }

    public static class SleepTimeout { public const int NeverSleep = -1; }

    public enum ScreenOrientation { Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5 }

    public static class Screen
    {
        public static int width => 1080;
        public static int height => 2400;
        public static Rect safeArea => default;
        public static int sleepTimeout { get; set; }
        public static ScreenOrientation orientation { get; set; }
    }

    public static class PlayerPrefs
    {
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetInt(string key, int value) { }
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
        public static void Save() { }
    }

    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch
    {
        public int fingerId { get; set; }
        public Vector2 position { get; set; }
        public TouchPhase phase { get; set; }
    }
    public enum KeyCode { Escape = 27 }

    public static class Input
    {
        public static bool touchSupported => false;
        public static int touchCount => 0;
        public static bool multiTouchEnabled { get; set; }
        public static Vector3 mousePosition => default;
        public static Touch GetTouch(int index) => default;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButtonUp(int button) => false;
        public static bool GetKeyDown(KeyCode key) => false;
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1 }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum BlendMode { Zero = 0, One = 1, DstColor = 2, SrcColor = 3, OneMinusDstColor = 4, SrcAlpha = 5, OneMinusSrcColor = 6, DstAlpha = 7, OneMinusDstAlpha = 8, SrcAlphaSaturate = 9, OneMinusSrcAlpha = 10 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum LightProbeUsage { Off = 0, BlendProbes = 1 }
    public enum ReflectionProbeUsage { Off, BlendProbes, BlendProbesAndSkybox, Simple }
    public enum GraphicsDeviceType { Null = 4, OpenGLCore = 17, Vulkan = 21 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { }
}

// ---------------------------------------------------------------- UnityEditor
namespace UnityEditor
{
    using UnityEngine;

    public class AssetImporter : Object { }
    public enum TextureImporterType { Default = 0 }
    public enum TextureImporterCompression { Uncompressed = 0, Compressed = 1 }
    public enum TextureImporterNPOTScale { None = 0 }
    public sealed class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public bool mipmapEnabled { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public TextureImporterNPOTScale npotScale { get; set; }
        public bool alphaIsTransparency { get; set; }
        public bool sRGBTexture { get; set; }
        public bool isReadable { get; set; }
        public int maxTextureSize { get; set; }
    }
    public class AssetPostprocessor
    {
        public string assetPath => "";
        public AssetImporter assetImporter => null;
    }

    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : System.Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : System.Attribute
    {
        public MenuItem(string itemName) { }
        public int priority;
    }

    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall;
        public static void Exit(int returnValue) { }
    }

    public sealed class SerializedProperty { public int intValue { get; set; } }
    public sealed class SerializedObject
    {
        public SerializedObject(Object obj) { }
        public SerializedProperty FindProperty(string propertyPath) => null;
        public bool ApplyModifiedProperties() => true;
    }
    public static class AssetDatabase
    {
        public static Object[] LoadAllAssetsAtPath(string assetPath) => null;
        public static void SaveAssets() { }
        public static void Refresh() { }
    }

    public static class EditorUtility { public static void RevealInFinder(string path) { } }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes { get; set; } }
    public enum AndroidBuildType { Debug = 0, Development = 1, Release = 2 }
    public enum AndroidSdkVersions { AndroidApiLevel26 = 26 }
    public static class EditorUserBuildSettings
    {
        public static bool buildAppBundle { get; set; }
        public static AndroidBuildType androidBuildType { get; set; }
    }

    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }
    public enum ManagedStrippingLevel { Disabled = 0, Low = 1, Medium = 2, High = 3, Minimal = 4 }
    [Flags] public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, X86_64 = 8 }
    public enum BuildTarget { Android = 13 }
    public enum BuildTargetGroup { Android = 7 }
    [Flags] public enum BuildOptions { None = 0 }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static string bundleVersion { get; set; }
        public static ColorSpace colorSpace { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static bool runInBackground { get; set; }
        public static void SetApplicationIdentifier(Build.NamedBuildTarget buildTarget, string identifier) { }
        public static void SetScriptingBackend(Build.NamedBuildTarget buildTarget, ScriptingImplementation backend) { }
        public static void SetManagedStrippingLevel(Build.NamedBuildTarget buildTarget, ManagedStrippingLevel level) { }
        public static class Android
        {
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static int bundleVersionCode { get; set; }
            public static bool renderOutsideSafeArea { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static bool useCustomKeystore { get; set; }
        }
    }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildOptions options { get; set; }
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions buildPlayerOptions) => null;
    }
}

namespace UnityEditor.Build
{
    public struct NamedBuildTarget { public static NamedBuildTarget Android => default; }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }
    public struct BuildSummary
    {
        public BuildResult result { get; }
        public ulong totalSize { get; }
        public int totalErrors { get; }
        public System.TimeSpan totalTime { get; }
    }
    public sealed class BuildReport : UnityEngine.Object { public BuildSummary summary => default; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) => true;
    }
}
