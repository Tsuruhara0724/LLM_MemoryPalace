using System;
using System.Collections.Generic;
using UnityEngine;

namespace MemPalaceLLM
{
    [Serializable]
    public class CameraPoseDefinition
    {
        public Vector3 position;
        public Vector3 eulerAngles;
    }

    [Serializable]
    public class RoomPrimitiveDefinition
    {
        public string id;
        public string label;
        public string primitiveShape;
        public string colorHex;
        public Vector3 position;
        public Vector3 scale;
        public Vector3 rotationEuler;
        public bool showLabel;
        public float labelHeight;
    }

    [Serializable]
    public class AnchorDefinition
    {
        public string id;
        public string label;
        public string primitiveShape;
        public string colorHex;
        public Vector3 position;
        public Vector3 scale;
        public Vector3 rotationEuler;
        public Vector3 mnemonicOffset;
        public float labelHeight;
        public List<VisualObjectSpec> modelParts = new();
    }

    [Serializable]
    public class RoomSpecDefinition
    {
        public string roomId;
        public string roomName;
        public string generatedBy;
        public string sourcePrompt;
        public string summary;
        public CameraPoseDefinition overviewCamera;
        public CameraPoseDefinition studyCamera;
        public List<RoomPrimitiveDefinition> environmentPrimitives = new();
        public List<AnchorDefinition> anchors = new();
    }

    public static class RoomSpecCatalog
    {
        private static RoomSpecDefinition currentRoom;

        public static RoomSpecDefinition CurrentRoom
        {
            get
            {
                if (currentRoom == null)
                {
                    currentRoom = LoadOrFallback();
                }

                return currentRoom;
            }
        }

        public static IReadOnlyList<AnchorDefinition> Anchors => CurrentRoom.anchors;

        public static string RoomName => CurrentRoom.roomName;

        public static int AnchorCount => CurrentRoom.anchors.Count;

        public static void SetCurrentRoom(RoomSpecDefinition room)
        {
            if (room == null)
            {
                return;
            }

            EnsureDefaults(room);
            currentRoom = room;
        }

        public static void ReloadResourceRoom()
        {
            currentRoom = LoadOrFallback();
        }

        public static AnchorDefinition GetAnchor(string anchorId)
        {
            for (int i = 0; i < CurrentRoom.anchors.Count; i++)
            {
                if (CurrentRoom.anchors[i].id == anchorId)
                {
                    return CurrentRoom.anchors[i];
                }
            }

            return CurrentRoom.anchors.Count > 0 ? CurrentRoom.anchors[0] : CreateFallbackRoom().anchors[0];
        }

        public static AnchorDefinition GetAssignmentAnchor(int wordIndex, int wordCount)
        {
            var anchors = CurrentRoom.anchors;
            if (anchors == null || anchors.Count == 0)
            {
                return CreateFallbackRoom().anchors[0];
            }

            if (wordCount <= 1)
            {
                return anchors[Mathf.Clamp(wordIndex, 0, anchors.Count - 1)];
            }

            if (anchors.Count <= wordCount)
            {
                return anchors[Mathf.Abs(wordIndex) % anchors.Count];
            }

            var spreadIndex = Mathf.RoundToInt(wordIndex * (anchors.Count - 1f) / (wordCount - 1f));
            return anchors[Mathf.Clamp(spreadIndex, 0, anchors.Count - 1)];
        }

        public static bool TryGetAnchor(string anchorId, out AnchorDefinition anchor)
        {
            for (int i = 0; i < CurrentRoom.anchors.Count; i++)
            {
                if (CurrentRoom.anchors[i].id == anchorId)
                {
                    anchor = CurrentRoom.anchors[i];
                    return true;
                }
            }

            anchor = null;
            return false;
        }

        public static PrimitiveType ParsePrimitiveType(string primitiveShape)
        {
            return primitiveShape switch
            {
                "Sphere" => PrimitiveType.Sphere,
                "Capsule" => PrimitiveType.Capsule,
                "Cylinder" => PrimitiveType.Cylinder,
                "Plane" => PrimitiveType.Plane,
                "Quad" => PrimitiveType.Quad,
                _ => PrimitiveType.Cube
            };
        }

        public static Color Hex(string htmlColor)
        {
            if (!string.IsNullOrWhiteSpace(htmlColor) && ColorUtility.TryParseHtmlString(htmlColor, out var parsed))
            {
                return parsed;
            }

            return Color.white;
        }

        private static RoomSpecDefinition LoadOrFallback()
        {
            var textAsset = Resources.Load<TextAsset>("MemPalaceRoomSpec");
            if (textAsset != null)
            {
                var loaded = JsonUtility.FromJson<RoomSpecDefinition>(textAsset.text);
                if (loaded != null && loaded.anchors != null && loaded.anchors.Count > 0)
                {
                    EnsureDefaults(loaded);
                    return loaded;
                }
            }

            return CreateFallbackRoom();
        }

