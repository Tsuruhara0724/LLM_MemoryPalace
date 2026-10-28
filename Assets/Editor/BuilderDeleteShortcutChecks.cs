using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace MemPalaceLLM.Editor
{
    public static class BuilderDeleteShortcutChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;

        [MenuItem("Tools/Memory Palace/Check Builder Delete Shortcut")]
        public static void Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run these isolated checks outside Play Mode.");
            checks = 0;
            var originalRoom = RoomSpecCatalog.CurrentRoom;
            var originalKeyboard = Keyboard.current;
            var originalEvents = EventSystem.current;
            var inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var editModeUpdates = inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode");
            var originalEditModeUpdates = (bool)editModeUpdates.GetValue(inputManager);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var root = new GameObject("Builder deletion check") { hideFlags = HideFlags.HideAndDontSave };
            GameObject eventRoot = null;
            var registeredEventsManually = false;
            try
            {
                // Use player input frames without entering Play Mode or initializing XR.
                editModeUpdates.SetValue(inputManager, true);
                var controller = root.AddComponent<MemoryPalaceExperimentController>();
                Call(controller, "EnsureGridRoomLayoutLists");
                var itemType = typeof(MemoryPalaceExperimentController).GetNestedType("GridFurnitureInstanceData", BindingFlags.NonPublic);
                var ids = new[] { "bed", "wardrobe", "desk", "chair", "bookshelf", "bathtub", "sofa", "lamp", "plant" };
                foreach (var id in ids)
                {
                    var item = Activator.CreateInstance(itemType, true);
                    itemType.GetField("id").SetValue(item, id + "_check");
                    itemType.GetField("definitionId").SetValue(item, id);
                    Items(controller).Add(item);
                }
                var mode = Field("gridEditorMode");
                mode.SetValue(controller, Enum.Parse(mode.FieldType, "Furniture"));

                bool Press(params Key[] keys)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.Update();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                    InputSystem.Update();
                    return (bool)Call(controller, "HandleGridFurnitureDeleteShortcut", keyboard);
                }
                void Undo() => Call(controller, "UndoGridRoomEdit");
                bool Minimum()
                {
                    var arguments = new object[] { 0 };
                    return (bool)Call(controller, "HasMinimumGridFurnitureTypes", arguments);
                }

                Expect(Minimum(), "Nine types initially satisfy the count guard.");
                Set(controller, "selectedGridFurnitureInstanceIndex", 2);
                var handledB = Press(Key.B);
                Expect(handledB && Items(controller).Count == 8,
                    "B deletes in Furnish without requiring Finish mode. handled=" + handledB +
                    ", count=" + Items(controller).Count + ", pressed=" + keyboard.bKey.isPressed +
                    ", edge=" + keyboard.bKey.wasPressedThisFrame + ", overUI=" + Call(controller, "IsPointerOverGui") +
                    ", selected=" + Get(controller, "selectedGridFurnitureInstanceIndex"));
                Expect(!(bool)Call(controller, "IsGridFurnitureDefinitionAlreadyPlaced", "desk", -1), "The deleted furniture type can be placed again.");
                Expect(!Minimum(), "Deleting from nine to eight relocks the completion count guard.");
                Expect((int)Get(controller, "selectedGridFurnitureInstanceIndex") == -1 &&
                       (int)Get(controller, "selectedBuilderAnchorIndex") == -1, "Deletion does not silently select the next item.");
                Expect(Press(Key.B) && Items(controller).Count == 8, "A second B without selection cannot delete another item.");
                Undo();
                Expect(Items(controller).Count == 9 && Minimum(), "Undo restores the item and completion count.");

                Set(controller, "selectedGridFurnitureInstanceIndex", 0);
                Set(controller, "movingGridFurnitureInstanceIndex", 0);
                Set(controller, "hoveredGridFurnitureInstanceIndex", 3);
                Expect(Press(Key.B) && Items(controller).Count == 8, "B can delete a selected/picked-up item.");
                Expect((int)Get(controller, "movingGridFurnitureInstanceIndex") == -1 &&
                       (int)Get(controller, "hoveredGridFurnitureInstanceIndex") == -1, "Deleting clears movement and hover state.");
                Undo();
                foreach (var key in new[] { Key.Delete, Key.Backspace })
                {
                    Set(controller, "selectedGridFurnitureInstanceIndex", 0);
                    Expect(Press(key) && Items(controller).Count == 8, key + " remains a deletion alias.");
                    Undo();
                }
                Set(controller, "selectedGridFurnitureInstanceIndex", 0);
                Expect(!Press(Key.LeftCtrl, Key.B) && Items(controller).Count == 9, "Ctrl+B does not delete.");
                Expect(!Press(Key.LeftAlt, Key.B) && Items(controller).Count == 9, "Alt+B does not delete.");
                Expect(!Press(Key.R) && Items(controller).Count == 9, "The deletion shortcut does not consume R.");

                eventRoot = new GameObject("Builder text input check", typeof(EventSystem)) { hideFlags = HideFlags.HideAndDontSave };
                var inputObject = new GameObject("Archive name", typeof(RectTransform), typeof(InputField));
                inputObject.transform.SetParent(eventRoot.transform, false);
                var events = eventRoot.GetComponent<EventSystem>();
                var registeredEvents = (IList)typeof(EventSystem).GetField("m_EventSystems", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                if (!registeredEvents.Contains(events))
                {
                    // Non-ExecuteAlways lifecycle callbacks are not automatic in EditMode.
                    typeof(EventSystem).GetMethod("OnEnable", PrivateInstance).Invoke(events, null);
                    registeredEventsManually = true;
                }
                EventSystem.current = events;
                events.SetSelectedGameObject(inputObject);
                Expect(!Press(Key.B) && Items(controller).Count == 9, "Typing B in a selected input field cannot delete furniture.");
                Expect(!Press(Key.Backspace) && Items(controller).Count == 9, "Editing text with Backspace cannot delete furniture.");
                events.SetSelectedGameObject(null);
                Set(controller, "selectedGridFurnitureInstanceIndex", -1);
                Expect(Press(Key.B) && Items(controller).Count == 9, "No-selection deletion is a safe no-op.");
                Expect(((string)Get(controller, "gridEditorStatus")).Contains("Click a placed furniture item first"), "No-selection feedback explains how to delete.");
                Expect(((string)Call(controller, "GetGridBuilderViewportHint")).Contains("B / Delete"), "Builder hints expose the new shortcut.");
                Debug.Log("BUILDER DELETE SHORTCUT CHECK PASSED: " + checks + "; virtual keyboard, EditMode only, no scene files modified.");
            }
            finally
            {
                if (eventRoot != null)
                {
                    if (registeredEventsManually)
                        typeof(EventSystem).GetMethod("OnDisable", PrivateInstance).Invoke(eventRoot.GetComponent<EventSystem>(), null);
                    UnityEngine.Object.DestroyImmediate(eventRoot);
                }
                if (originalEvents != null) EventSystem.current = originalEvents;
                UnityEngine.Object.DestroyImmediate(root);
                InputSystem.RemoveDevice(keyboard);
                originalKeyboard?.MakeCurrent();
                editModeUpdates.SetValue(inputManager, originalEditModeUpdates);
                UpdateEditorInput();
                RoomSpecCatalog.SetCurrentRoom(originalRoom);
            }
        }

        private static void UpdateEditorInput()
        {
            // Restore the editor's input buffers after the isolated player-frame simulation.
            typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Editor });
        }
        private static FieldInfo Field(string name) => typeof(MemoryPalaceExperimentController).GetField(name, PrivateInstance);
        private static object Get(object target, string name) => Field(name).GetValue(target);
        private static void Set(object target, string name, object value) => Field(name).SetValue(target, value);
        private static object Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
        private static IList Items(object target)
        {
            var layout = Get(target, "gridRoomLayout");
            return (IList)layout.GetType().GetField("furniture").GetValue(layout);
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
