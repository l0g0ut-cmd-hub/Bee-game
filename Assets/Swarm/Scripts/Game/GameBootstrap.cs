using UnityEngine;

namespace Swarm.Game
{
    /// <summary>Запускает игру в любой сцене: достаточно нажать Play или собрать билд с пустой сценой Main.</summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (GameRoot.Instance != null) return;
            // убираем камеры из сцены (например, дефолтную Main Camera) — рендер полностью свой
            foreach (var cam in Camera.allCameras) cam.gameObject.SetActive(false);
            new GameObject("SWARM").AddComponent<GameRoot>();
        }
    }
}
