using UnityEngine;

namespace MemPalaceLLM
{
    /// <summary>Checks mesh dependencies without instantiating or changing a prefab asset.</summary>
    public static class FurniturePrefabAvailability
    {
        public static bool IsUsable(GameObject prefab, out string reason)
        {
            if (prefab == null)
            {
                reason = "Prefab was not found.";
                return false;
            }

            var hasDrawableMesh = false;
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || renderer.forceRenderingOff || !IsActiveWithinPrefab(renderer.transform, prefab.transform))
                    continue;

                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    mesh = skinnedRenderer.sharedMesh;
                else if (renderer is MeshRenderer)
                    mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                else
                    continue;

                if (mesh == null)
                {
                    reason = "Missing mesh on " + renderer.name + ".";
                    return false;
                }

                var materials = renderer.sharedMaterials;
                if (materials.Length == 0)
                {
                    reason = "Missing materials on " + renderer.name + ".";
                    return false;
                }
                foreach (var material in materials)
                {
                    if (material != null) continue;
                    reason = "Missing material on " + renderer.name + ".";
                    return false;
                }

                hasDrawableMesh |= HasDrawableGeometry(mesh);
            }

            reason = hasDrawableMesh ? string.Empty : "Prefab has no active, enabled, drawable mesh.";
            return hasDrawableMesh;
        }

        private static bool IsActiveWithinPrefab(Transform candidate, Transform root)
        {
            // activeInHierarchy is not a reliable activation test for prefab assets.
            // Only the hierarchy that will be instantiated belongs to this check.
            for (var current = candidate; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static bool HasDrawableGeometry(Mesh mesh)
        {
            if (mesh.vertexCount == 0) return false;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                var indexCount = mesh.GetIndexCount(i);
                switch (mesh.GetTopology(i))
                {
                    case MeshTopology.Triangles: if (indexCount >= 3) return true; break;
                    case MeshTopology.Quads: if (indexCount >= 4) return true; break;
                    case MeshTopology.Lines:
                    case MeshTopology.LineStrip: if (indexCount >= 2) return true; break;
                    case MeshTopology.Points: if (indexCount >= 1) return true; break;
                }
            }
            return false;
        }
    }
}
