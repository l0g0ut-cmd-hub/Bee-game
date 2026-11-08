using UnityEngine;

namespace Swarm.Presentation
{
    public enum Sfx : byte { Click, Collect, Deposit, Hit, Kill, KillBig, BeeDie, Hatch, Upgrade, Alarm, Win, Lose, Windup, Heat, Count }

    /// <summary>
    /// Процедурный звук: все клипы синтезируются при старте (никаких аудиофайлов).
    /// Непрерывный гул роя меняет громкость и высоту от численности и скорости.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        const int Rate = 22050;
        AudioClip[] _clips;
        AudioSource[] _voices;
        AudioSource _buzz, _buzzLow;
        int _nextVoice;
        readonly float[] _lastPlayed = new float[(int)Sfx.Count];
        static readonly float[] MinInterval = { 0.03f, 0.07f, 0.12f, 0.06f, 0.05f, 0.05f, 0.08f, 0.1f, 0.1f, 0.5f, 0.5f, 0.5f, 0.15f, 0.12f };
        static readonly float[] Volume = { 0.5f, 0.18f, 0.35f, 0.22f, 0.45f, 0.6f, 0.3f, 0.4f, 0.5f, 0.45f, 0.6f, 0.6f, 0.35f, 0.25f };

        public bool Muted { get; private set; }

        void Awake()
        {
            Instance = this;
            Muted = PlayerPrefs.GetInt("swarm_muted", 0) == 1;
            _clips = new AudioClip[(int)Sfx.Count];
            _clips[(int)Sfx.Click] = Synth("click", 0.04f, (t, n) => Sq(t * 1200f) * Env(n, 0.002f, 0.04f) * 0.4f);
            _clips[(int)Sfx.Collect] = Synth("collect", 0.12f, (t, n) => Sin(t * Lerp(1100f, 1500f, n)) * Env(n, 0.005f, 0.12f) * 0.5f);
            _clips[(int)Sfx.Deposit] = Synth("deposit", 0.22f, (t, n) => Sin(t * Lerp(520f, 340f, n) + Sin(t * 30f) * 0.6f) * Env(n, 0.01f, 0.22f) * 0.7f);
            _clips[(int)Sfx.Hit] = Synth("hit", 0.07f, (t, n) => (Noise() * 0.6f + Sq(t * 180f) * 0.4f) * Env(n, 0.001f, 0.07f) * 0.6f);
            _clips[(int)Sfx.Kill] = Synth("kill", 0.25f, (t, n) => (Sin(t * Lerp(420f, 90f, n)) * 0.7f + Noise() * 0.3f * (1f - n)) * Env(n, 0.002f, 0.25f));
            _clips[(int)Sfx.KillBig] = Synth("killbig", 0.5f, (t, n) => (Sin(t * Lerp(260f, 50f, n)) * 0.7f + Noise() * 0.5f * (1f - n)) * Env(n, 0.002f, 0.5f));
            _clips[(int)Sfx.BeeDie] = Synth("beedie", 0.18f, (t, n) => Saw(t * Lerp(900f, 300f, n)) * Env(n, 0.003f, 0.18f) * 0.35f);
            _clips[(int)Sfx.Hatch] = Synth("hatch", 0.3f, (t, n) => Sin(t * Note(n < 0.33f ? 0 : n < 0.66f ? 4 : 7, 660f)) * Env(n, 0.01f, 0.3f) * 0.5f);
            _clips[(int)Sfx.Upgrade] = Synth("upgrade", 0.45f, (t, n) => (Sin(t * Note((int)(n * 4f) * 3, 523f)) + Sin(t * Note((int)(n * 4f) * 3, 1046f)) * 0.3f) * Env(n, 0.01f, 0.45f) * 0.45f);
            _clips[(int)Sfx.Alarm] = Synth("alarm", 0.7f, (t, n) => Sq(t * ((int)(n * 6f) % 2 == 0 ? 640f : 470f)) * Env(n, 0.01f, 0.7f) * 0.3f);
            _clips[(int)Sfx.Win] = Synth("win", 1.0f, (t, n) => Sin(t * Note(new[] { 0, 4, 7, 12, 12, 12 }[Mathf.Min(5, (int)(n * 6f))], 523f)) * Env(n, 0.01f, 1f) * 0.5f);
            _clips[(int)Sfx.Lose] = Synth("lose", 1.0f, (t, n) => Saw(t * Note(new[] { 7, 5, 3, 0, 0 }[Mathf.Min(4, (int)(n * 5f))], 262f)) * Env(n, 0.01f, 1f) * 0.3f);

            // телеграф рывка: нарастающий «вжж»; перегрев: шипение
            _clips[(int)Sfx.Windup] = Synth("windup", 0.35f, (t, n) => Saw(t * Lerp(180f, 520f, n * n)) * (0.3f + 0.7f * n) * (1f - Mathf.Max(0f, n - 0.85f) * 6f) * 0.4f);
            _clips[(int)Sfx.Heat] = Synth("heat", 0.25f, (t, n) => Noise() * (0.5f + 0.5f * Sin(t * 13f)) * Env(n, 0.03f, 0.25f) * 0.45f, lowpass: 0.9f);
            _voices = new AudioSource[8];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
            _buzz = MakeLoop("buzz", 1f, (t, n) =>
                (Saw(t * 220f) * 0.5f + Saw(t * 223f) * 0.4f + Saw(t * 446f) * 0.15f) * (0.75f + 0.25f * Sin(t * 31f)));
            _buzzLow = MakeLoop("buzzlow", 1f, (t, n) => (Saw(t * 110f) * 0.6f + Saw(t * 113f) * 0.4f) * (0.8f + 0.2f * Sin(t * 17f)));
            ApplyMute();
        }

