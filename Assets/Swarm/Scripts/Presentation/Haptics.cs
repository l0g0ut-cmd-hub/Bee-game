using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>
    /// Короткий вибро-отклик. На Android 8+ — VibrationEffect с амплитудой (тонкий «тик»),
    /// ниже — классический vibrate(ms). Частота ограничена, чтобы бой не превращался в зуммер.
    /// </summary>
    public static class Haptics
    {
        public static bool Enabled = true;
        static float _last = -10f;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static int _sdk;
        static bool _init, _broken;

        static void Init()
        {
            _init = true;
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = act.Call<AndroidJavaObject>("getSystemService", "vibrator");
                using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdk = ver.GetStatic<int>("SDK_INT");
            }
            catch { _broken = true; }
        }
#endif

        public static void Load() => Enabled = PlayerPrefs.GetInt("swarm_haptics", 1) == 1;

        public static void Toggle()
        {
            Enabled = !Enabled;
            PlayerPrefs.SetInt("swarm_haptics", Enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (Enabled) Pulse(30, 200, 0f);
        }

        /// <summary>ms — длительность, amplitude 1..255.</summary>
        public static void Pulse(int ms, int amplitude, float minInterval = 0.07f)
        {
            if (!Enabled) return;
            float now = Time.unscaledTime;
            if (now - _last < minInterval) return;
            _last = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_init) Init();
            if (_broken || _vibrator == null) { Handheld.Vibrate(); return; }
            try
            {
                if (_sdk >= 26)
                {
                    using (var ve = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var eff = ve.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amplitude, 1, 255)))
                        _vibrator.Call("vibrate", eff);
                }
                else _vibrator.Call("vibrate", (long)ms);
            }
            catch { _broken = true; }
#endif
        }
    }
}
