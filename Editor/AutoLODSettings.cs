using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityObject = UnityEngine.Object;

namespace Unity.AutoLOD
{
    public class AutoLODSettings
    {
        static AutoLODSettingsData autoLODSettingsData => AutoLODSettingsData.Instance;

        [SettingsProvider]
        static SettingsProvider PreferencesGUI()
        {
            return new SettingsProvider("Preferences/AutoLOD", SettingsScope.User)
            {
                guiHandler = (searchContext) => DisplayPreferencesGUI(),
            };
            
            autoLODSettingsData.OnSettingsUpdated -= UpdateDependencies;
            autoLODSettingsData.OnSettingsUpdated += UpdateDependencies;
        }

        static void DisplayPreferencesGUI()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();
            MaxExecutionTimeGUI();
            MeshSimplifierGUI();
            BatcherGUI();
            MaxLODGUI();
            MaxLOD0PolyCountGUI();
            GenerateLODsOnImportGUI();
            SaveAssetsGUI();
            SameMaterialLODsGUI();
            UseSceneLODGUI();
            HierarchyTypeGUI();
            EditorGUILayout.EndVertical();
        }

        static void MaxExecutionTimeGUI()
        {
            var label = new GUIContent("Max Execution Time (ms)",
                "One of the features of AutoLOD is to keep the editor running responsively, so it’s possible to set"
                + "the max execution time for coroutines that run. AutLOD will spawn LOD generators on separate "
                + "threads, however, some generators may require main thread usage for accessing non thread-safe "
                + "Unity data structures and classes.");

            if (autoLODSettingsData.MaxExecutionTime == 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (!EditorGUILayout.Toggle(label, true))
                    autoLODSettingsData.MaxExecutionTime = 1;
                GUILayout.Label("Infinity");
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var maxTime = EditorGUILayout.IntSlider(label, autoLODSettingsData.MaxExecutionTime, 0, 15);
                if (EditorGUI.EndChangeCheck())
                    autoLODSettingsData.MaxExecutionTime = maxTime;
            }
        }