        AudioSource MakeLoop(string name, float len, System.Func<float, float, float> f)
        {
            var clip = Synth(name, len, f, lowpass: 0.25f, normalizeEnds: false);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        public void ToggleMute()
        {
            Muted = !Muted;
            PlayerPrefs.SetInt("swarm_muted", Muted ? 1 : 0);
            PlayerPrefs.Save();
            ApplyMute();
        }

        void ApplyMute() => AudioListener.volume = Muted ? 0f : 1f;

        public void Play(Sfx s, float pitch = 1f)
        {
            int i = (int)s;
            float now = Time.unscaledTime;
            if (now - _lastPlayed[i] < MinInterval[i]) return;
            _lastPlayed[i] = now;
            var v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.pitch = pitch * (0.94f + Random.value * 0.12f);
            v.PlayOneShot(_clips[i], Volume[i]);
        }

        /// <summary>Гул роя: громкость от численности, высота от скорости, низкий гул шершней.</summary>
        public void SetSwarm(int bees, float speed01, int enemies, bool active)
        {
            float target = active ? Mathf.Clamp01(bees / 120f) * 0.22f + 0.04f : 0f;
            _buzz.volume = Mathf.MoveTowards(_buzz.volume, target, Time.unscaledDeltaTime * 0.6f);
            _buzz.pitch = 0.9f + speed01 * 0.3f;
            float low = active && enemies > 0 ? Mathf.Clamp01(enemies / 6f) * 0.18f + 0.05f : 0f;
            _buzzLow.volume = Mathf.MoveTowards(_buzzLow.volume, low, Time.unscaledDeltaTime * 0.6f);
        }

        // ------------------------------------------------------------ синтез
        static System.Random _noise = new System.Random(1);
        static float Noise() => (float)_noise.NextDouble() * 2f - 1f;
        static float Sin(float ph) => Mathf.Sin(ph * Mathf.PI * 2f);
        static float Sq(float ph) => (ph - Mathf.Floor(ph)) < 0.5f ? 1f : -1f;
        static float Saw(float ph) => (ph - Mathf.Floor(ph)) * 2f - 1f;
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Note(int semis, float baseHz) => baseHz * Mathf.Pow(2f, semis / 12f);
        static float Env(float n, float attack, float len)
        {
            float t = n * len;
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            return a * (1f - n) * (1f - n);
        }

        /// <summary>f(t, n): t — время в секундах (фаза в герцах передаётся как t*hz), n — 0..1 по длине клипа.</summary>
        static AudioClip Synth(string name, float len, System.Func<float, float, float> f, float lowpass = 0.6f, bool normalizeEnds = true)
        {
            int n = Mathf.Max(1, (int)(Rate * len));
            var data = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = f(t, i / (float)n);
                y += (x - y) * lowpass; // однополюсный ФНЧ смягчает пиксельную «пилу»
                data[i] = Mathf.Clamp(y, -1f, 1f);
            }
            if (normalizeEnds && n > 64)
                for (int i = 0; i < 64; i++) data[n - 1 - i] *= i / 64f;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
