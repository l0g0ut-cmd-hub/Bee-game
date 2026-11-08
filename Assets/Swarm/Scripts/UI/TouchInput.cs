using UnityEngine;

namespace Swarm.UI
{
    /// <summary>
    /// До двух указателей. Первый палец ведёт рой (и UI), второй — отряд.
    /// В редакторе: левая кнопка мыши — первый палец, правая — второй.
    /// </summary>
    public sealed class TouchInput
    {
        public bool Down, Held, Up;
        public Vector2 ScreenPos;
        public bool Held2, Down2;
        public Vector2 ScreenPos2;
        int _finger = -1, _finger2 = -1;

        static bool UseTouch => Input.touchSupported && Application.isMobilePlatform;

        public void Poll()
        {
            Down = Up = Down2 = false;
            if (UseTouch)
            {
                bool found = false, found2 = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began)
                    {
                        if (_finger < 0) { _finger = t.fingerId; Down = true; }
                        else if (_finger2 < 0 && t.fingerId != _finger) { _finger2 = t.fingerId; Down2 = true; }
                    }
                    bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                    if (t.fingerId == _finger)
                    {
                        found = true;
                        ScreenPos = t.position;
                        if (ended) { Up = true; _finger = -1; }
                    }
                    else if (t.fingerId == _finger2)
                    {
                        found2 = true;
                        ScreenPos2 = t.position;
                        if (ended) _finger2 = -1;
                    }
                }
                if (!found && _finger >= 0) { Up = true; _finger = -1; }
                if (!found2) _finger2 = -1;
                Held = _finger >= 0 || Down;
                Held2 = _finger2 >= 0;
            }
            else
            {
                ScreenPos = Input.mousePosition;
                Down = Input.GetMouseButtonDown(0);
                Up = Input.GetMouseButtonUp(0);
                Held = Input.GetMouseButton(0);
                Down2 = Input.GetMouseButtonDown(1);
                Held2 = Input.GetMouseButton(1);
                ScreenPos2 = Input.mousePosition;
            }
        }

        public void Reset() { _finger = _finger2 = -1; Down = Up = Held = Held2 = Down2 = false; }
    }
}