        public static void EnsureDefaults(RoomSpecDefinition room)
        {
            room.environmentPrimitives ??= new List<RoomPrimitiveDefinition>();
            room.anchors ??= new List<AnchorDefinition>();
            room.overviewCamera ??= new CameraPoseDefinition();
            room.studyCamera ??= new CameraPoseDefinition();

            room.roomId = string.IsNullOrWhiteSpace(room.roomId)
                ? "generated_room_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : room.roomId;
            room.roomName = string.IsNullOrWhiteSpace(room.roomName)
                ? "Generated Memory Room"
                : room.roomName;
            room.generatedBy = string.IsNullOrWhiteSpace(room.generatedBy)
                ? "Ollama room generator"
                : room.generatedBy;
            room.sourcePrompt = room.sourcePrompt ?? string.Empty;
            room.summary = room.summary ?? string.Empty;

            for (int i = 0; i < room.environmentPrimitives.Count; i++)
            {
                room.environmentPrimitives[i].primitiveShape = string.IsNullOrWhiteSpace(room.environmentPrimitives[i].primitiveShape)
                    ? "Cube"
                    : room.environmentPrimitives[i].primitiveShape;
                room.environmentPrimitives[i].colorHex = string.IsNullOrWhiteSpace(room.environmentPrimitives[i].colorHex)
                    ? "#FFFFFF"
                    : room.environmentPrimitives[i].colorHex;
                if (room.environmentPrimitives[i].labelHeight <= 0f)
                {
                    room.environmentPrimitives[i].labelHeight = 0.8f;
                }
            }

            for (int i = 0; i < room.anchors.Count; i++)
            {
                room.anchors[i].modelParts ??= new List<VisualObjectSpec>();
                room.anchors[i].primitiveShape = string.IsNullOrWhiteSpace(room.anchors[i].primitiveShape)
                    ? "Cube"
                    : room.anchors[i].primitiveShape;
                room.anchors[i].colorHex = string.IsNullOrWhiteSpace(room.anchors[i].colorHex)
                    ? "#FFFFFF"
                    : room.anchors[i].colorHex;
                if (room.anchors[i].mnemonicOffset == default)
                {
                    room.anchors[i].mnemonicOffset = new Vector3(0f, 0.9f, 0f);
                }

                if (room.anchors[i].labelHeight <= 0f)
                {
                    room.anchors[i].labelHeight = 0.85f;
                }
            }
        }

