using Swarm.Presentation;
using UnityEngine;

namespace Swarm.UI
{
    /// <summary>
    /// Immediate-mode UI в пикселях RT. Рисуется в отдельный QuadBatch на слое UI.
    /// Нажатие, начатое на кнопке или блокирующей панели, не управляет роем до отпускания.
    /// </summary>
    public sealed class PixelUI
    {
        public readonly QuadBatch Batch;
        public readonly TextBuf Buf = new TextBuf();
        PixelScreen _screen;

        public Vector2 Pointer;
        public bool Down, Held, Up;
        int _active;
        bool _captured;
        bool _clickedThisFrame;

        public int W => _screen.Width;
        public int H => _screen.Height;
        public float SafeTop => _screen.SafeTop;
        public float SafeBottom => _screen.SafeBottom;

        /// <summary>Указатель занят интерфейсом — рой его не получает.</summary>
        public bool Captured => _captured;
        public bool Clicked => _clickedThisFrame;

        public static readonly Color32 White = new Color32(255, 250, 240, 255);
        public static readonly Color32 Honey = new Color32(247, 207, 85, 255);
        public static readonly Color32 HoneyDark = new Color32(201, 122, 31, 255);
        public static readonly Color32 Muted = new Color32(196, 186, 168, 255);
        public static readonly Color32 Red = new Color32(238, 106, 82, 255);
        public static readonly Color32 Green = new Color32(168, 221, 115, 255);
        public static readonly Color32 Ink = new Color32(23, 18, 28, 255);
        public static readonly Color32 InkSoft = new Color32(23, 18, 28, 170);

        public PixelUI(Transform root)
        {
            Batch = new QuadBatch("UI", 2600, SpriteAtlas.Alpha, 0, PixelScreen.LayerUI, root);
            Batch.Go.transform.position = new Vector3(PixelScreen.UiOrigin.x, PixelScreen.UiOrigin.y, 0f);
        }

        public void Begin(PixelScreen screen, TouchInput input)
        {
            _screen = screen;
            Pointer = screen.ScreenToPixel(input.ScreenPos);
            Down = input.Down;
            Held = input.Held;
            Up = input.Up;
            _clickedThisFrame = false;
            Batch.Begin();
        }

        public void End()
        {
            Batch.End();
            if (Up || !Held) { _active = 0; _captured = false; }
        }

        bool Hit(Rect r) => r.Contains(Pointer);

        /// <summary>Область, нажатие в которой «съедается» интерфейсом (панели, модальные экраны).</summary>
        public void Blocker(Rect r)
        {
            if (Down && Hit(r)) _captured = true;
        }

        public bool Button(int id, Rect r, string label, bool enabled = true, int scale = 1, Spr icon = Spr.Count, Color32? textColor = null)
        {
            bool hover = Hit(r);
            if (Down && hover)
            {
                _captured = true;
                if (enabled) _active = id;
            }
            bool pressed = _active == id && Held && hover;
            bool clicked = Up && _active == id && hover && enabled;
            if (clicked)
            {
                _clickedThisFrame = true;
                AudioDirector.Instance?.Play(Sfx.Click);
            }

            Spr skin = !enabled ? Spr.ButtonOff : (pressed ? Spr.ButtonDown : Spr.Button);
            NineSlice((int)skin, r, 3);
            float dy = pressed ? -1f : 0f;
            Color32 tc = textColor ?? (enabled ? Ink : new Color32(60, 60, 70, 255));
            int tw = label != null ? PixelFont.Measure(label, scale) : 0;
            float cx = r.center.x;
            float ty = Mathf.Round(r.center.y - PixelFont.LineHeight * scale * 0.5f + dy);
            if (icon != Spr.Count)
            {
                int iw = SpriteAtlas.W(icon);
                float total = iw + (tw > 0 ? tw + 3 : 0);
                float x0 = Mathf.Round(cx - total * 0.5f);
                Batch.Add(icon, x0 + iw * 0.5f, Mathf.Round(r.center.y + dy), new Color32(255, 255, 255, (byte)(enabled ? 255 : 140)));
                if (label != null) PixelFont.Draw(Batch, label, x0 + iw + 3, ty, tc, scale, 0, false);
            }
            else if (label != null)
            {
                PixelFont.Draw(Batch, label, cx, ty, tc, scale, 1, false);
            }
            return clicked;
        }

