using UnityEngine;

namespace MemPalaceLLM
{
    public static class MemoryPalaceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Object.FindFirstObjectByType<MemoryPalaceExperimentController>() != null)
            {
                return;
            }

            var bootstrap = new GameObject("MemoryPalaceExperiment");
            Object.DontDestroyOnLoad(bootstrap);
            bootstrap.AddComponent<MemoryPalaceExperimentController>();
        }
    }
}