        private static RoomSpecDefinition CreateFallbackRoom()
        {
            var room = new RoomSpecDefinition
            {
                roomId = "fallback_memory_studio",
                roomName = "Memory Studio Apartment",
                generatedBy = "Fallback room spec",
                sourcePrompt = "A compact studio apartment with clear, spaced anchors for a desktop memory-palace experiment.",
                summary = "Fallback room used when the external room spec resource is missing.",
                overviewCamera = new CameraPoseDefinition
                {
                    position = new Vector3(0f, 3f, -10.5f),
                    eulerAngles = new Vector3(14f, 0f, 0f)
                },
                studyCamera = new CameraPoseDefinition
                {
                    position = new Vector3(0f, 1.7f, -4.8f),
                    eulerAngles = Vector3.zero
                }
            };

            room.environmentPrimitives.AddRange(new[]
            {
                CreatePrimitive("floor", "Floor", "Cube", "#3F2E23", new Vector3(0f, -0.1f, 0f), new Vector3(12f, 0.2f, 12f), Vector3.zero, false, 0f),
                CreatePrimitive("ceiling", "Ceiling", "Cube", "#1F2433", new Vector3(0f, 4.2f, 0f), new Vector3(12f, 0.2f, 12f), Vector3.zero, false, 0f),
                CreatePrimitive("wall_back", "Back Wall", "Cube", "#354257", new Vector3(0f, 2f, 6f), new Vector3(12f, 4f, 0.2f), Vector3.zero, false, 0f),
                CreatePrimitive("wall_front", "Front Wall", "Cube", "#354257", new Vector3(0f, 2f, -6f), new Vector3(12f, 4f, 0.2f), Vector3.zero, false, 0f),
                CreatePrimitive("wall_left", "Left Wall", "Cube", "#354257", new Vector3(-6f, 2f, 0f), new Vector3(0.2f, 4f, 12f), Vector3.zero, false, 0f),
                CreatePrimitive("wall_right", "Right Wall", "Cube", "#354257", new Vector3(6f, 2f, 0f), new Vector3(0.2f, 4f, 12f), Vector3.zero, false, 0f),
                CreatePrimitive("rug", "Study Rug", "Cube", "#29475C", new Vector3(0f, -0.02f, -1.5f), new Vector3(4.2f, 0.02f, 2.4f), Vector3.zero, false, 0f),
                CreatePrimitive("sofa", "Sofa", "Cube", "#6A5C72", new Vector3(2.9f, 0.55f, -3.7f), new Vector3(2.2f, 1.1f, 0.9f), Vector3.zero, true, 1.0f),
                CreatePrimitive("bookshelf", "Bookshelf", "Cube", "#5B4635", new Vector3(-4.8f, 1.55f, -4.3f), new Vector3(1.4f, 3.1f, 0.45f), Vector3.zero, true, 1.65f)
            });

            room.anchors.AddRange(new[]
            {
                CreateAnchor("fridge", "Fridge", "Cube", "#D4D7DD", new Vector3(0f, 1.1f, 4.7f), new Vector3(1.1f, 2.1f, 0.9f), Vector3.zero, new Vector3(0f, 1.05f, 0f), 1.15f),
                CreateAnchor("sink", "Sink", "Cube", "#AAB7C4", new Vector3(-4.8f, 0.8f, 0.4f), new Vector3(1.2f, 0.7f, 1f), Vector3.zero, new Vector3(0f, 0.85f, 0f), 0.95f),
                CreateAnchor("table", "Table", "Cube", "#8B6A3A", new Vector3(2.2f, 0.7f, 0.2f), new Vector3(1.9f, 0.2f, 1.3f), Vector3.zero, new Vector3(0f, 0.7f, 0f), 0.95f),
                CreateAnchor("window", "Window", "Cube", "#779CCB", new Vector3(0f, 1.8f, 5.88f), new Vector3(2.4f, 1.2f, 0.08f), Vector3.zero, new Vector3(0f, 0.85f, -0.1f), 1.05f),
                CreateAnchor("stove", "Stove", "Cube", "#5D6066", new Vector3(-4.6f, 0.7f, 3.6f), new Vector3(1.1f, 1f, 1f), Vector3.zero, new Vector3(0f, 0.85f, 0f), 0.95f),
                CreateAnchor("counter", "Counter", "Cube", "#6D5848", new Vector3(4.9f, 0.8f, 2.4f), new Vector3(1.8f, 1f, 0.9f), Vector3.zero, new Vector3(0f, 0.9f, 0f), 1.0f),
                CreateAnchor("door", "Door", "Cube", "#7B5032", new Vector3(5.9f, 1.4f, -2.4f), new Vector3(0.1f, 2.5f, 1.5f), Vector3.zero, new Vector3(-0.55f, 0.1f, 0f), 1.45f),
                CreateAnchor("shelf", "Cube Shelf", "Cube", "#9C7A4D", new Vector3(-5.4f, 2f, -2f), new Vector3(0.4f, 0.15f, 2f), Vector3.zero, new Vector3(0.45f, 0.55f, 0f), 0.65f),
                CreateAnchor("floor_center", "Floor Center", "Cube", "#4C5F7C", new Vector3(0f, 0.08f, -0.6f), new Vector3(2.4f, 0.05f, 2.4f), Vector3.zero, new Vector3(0f, 0.45f, 0f), 0.55f),
                CreateAnchor("ceiling_lamp", "Ceiling Lamp", "Sphere", "#FFD98A", new Vector3(0f, 3.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), Vector3.zero, new Vector3(0f, -0.7f, 0f), 0.0f),
                CreateAnchor("desk", "Desk", "Cube", "#7A5A3B", new Vector3(3.9f, 0.72f, -4.3f), new Vector3(1.5f, 0.18f, 0.8f), Vector3.zero, new Vector3(0f, 0.72f, 0f), 0.95f),
                CreateAnchor("plant", "Plant", "Cylinder", "#6F8F5D", new Vector3(-4.6f, 0.72f, -0.2f), new Vector3(0.65f, 1.3f, 0.65f), Vector3.zero, new Vector3(0f, 0.82f, 0f), 1.0f)
            });

            EnsureDefaults(room);
            return room;
        }

        private static RoomPrimitiveDefinition CreatePrimitive(
            string id,
            string label,
            string primitiveShape,
            string colorHex,
            Vector3 position,
            Vector3 scale,
            Vector3 rotationEuler,
            bool showLabel,
            float labelHeight)
        {
            return new RoomPrimitiveDefinition
            {
                id = id,
                label = label,
                primitiveShape = primitiveShape,
                colorHex = colorHex,
                position = position,
                scale = scale,
                rotationEuler = rotationEuler,
                showLabel = showLabel,
                labelHeight = labelHeight
            };
        }

        private static AnchorDefinition CreateAnchor(
            string id,
            string label,
            string primitiveShape,
            string colorHex,
            Vector3 position,
            Vector3 scale,
            Vector3 rotationEuler,
            Vector3 mnemonicOffset,
            float labelHeight)
        {
            return new AnchorDefinition
            {
                id = id,
                label = label,
                primitiveShape = primitiveShape,
                colorHex = colorHex,
                position = position,
                scale = scale,
                rotationEuler = rotationEuler,
                mnemonicOffset = mnemonicOffset,
                labelHeight = labelHeight
            };
        }
    }
}
