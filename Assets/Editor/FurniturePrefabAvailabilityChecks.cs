using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MemPalaceLLM.Editor
{
    public static class FurniturePrefabAvailabilityChecks
    {
        private static int checks;

        [MenuItem("Tools/Memory Palace/Check Furniture Prefab Fallback")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run these isolated checks outside Play Mode.");
            checks = 0;
            var root = new GameObject("Furniture availability check") { hideFlags = HideFlags.HideAndDontSave };
            Mesh emptyMesh = null;
            try
            {
                Expect(!FurniturePrefabAvailability.IsUsable(null, out _), "A missing prefab must use the fallback.");
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "An empty prefab must use the fallback.");
                var visible = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visible.transform.SetParent(root.transform, false);
                var meshFilter = visible.GetComponent<MeshFilter>();
                var meshRenderer = visible.GetComponent<MeshRenderer>();
                var originalMesh = meshFilter.sharedMesh;
                var originalMaterial = meshRenderer.sharedMaterial;
                var originalScale = root.transform.localScale = new Vector3(0.385f, 0.385f, 0.385f);
                var originalRotation = root.transform.localRotation = Quaternion.Euler(0, 90, 0);

                Expect(FurniturePrefabAvailability.IsUsable(root, out _), "A complete mesh must remain usable.");
                Expect(root.transform.localScale == originalScale && root.transform.localRotation == originalRotation &&
                       meshFilter.sharedMesh == originalMesh && meshRenderer.sharedMaterial == originalMaterial,
                    "Inspection must preserve authored transforms and shared assets.");

                meshFilter.sharedMesh = null;
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "A missing active mesh must use the fallback.");
                meshFilter.sharedMesh = originalMesh;
                meshRenderer.sharedMaterials = Array.Empty<Material>();
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "An empty material array must use the fallback.");
                meshRenderer.sharedMaterials = new[] { originalMaterial, null };
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "Any missing active material slot must use the fallback.");
                meshRenderer.sharedMaterial = originalMaterial;
                meshRenderer.sharedMaterials = new[] { originalMaterial };

                var broken = new GameObject("Optional broken mesh", typeof(MeshFilter), typeof(MeshRenderer));
                broken.transform.SetParent(root.transform, false);
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "A second active broken renderer must not be hidden by a valid one.");
                broken.SetActive(false);
                Expect(FurniturePrefabAvailability.IsUsable(root, out _), "An inactive optional renderer must not invalidate a complete prefab.");
                broken.SetActive(true);
                broken.GetComponent<MeshRenderer>().enabled = false;
                Expect(FurniturePrefabAvailability.IsUsable(root, out _), "A disabled optional renderer must not invalidate a complete prefab.");
                broken.GetComponent<MeshRenderer>().enabled = true;
                broken.GetComponent<MeshRenderer>().forceRenderingOff = true;
                Expect(FurniturePrefabAvailability.IsUsable(root, out _), "A forced-off renderer must not invalidate a complete prefab.");

                visible.SetActive(false);
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "At least one active drawable mesh is required.");
                visible.SetActive(true);
                emptyMesh = new Mesh();
                meshFilter.sharedMesh = emptyMesh;
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "An empty mesh is not drawable.");
                meshFilter.sharedMesh = originalMesh;

                meshRenderer.enabled = false;
                var skinnedRoot = new GameObject("Skinned furniture", typeof(SkinnedMeshRenderer));
                skinnedRoot.transform.SetParent(root.transform, false);
                var skinned = skinnedRoot.GetComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = originalMesh;
                skinned.sharedMaterial = originalMaterial;
                Expect(FurniturePrefabAvailability.IsUsable(root, out _), "A complete skinned mesh must remain usable.");
                skinned.sharedMesh = null;
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "A missing skinned mesh must use the fallback.");
                skinned.sharedMesh = originalMesh;
                root.SetActive(false);
                Expect(!FurniturePrefabAvailability.IsUsable(root, out _), "An inactive prefab root has no drawable mesh.");

                CheckMissingModelKeys();
                CheckBlankWordImage();
                Debug.Log("FURNITURE PREFAB AVAILABILITY CHECK PASSED: " + checks +
                          " isolated EditMode checks; no scene or asset files modified. Procedural furniture creation is not executed in EditMode.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (emptyMesh != null) UnityEngine.Object.DestroyImmediate(emptyMesh);
            }
        }

        private static void CheckMissingModelKeys()
        {
            var method = typeof(MemoryPalaceExperimentController).GetMethod(
                "TryCreateFurniturePrefabModel", BindingFlags.Instance | BindingFlags.NonPublic);
            Expect(method != null, "The controller prefab selection entry point must exist.");
            var roomField = typeof(RoomSpecCatalog).GetField("currentRoom", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The room catalog state field was not found.");
            var originalRoom = roomField.GetValue(null);
            var root = new GameObject("Missing furniture model checks") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                // Keep the controller inactive so no runtime initialization, XR or services can start.
                var controller = root.AddComponent<MemoryPalaceExperimentController>();
                controller.enabled = false;
                var modelRoot = new GameObject("Unchanged model parent") { hideFlags = HideFlags.HideAndDontSave };
                modelRoot.transform.SetParent(root.transform, false);
                var marker = new GameObject("Existing child") { hideFlags = HideFlags.HideAndDontSave };
                marker.transform.SetParent(modelRoot.transform, false);
                var position = modelRoot.transform.localPosition = new Vector3(0.3f, 0.4f, 0.5f);
                var rotation = modelRoot.transform.localRotation = Quaternion.Euler(0f, 27f, 0f);
                var scale = modelRoot.transform.localScale = new Vector3(0.7f, 0.8f, 0.9f);
                var labels = new[]
                {
                    "Door", "Bed", "Wardrobe", "Desk", "Chair", "Bookshelf", "Bathtub",
                    "Sofa", "Television", "Air Conditioner", "Window", "Lamp", "Plant", "Toilet"
                };
                for (var i = 0; i < labels.Length; i++)
                {
                    // A unique explicit key prevents resolution to any commercial furniture resource.
                    var missingKey = "furniture_check_missing_b7f4e579d4ca45eca54f3e2fae4fe04d_" + i;
                    var anchor = new AnchorDefinition
                    {
                        id = "availability_check_" + i,
                        label = labels[i],
                        modelKey = missingKey,
                        primitiveShape = "Cube",
                        colorHex = "#7A5A3B",
                        scale = Vector3.one
                    };
                    Expect(Resources.Load<GameObject>("FurniturePrefabs/" + missingKey) == null,
                        labels[i] + ": the sentinel resource must not exist.");
                    Expect(!(bool)method.Invoke(controller, new object[] { anchor, modelRoot.transform, false, -1 }),
                        labels[i] + ": a missing explicit prefab must select the procedural fallback.");
                    Expect(modelRoot.transform.childCount == 1 && modelRoot.transform.GetChild(0) == marker.transform,
                        labels[i] + ": a missing prefab must not instantiate or remove children.");
                    Expect(modelRoot.transform.parent == root.transform && modelRoot.transform.localPosition == position &&
                           modelRoot.transform.localRotation == rotation && modelRoot.transform.localScale == scale,
                        labels[i] + ": prefab rejection must preserve the model parent and its authored transform.");
                }
                Expect(ReferenceEquals(roomField.GetValue(null), originalRoom), "Prefab selection must not change the room catalog.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                roomField.SetValue(null, originalRoom);
            }
        }

        private static void CheckBlankWordImage()
        {
            Texture2D blank = null;
            try
            {
                blank = WordImageCatalog.CreateBlankPlaceholder();
                blank.hideFlags = HideFlags.HideAndDontSave;
                Expect(blank.width == 512 && blank.height == 384, "The programmatic word-image placeholder must use its standard dimensions.");
                Expect(blank.format == TextureFormat.RGBA32 && blank.isReadable && blank.mipmapCount == 1,
                    "The programmatic placeholder must be a readable, uncompressed texture without mipmaps.");
                var white = true;
                foreach (var pixel in blank.GetPixels32())
                {
                    if (pixel.r == 255 && pixel.g == 255 && pixel.b == 255 && pixel.a == 255) continue;
                    white = false;
                    break;
                }
                Expect(white, "Every programmatic placeholder pixel must be opaque white.");
            }
            finally
            {
                if (blank != null) UnityEngine.Object.DestroyImmediate(blank);
            }
            Expect(blank == null, "The temporary programmatic placeholder must be destroyed after inspection.");
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
