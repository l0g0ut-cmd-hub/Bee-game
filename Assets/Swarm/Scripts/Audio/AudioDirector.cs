using UnityEngine;

namespace Swarm.Presentation
{
    public enum Sfx : byte { Click, Collect, Deposit, Hit, Kill, KillBig, BeeDie, Hatch, Upgrade, Alarm, Win, Lose, Windup, Heat, Bird, Swipe, Sprout, Night, Count }

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
        AudioSource _buzz, _buzzLow, _musicCalm, _musicThreat, _rain;
        int _nextVoice;
        readonly float[] _lastPlayed = new float[(int)Sfx.Count];
        static readonly float[] MinInterval = { 0.03f, 0.07f, 0.12f, 0.06f, 0.05f, 0.05f, 0.08f, 0.1f, 0.1f, 0.5f, 0.5f, 0.5f, 0.15f, 0.12f, 0.5f, 0.12f, 0.15f, 1f };
        static readonly float[] Volume = { 0.5f, 0.18f, 0.35f, 0.22f, 0.45f, 0.6f, 0.3f, 0.4f, 0.5f, 0.45f, 0.6f, 0.6f, 0.35f, 0.25f, 0.4f, 0.5f, 0.3f, 0.35f };

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
            // щурка: два нисходящих свиста; лапа медведя: глухой удар; росток: колокольчик; ночь: низкий колокол
            _clips[(int)Sfx.Bird] = Synth("bird", 0.6f, (t, n) => Sin(t * Lerp(2400f, 1500f, Mathf.Repeat(n * 2f, 1f))) * Env(Mathf.Repeat(n * 2f, 1f), 0.01f, 0.3f) * 0.35f);
            _clips[(int)Sfx.Swipe] = Synth("swipe", 0.3f, (t, n) => (Noise() * (1f - n) * 0.6f + Sin(t * Lerp(140f, 60f, n)) * 0.6f) * Env(n, 0.005f, 0.3f));
            _clips[(int)Sfx.Sprout] = Synth("sprout", 0.35f, (t, n) => (Sin(t * Note((int)(n * 3f) * 5, 880f)) * 0.6f + Sin(t * 1760f) * 0.15f) * Env(n, 0.01f, 0.35f) * 0.5f);
            _clips[(int)Sfx.Night] = Synth("night", 1.4f, (t, n) => (Sin(t * 196f) * 0.6f + Sin(t * 294f) * 0.3f + Sin(t * 392f) * 0.15f) * Env(n, 0.02f, 1.4f) * 0.5f);
            // звуки интерфейса Kenney Interface Sounds (CC0); если файлов нет — остаётся синтез
            var uiClick = Resources.Load<AudioClip>("Audio/ui_click");
            if (uiClick != null) _clips[(int)Sfx.Click] = uiClick;
            var uiBuy = Resources.Load<AudioClip>("Audio/ui_buy");
            if (uiBuy != null) _clips[(int)Sfx.Upgrade] = uiBuy;
            _voices = new AudioSource[8];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
            _buzz = MakeLoop("buzz", 1f, (t, n) =>
                (Saw(t * 220f) * 0.5f + Saw(t * 223f) * 0.4f + Saw(t * 446f) * 0.15f) * (0.75f + 0.25f * Sin(t * 31f)));
            _buzzLow = MakeLoop("buzzlow", 1f, (t, n) => (Saw(t * 110f) * 0.6f + Saw(t * 113f) * 0.4f) * (0.8f + 0.2f * Sin(t * 17f)));
            _musicCalm = MakeMusic("music_calm", CalmLoop());
            _musicThreat = MakeMusic("music_threat", ThreatLoop());
            _rain = MakeLoop("rain", 2f, (t, n) => Noise() * 0.5f * (0.85f + 0.15f * Sin(t * 3f)));
            ApplyMute();
        }

        // ------------------------------------------------------------ адаптивная музыка
        const float Bpm = 112f;
        static readonly int[][] Chords = { new[] { 0, 4, 7 }, new[] { -3, 0, 4 }, new[] { -7, -3, 0 }, new[] { -5, -1, 2 } }; // C Am F G
        float _threatLevel;

        AudioSource MakeMusic(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            src.Play();
            return src;
        }

        /// <summary>Спокойный слой: мягкое арпеджио восьмыми + низкий пэд, 4 такта.</summary>
        static float[] CalmLoop()
        {
            float beat = 60f / Bpm;
            int n = (int)(Rate * beat * 16f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int bar = Mathf.Min(3, (int)(t / (beat * 4f)));
                int[] ch = Chords[bar];
                float eighth = beat * 0.5f;
                int step = (int)(t / eighth);
                float lt = t - step * eighth;
                int note = ch[step % 3] + ((step / 3) % 2 == 0 ? 12 : 0);
                float hz = 261.63f * Mathf.Pow(2f, note / 12f);
                float pluck = (Mathf.Sin(t * hz * Mathf.PI * 2f) * 0.7f + Mathf.Sin(t * hz * 2f * Mathf.PI * 2f) * 0.15f) * Mathf.Exp(-lt * 6f);
                float pad = 0f;
                for (int k = 0; k < 3; k++) pad += Mathf.Sin(t * 130.81f * Mathf.Pow(2f, ch[k] / 12f) * Mathf.PI * 2f);
                d[i] = pluck * 0.32f + pad * 0.05f;
            }
            Smooth(d);
            return d;
        }

        /// <summary>Слой угрозы: бочка на доли, хэт на слабые, бас-остинато в миноре — той же длины, синхронно.</summary>
        static float[] ThreatLoop()
        {
            float beat = 60f / Bpm;
            int n = (int)(Rate * beat * 16f);
            var d = new float[n];
            var rnd = new System.Random(3);
            int[] bass = { -3, -3, 0, -3, -5, -5, -7, -5 };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float bt = t % beat;
                float kick = Mathf.Sin(bt * Mathf.Lerp(150f, 45f, Mathf.Clamp01(bt * 8f)) * Mathf.PI * 2f) * Mathf.Exp(-bt * 14f);
                float off = (t + beat * 0.5f) % beat;
                float hat = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-off * 60f) * 0.35f;
                int bstep = (int)(t / (beat * 0.5f)) % bass.Length;
                float bhz = 65.41f * Mathf.Pow(2f, bass[bstep] / 12f);
                float ph = t * bhz;
                float saw = (ph - Mathf.Floor(ph)) * 2f - 1f;
                d[i] = kick * 0.55f + hat + saw * 0.16f;
            }
            Smooth(d);
            return d;
        }

        static void Smooth(float[] d)
        {
            float y = 0f;
            for (int i = 0; i < d.Length; i++) { y += (d[i] - y) * 0.35f; d[i] = Mathf.Clamp(y, -1f, 1f); }
        }

        /// <summary>threat01 — насколько опасно рядом с роем; night — приглушить; rain01 — шум дождя.</summary>
        public void SetMusic(float threat01, bool night, float rain01, bool menu)
        {
            float k = Time.unscaledDeltaTime;
            _threatLevel = Mathf.MoveTowards(_threatLevel, threat01, k * (threat01 > _threatLevel ? 0.8f : 0.25f));
            float calm = menu ? 0.2f : (night ? 0.1f : 0.15f);
            _musicCalm.volume = Mathf.MoveTowards(_musicCalm.volume, calm * (1f - 0.4f * _threatLevel), k * 0.3f);
            _musicThreat.volume = Mathf.MoveTowards(_musicThreat.volume, menu ? 0f : _threatLevel * 0.24f, k * 0.5f);
            _rain.volume = Mathf.MoveTowards(_rain.volume, rain01 * 0.13f, k * 0.2f);
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
