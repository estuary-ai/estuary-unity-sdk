using UnityEditor;
using UnityEngine;

namespace Estuary.Editor
{
    /// <summary>
    /// Convenience menu for spawning fully-wired Estuary objects in a scene, so you
    /// don't have to add and cross-reference EstuaryCharacter / EstuaryAudioSource /
    /// EstuaryMicrophone by hand.
    ///
    /// Lives in its own editor assembly (Estuary.Editor.Tools) that references the
    /// runtime — kept separate from Estuary.Editor so the dependency auto-installer
    /// there stays independent of whether the runtime compiles.
    /// </summary>
    internal static class EstuaryCreateMenu
    {
        [MenuItem("GameObject/Estuary/AI Character", false, 10)]
        private static void CreateCharacter(MenuCommand menuCommand)
        {
            // Ensure a manager exists — the character connects through it.
            EstuaryManager manager =
#if UNITY_2022_2_OR_NEWER
                Object.FindFirstObjectByType<EstuaryManager>();
#else
                Object.FindObjectOfType<EstuaryManager>();
#endif
            if (manager == null)
            {
                var managerGo = ObjectFactory.CreateGameObject("EstuaryManager", typeof(EstuaryManager));
                Undo.RegisterCreatedObjectUndo(managerGo, "Create EstuaryManager");
            }

            var go = ObjectFactory.CreateGameObject("Estuary Character");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // ObjectFactory.AddComponent mimics the Add Component button, so
            // EstuaryCharacter.Reset() runs and auto-wires the audio + mic stack.
            ObjectFactory.AddComponent<EstuaryCharacter>(go);

            Undo.RegisterCreatedObjectUndo(go, "Create Estuary Character");
            Selection.activeGameObject = go;
        }
    }
}