        static void MeshSimplifierGUI()
        {
            var type = autoLODSettingsData.MeshSimplifierType;
            if (type != null)
            {
                var label = new GUIContent("Default Mesh Simplifier", "All simplifiers (IMeshSimplifier) are "
                                                                      + "enumerated and provided here for selection. By allowing for multiple implementations, "
                                                                      + "different approaches can be compared. The default mesh simplifier is used to generate LODs "
                                                                      + "on import and when explicitly called.");

                var displayedOptions = autoLODSettingsData.MeshSimplifiers.Select(t => t.Name).ToArray();
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.Popup(label, Array.IndexOf(displayedOptions, type.Name), displayedOptions);
                if (EditorGUI.EndChangeCheck())
                    autoLODSettingsData.MeshSimplifierType = autoLODSettingsData.MeshSimplifiers[selected];

                if (autoLODSettingsData.MeshSimplifierType != null && typeof(IMeshSimplifier).IsAssignableFrom(autoLODSettingsData.MeshSimplifierType))
                {
                    if (autoLODSettingsData.SimplifierPreferences == null || autoLODSettingsData.SimplifierPreferences.GetType() != autoLODSettingsData.MeshSimplifierType)
                        autoLODSettingsData.SimplifierPreferences = Activator.CreateInstance(autoLODSettingsData.MeshSimplifierType) as IPreferences;

                    if (autoLODSettingsData.SimplifierPreferences != null)
                    {
                        EditorGUI.indentLevel++;
                        autoLODSettingsData.SimplifierPreferences.OnPreferencesGUI();
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No IMeshSimplifiers found!", MessageType.Warning);
            }
        }

        static void BatcherGUI()
        {
            var type = autoLODSettingsData.BatcherType;
            if (type != null)
            {
                var label = new GUIContent("Default Batcher", "All simplifiers (IMeshSimplifier) are "
                                                              + "enumerated and provided here for selection. By allowing for multiple implementations, "
                                                              + "different approaches can be compared. The default batcher is used in HLOD generation when "
                                                              + "combining objects that are located within the same LODVolume.");

                var displayedOptions = autoLODSettingsData.Batchers.Select(t => t.Name).ToArray();
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.Popup(label, Array.IndexOf(displayedOptions, type.Name), displayedOptions);
                if (EditorGUI.EndChangeCheck())
                    autoLODSettingsData.BatcherType = autoLODSettingsData.Batchers[selected];
            }
            else
            {
                EditorGUILayout.HelpBox("No IBatchers found!", MessageType.Warning);
            }
        }
        
        static void MaxLODGUI()
        {
            var label = new GUIContent("Maximum LOD Generated", "Controls the depth of the generated LOD chain");

            var maxLODValues = Enumerable.Range(0, LODData.MaxLOD + 1).ToArray();
            EditorGUI.BeginChangeCheck();
            int maxLODGenerated = EditorGUILayout.IntPopup(label, autoLODSettingsData.MaxLOD,
                maxLODValues.Select(v => new GUIContent(v.ToString())).ToArray(), maxLODValues);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.MaxLOD = maxLODGenerated;
        }

        static void MaxLOD0PolyCountGUI ()
        {
            var label = new GUIContent("Initial LOD Max Poly Count", "In the case where non realtime-ready assets "
                                                                     + "are brought into Unity these would normally perform poorly. Being able to set a max poly count "
                                                                     + "for LOD0 allows even the largest of meshes to import with performance-minded defaults.");

            EditorGUI.BeginChangeCheck();
            var maxPolyCount = EditorGUILayout.IntField(label, autoLODSettingsData.InitialLODMaxPolyCount);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.InitialLODMaxPolyCount = maxPolyCount;
        }

        static void GenerateLODsOnImportGUI()
        {
            var label = new GUIContent("Generate on Import", "Controls whether automatic LOD generation will happen "
                                                             + "on import. Even if this option is disabled it is still possible to generate LOD chains "
                                                             + "individually on individual files.");

            EditorGUI.BeginChangeCheck();
            var generateLODsOnImport = EditorGUILayout.Toggle(label, autoLODSettingsData.GenerateOnImport);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.GenerateOnImport = generateLODsOnImport;
        }

        static void SaveAssetsGUI()
        {
            var label = new GUIContent("Save Assets",
                "This can speed up performance, but may cause errors with some simplifiers");
            EditorGUI.BeginChangeCheck();
            var saveAssetsOnImport = EditorGUILayout.Toggle(label, autoLODSettingsData.SaveAssets);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.SaveAssets = saveAssetsOnImport;
        }

        static void SameMaterialLODsGUI()
        {
            var label = new GUIContent("Use Same Material for LODs", "If enabled, all LODs will use the same material as LOD0.");
            EditorGUI.BeginChangeCheck();
            var useSameMaterial = EditorGUILayout.Toggle(label, autoLODSettingsData.UseSameMaterialForLODs);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.UseSameMaterialForLODs = useSameMaterial;
        }

        static void UseSceneLODGUI()
        {
            var label = new GUIContent("Scene LOD", "Enable Hierarchical LOD (HLOD) support for scenes, "
                                                    + "which will automatically generate and stay updated in the background.");

            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.Toggle(label, autoLODSettingsData.SceneLODEnabled);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.SceneLODEnabled = enabled;

            if (autoLODSettingsData.SceneLODEnabled)
            {
                label = new GUIContent("Show Volume Bounds", "This will display the bounds visually of the bounding "
                                                             + "volume hierarchy (currently an Octree)");

                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                var showBounds = EditorGUILayout.Toggle(label, autoLODSettingsData.ShowVolumeBounds);
                if (EditorGUI.EndChangeCheck())
                    autoLODSettingsData.ShowVolumeBounds = showBounds;

                var sceneLOD = SceneLOD.instance;
                EditorGUILayout.HelpBox(string.Format("Coroutine Queue: {0}\nCurrent Execution Time: {1:0.00} s", sceneLOD.coroutineQueueRemaining, sceneLOD.coroutineCurrentExecutionTime * 0.001f), MessageType.None);

                // Force more frequent updating
                var mouseOverWindow = EditorWindow.mouseOverWindow;
                if (mouseOverWindow)
                    mouseOverWindow.Repaint();

                EditorGUI.indentLevel--;
            }
        }
        
        static void HierarchyTypeGUI ()
        {
            var label = new GUIContent("Default Hierarchy Type", "Controls the hierarchy type used for LODs");

            EditorGUI.BeginChangeCheck();
            LODHierarchyType hierarchy = (LODHierarchyType)EditorGUILayout.EnumPopup(label, autoLODSettingsData.HierarchyType);
            if (EditorGUI.EndChangeCheck())
                autoLODSettingsData.HierarchyType = hierarchy;
        }
        
        static public void UpdateDependencies()
        {
            MonoBehaviourHelper.maxSharedExecutionTimeMS = autoLODSettingsData.MaxExecutionTime == 0 ? Mathf.Infinity : autoLODSettingsData.MaxExecutionTime;

            LODDataEditor.meshSimplifier = autoLODSettingsData.MeshSimplifierType.AssemblyQualifiedName;
            LODDataEditor.batcher = autoLODSettingsData.BatcherType.AssemblyQualifiedName;
            LODDataEditor.maxLODGenerated = autoLODSettingsData.MaxLOD;
            LODDataEditor.initialLODMaxPolyCount = autoLODSettingsData.InitialLODMaxPolyCount;
            LODDataEditor.hierarchyType = autoLODSettingsData.HierarchyType;
            
            if (autoLODSettingsData.SceneLODEnabled && !SceneLOD.activated)
            {
                if (!SceneLOD.instance)
                    Debug.LogError("SceneLOD failed to start");
            }
            else if (!autoLODSettingsData.SceneLODEnabled && SceneLOD.activated)
            {
                UnityObject.DestroyImmediate(SceneLOD.instance);
            }
        }
    }
}