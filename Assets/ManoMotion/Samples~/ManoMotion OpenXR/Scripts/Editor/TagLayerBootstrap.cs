// Packages/com.yourco.yourpkg/Editor/TagLayerBootstrap.cs  (or Assets/.../Editor/)
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YourCo.YourPkg.Editor
{
    [InitializeOnLoad]
    public static class TagLayerBootstrap
    {
        // Customize these for your package:
        static readonly string[] RequiredTags  = { "Right", "Left", "RightHandInteractor", "LeftHandInteractor" };
        static readonly string[] RequiredLayers = { "Interactables", "Hands" };

        static TagLayerBootstrap()
        {
            // Run once after scripts reload (also on project open / domain reload)
            EditorApplication.delayCall += EnsureTagsAndLayers;
        }

        [MenuItem("Tools/YourPkg/Ensure Tags & Layers")]
        public static void EnsureTagsAndLayers()
        {
            var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (objs == null || objs.Length == 0)
            {
                Debug.LogWarning("[YourPkg] Could not load TagManager.asset");
                return;
            }

            var tagManager = new SerializedObject(objs[0]);

            // Tags
            var tagsProp = tagManager.FindProperty("tags");
            AddTags(tagsProp, RequiredTags);

            // Layers (Unity reserves 0–7; use 8–31)
            var layersProp = tagManager.FindProperty("layers");
            AddLayers(layersProp, RequiredLayers);

            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        static void AddTags(SerializedProperty tagsProp, IEnumerable<string> tags)
        {
            foreach (var tag in tags)
            {
                if (HasString(tagsProp, tag)) continue;
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
                Debug.Log($"[YourPkg] Added Tag: {tag}");
            }
        }

        static void AddLayers(SerializedProperty layersProp, IEnumerable<string> layers)
        {
            for (int i = 0; i < layersProp.arraySize; i++)
            {
                // ensure property array has at least 32 entries
            }

            foreach (var layer in layers)
            {
                if (HasString(layersProp, layer)) continue;

                // find empty slot 8..31
                int empty = -1;
                for (int i = 8; i <= 31; i++)
                {
                    var sp = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(sp.stringValue))
                    {
                        empty = i;
                        break;
                    }
                }

                if (empty == -1)
                {
                    Debug.LogWarning($"[YourPkg] No free layer slots (8–31) to add '{layer}'.");
                    continue;
                }

                layersProp.GetArrayElementAtIndex(empty).stringValue = layer;
                Debug.Log($"[YourPkg] Added Layer {empty}: {layer}");
            }
        }

        static bool HasString(SerializedProperty arrayProp, string value)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
                if (arrayProp.GetArrayElementAtIndex(i).stringValue == value)
                    return true;
            return false;
        }
    }
}
#endif