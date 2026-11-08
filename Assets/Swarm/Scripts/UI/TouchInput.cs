using UnityEngine;

namespace Swarm.UI
{
    /// <summary>Один указатель: палец на устройстве, мышь в редакторе. Отслеживает именно тот палец, что коснулся первым.</summary>
    public sealed class TouchInput
    {
        public bool Down, Held, Up;
        public Vector2 ScreenPos;
        int _finger = -1;

        static bool UseTouch => Input.touchSupported && Application.isMobilePlatform;

        public void Poll()
        {
            Down = Up = false;
            if (UseTouch)
            {
                bool found = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (_finger < 0 && t.phase == TouchPhase.Began)
                    {
                        _finger = t.fingerId;
                        Down = true;
                    }
                    if (t.fingerId != _finger) continue;
                    found = true;
                    ScreenPos = t.position;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    {
                        Up = true;
                        _finger = -1;
                    }
                }
                if (!found && _finger >= 0) { Up = true; _finger = -1; }
                Held = _finger >= 0 || Down;
            }
            else
            {
                ScreenPos = Input.mousePosition;
                Down = Input.GetMouseButtonDown(0);
                Up = Input.GetMouseButtonUp(0);
                Held = Input.GetMouseButton(0);
            }
        }

        public void Reset() { _finger = -1; Down = Up = Held = false; }
    }
}