        /// <summary>Кнопка с текстом из буфера (числа без аллокаций).</summary>
        public bool ButtonBuf(int id, Rect r, TextBuf text, bool enabled, Spr icon)
        {
            bool clicked = Button(id, r, null, enabled, 1, Spr.Count);
            bool pressed = _active == id && Held && Hit(r);
            float dy = pressed ? -1f : 0f;
            int tw = PixelFont.Measure(text);
            int iw = icon != Spr.Count ? SpriteAtlas.W(icon) + 2 : 0;
            float x0 = Mathf.Round(r.center.x - (tw + iw) * 0.5f);
            float ty = Mathf.Round(r.center.y - 3.5f + dy);
            PixelFont.Draw(Batch, text, x0, ty, enabled ? Ink : new Color32(60, 60, 70, 255), 1, 0, false);
            if (icon != Spr.Count) Batch.Add(icon, x0 + tw + 2 + SpriteAtlas.W(icon) * 0.5f, Mathf.Round(r.center.y + dy), new Color32(255, 255, 255, (byte)(enabled ? 255 : 140)));
            return clicked;
        }

        public void Panel(Rect r, bool dark = false) => NineSlice(dark ? (int)Spr.PanelDark : (int)Spr.Panel, r, 3);

        public void Fill(Rect r, Color32 c) => Batch.Rect(r.x, r.y, r.width, r.height, c);

        public void Bar(Rect r, float t, Color32 fill, Color32 back)
        {
            Batch.Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2, Ink);
            Batch.Rect(r.x, r.y, r.width, r.height, back);
            float w = Mathf.Round(r.width * Mathf.Clamp01(t));
            if (w > 0f)
            {
                Batch.Rect(r.x, r.y, w, r.height, fill);
                Batch.Rect(r.x, r.y + r.height - 1, w, 1, new Color32(255, 255, 255, 70));
            }
        }

        public void Text(string s, float x, float y, Color32 c, int scale = 1, int align = 0, bool shadow = true) =>
            PixelFont.Draw(Batch, s, x, y, c, scale, align, shadow);

        public void Text(TextBuf t, float x, float y, Color32 c, int scale = 1, int align = 0, bool shadow = true) =>
            PixelFont.Draw(Batch, t, x, y, c, scale, align, shadow);

        public void Icon(Spr s, float cx, float cy, Color32? tint = null, int scale = 1) =>
            Batch.Add(s, cx, cy, tint ?? new Color32(255, 255, 255, 255), false, scale);

        public void NineSlice(int spr, Rect r, int b)
        {
            int sw = SpriteAtlas.W(spr), sh = SpriteAtlas.H(spr);
            int cw = sw - 2 * b, ch = sh - 2 * b;
            float x0 = Mathf.Round(r.x), y0 = Mathf.Round(r.y), w = Mathf.Round(r.width), h = Mathf.Round(r.height);
            float mw = w - 2 * b, mh = h - 2 * b;
            var c = new Color32(255, 255, 255, 255);
            // низ
            Batch.AddSub(spr, 0, 0, b, b, x0, y0, b, b, c);
            Batch.AddSub(spr, b, 0, cw, b, x0 + b, y0, mw, b, c);
            Batch.AddSub(spr, sw - b, 0, b, b, x0 + w - b, y0, b, b, c);
            // середина
            Batch.AddSub(spr, 0, b, b, ch, x0, y0 + b, b, mh, c);
            Batch.AddSub(spr, b, b, cw, ch, x0 + b, y0 + b, mw, mh, c);
            Batch.AddSub(spr, sw - b, b, b, ch, x0 + w - b, y0 + b, b, mh, c);
            // верх
            Batch.AddSub(spr, 0, sh - b, b, b, x0, y0 + h - b, b, b, c);
            Batch.AddSub(spr, b, sh - b, cw, b, x0 + b, y0 + h - b, mw, b, c);
            Batch.AddSub(spr, sw - b, sh - b, b, b, x0 + w - b, y0 + h - b, b, b, c);
        }

        public void Rotated(Spr s, float x, float y, float angle, Color32 c) => Batch.AddRotated((int)s, x, y, angle, c);
    }
}
