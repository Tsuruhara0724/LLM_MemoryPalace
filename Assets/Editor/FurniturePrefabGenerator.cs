#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MemPalaceLLM
{
    [InitializeOnLoad]
    public static class FurniturePrefabGenerator
    {
        private const string PrefabFolder = "Assets/Resources/FurniturePrefabs";
        private const string MaterialFolder = PrefabFolder + "/GeneratedMaterials";
        private const string BathtubPrefabPath = PrefabFolder + "/bathtub.prefab";

        static FurniturePrefabGenerator()
        {
            EditorApplication.delayCall += EnsureDefaultFurniturePrefabs;
        }

        [MenuItem("MemPalace/Generate Furniture Prefabs")]
        public static void GenerateFurniturePrefabs()
        {
            EnsureBathtubPrefab(true);
        }

        private static void EnsureDefaultFurniturePrefabs()
        {
            EnsureBathtubPrefab(false);
        }

        private static void EnsureBathtubPrefab(bool force)
        {
            EnsureFolders();
            if (!force && File.Exists(BathtubPrefabPath))
            {
                return;
            }

            var shell = EnsureMaterial("bathtub_shell", new Color(0.86f, 0.91f, 0.94f));
            var inner = EnsureMaterial("bathtub_inner", new Color(0.96f, 0.98f, 0.99f));
            var water = EnsureMaterial("bathtub_water", new Color(0.45f, 0.68f, 0.88f, 0.82f));
            var metal = EnsureMaterial("bathtub_metal", new Color(0.62f, 0.66f, 0.68f));

            var root = new GameObject("bathtub");
            try
            {
                AddBox(root.transform, "TubOuter", new Vector3(0f, 0.28f, 0f), new Vector3(1.05f, 0.45f, 0.62f), shell);
                AddBox(root.transform, "InnerBasin", new Vector3(0f, 0.52f, 0f), new Vector3(0.78f, 0.08f, 0.38f), inner);
                AddBox(root.transform, "WaterPlane", new Vector3(0f, 0.575f, 0f), new Vector3(0.68f, 0.025f, 0.30f), water);
                AddBox(root.transform, "RimLeft", new Vector3(-0.56f, 0.58f, 0f), new Vector3(0.08f, 0.11f, 0.68f), shell);
                AddBox(root.transform, "RimRight", new Vector3(0.56f, 0.58f, 0f), new Vector3(0.08f, 0.11f, 0.68f), shell);
                AddBox(root.transform, "RimBack", new Vector3(0f, 0.58f, 0.36f), new Vector3(1.12f, 0.11f, 0.08f), shell);
                AddBox(root.transform, "RimFront", new Vector3(0f, 0.58f, -0.36f), new Vector3(1.12f, 0.11f, 0.08f), shell);
                AddBox(root.transform, "BaseShadow", new Vector3(0f, 0.04f, 0f), new Vector3(0.86f, 0.08f, 0.48f), inner);
                AddBox(root.transform, "FaucetStem", new Vector3(-0.34f, 0.78f, 0.26f), new Vector3(0.05f, 0.30f, 0.05f), metal);
                AddBox(root.transform, "FaucetHead", new Vector3(-0.22f, 0.91f, 0.20f), new Vector3(0.25f, 0.05f, 0.08f), metal);

                PrefabUtility.SaveAsPrefabAsset(root, BathtubPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "FurniturePrefabs");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder(PrefabFolder, "GeneratedMaterials");
            }
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader)
            {
                name = name,
                color = color
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AddBox(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
#endif
