using System.Collections.Generic;
using UnityEngine;

namespace Swarm.Presentation
{
    /// <summary>Пиксельный шрифт 5x7 из атласа (заглавные кириллица/латиница). Строчные приводятся к верхнему регистру.</summary>
    public static class PixelFont
    {
        static readonly Dictionary<char, int> _map = new Dictionary<char, int>(128);
        static bool _init;
        public const int LineHeight = 7;
        public const int Spacing = 1;

        static void Init()
        {
            if (_init) return;
            _init = true;
            string chars = AtlasData.FontChars;
            for (int i = 0; i < chars.Length; i++) _map[chars[i]] = AtlasData.GlyphStart + i;
        }

        static int Glyph(char c)
        {
            Init();
            if (_map.TryGetValue(c, out int g)) return g;
            char u = char.ToUpperInvariant(c);
            if (_map.TryGetValue(u, out g)) return g;
            if (c == 'ё') return _map.TryGetValue('Ё', out g) ? g : -1;
            return _map.TryGetValue('?', out g) ? g : -1;
        }

        static int Advance(char c)
        {
            int g = Glyph(c);
            return g < 0 ? 4 : SpriteAtlas.W(g) + Spacing;
        }

        public static int Measure(string s, int scale = 1)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            for (int i = 0; i < s.Length; i++) w += Advance(s[i]);
            return (w - Spacing) * scale;
        }

        public static int Measure(TextBuf t, int scale = 1)
        {
            if (t.Length == 0) return 0;
            int w = 0;
            for (int i = 0; i < t.Length; i++) w += Advance(t.Chars[i]);
            return (w - Spacing) * scale;
        }

        /// <summary>x,y — левый нижний угол строки. align: 0 = левый край, 1 = центр, 2 = правый.</summary>
        public static void Draw(QuadBatch b, string s, float x, float y, Color32 col, int scale = 1, int align = 0, bool shadow = true)
        {
            if (string.IsNullOrEmpty(s)) return;
            int w = Measure(s, scale);
            float x0 = Mathf.Round(align == 1 ? x - w * 0.5f : align == 2 ? x - w : x);
            if (shadow) Emit(b, s, null, s.Length, x0, y - scale, Shade(col), scale);
            Emit(b, s, null, s.Length, x0, y, col, scale);
        }

        public static void Draw(QuadBatch b, TextBuf t, float x, float y, Color32 col, int scale = 1, int align = 0, bool shadow = true)
        {
            if (t.Length == 0) return;
            int w = Measure(t, scale);
            float x0 = Mathf.Round(align == 1 ? x - w * 0.5f : align == 2 ? x - w : x);
            if (shadow) Emit(b, null, t.Chars, t.Length, x0, y - scale, Shade(col), scale);
            Emit(b, null, t.Chars, t.Length, x0, y, col, scale);
        }

        static Color32 Shade(Color32 c) => new Color32(23, 18, 28, (byte)(c.a * 0.85f));

        static void Emit(QuadBatch b, string s, char[] chars, int len, float x, float y, Color32 col, int scale)
        {
            for (int i = 0; i < len; i++)
            {
                char c = s != null ? s[i] : chars[i];
                int g = Glyph(c);
                if (g < 0) { x += 4 * scale; continue; }
                if (c != ' ') b.Add(g, x, y, col, false, scale);
                x += (SpriteAtlas.W(g) + Spacing) * scale;
            }
        }
    }

    /// <summary>Переиспользуемый буфер строки: числа и склейка без аллокаций в кадре.</summary>
    public sealed class TextBuf
    {
        public readonly char[] Chars = new char[96];
        public int Length;

        public TextBuf Clear() { Length = 0; return this; }

        public TextBuf Append(string s)
        {
            if (s == null) return this;
            for (int i = 0; i < s.Length && Length < Chars.Length; i++) Chars[Length++] = s[i];
            return this;
        }

        public TextBuf Append(char c)
        {
            if (Length < Chars.Length) Chars[Length++] = c;
            return this;
        }

        public TextBuf Append(int v)
        {
            if (v < 0) { Append('-'); v = -v; }
            int start = Length;
            do
            {
                if (Length >= Chars.Length) break;
                Chars[Length++] = (char)('0' + v % 10);
                v /= 10;
            } while (v > 0);
            System.Array.Reverse(Chars, start, Length - start);
            return this;
        }

        /// <summary>Число с фиксированным количеством знаков после запятой (1.15).</summary>
        public TextBuf AppendDecimal(float v, int decimals)
        {
            if (v < 0f) { Append('-'); v = -v; }
            int mul = decimals == 1 ? 10 : decimals == 2 ? 100 : 1;
            int iv = Mathf.RoundToInt(v * mul);
            Append(iv / mul);
            if (decimals > 0)
            {
                int frac = iv % mul;
                // убираем хвостовые нули: 1.50 -> 1.5, 2.00 -> 2
                while (decimals > 0 && frac % 10 == 0 && frac != 0) { frac /= 10; decimals--; }
                if (frac != 0)
                {
                    Append('.');
                    if (decimals == 2 && frac < 10) Append('0');
                    Append(frac);
                }
            }
            return this;
        }

        /// <summary>Время как M:SS.</summary>
        public TextBuf AppendTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            Append(s / 60);
            Append(':');
            int r = s % 60;
            if (r < 10) Append('0');
            return Append(r);
        }
    }
}
