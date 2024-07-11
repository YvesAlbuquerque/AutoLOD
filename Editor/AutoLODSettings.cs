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
        static public Type meshSimplifierType
        {
            set
            {
                if (typeof(IMeshSimplifier).IsAssignableFrom(value))
                    EditorPrefs.SetString(AutoLODConst.k_DefaultMeshSimplifier, value.AssemblyQualifiedName);
                else if (value == null)
                    EditorPrefs.DeleteKey(AutoLODConst.k_DefaultMeshSimplifier);

                UpdateDependencies();
            }
            get
            {
                var type = Type.GetType(EditorPrefs.GetString(AutoLODConst.k_DefaultMeshSimplifier, AutoLODConst.k_DefaultMeshSimplifierDefault));
                
                if (type == null || !typeof(IMeshSimplifier).IsAssignableFrom(type))
                    type = Type.GetType(AutoLODConst.k_DefaultMeshSimplifierDefault);
                
                if (type == null && meshSimplifiers.Count > 0)
                    type = Type.GetType(meshSimplifiers[0].AssemblyQualifiedName);
                return type;
            }
        }
        
        static List<Type> meshSimplifiers
        {
            get
            {
                if (s_MeshSimplifiers == null || s_MeshSimplifiers.Count == 0)
                {
                    s_MeshSimplifiers = ObjectUtils.GetImplementationsOfInterface(typeof(IMeshSimplifier)).ToList();
                    
#if ENABLE_INSTALOD
                    var instaLODSimplifier = Type.GetType("Unity.AutoLOD.InstaLODMeshSimplifier, Assembly-CSharp-Editor");
                    if (instaLODSimplifier != null)
                    {
                        s_MeshSimplifiers.Add(instaLODSimplifier);
                    }
#endif
                }

                return s_MeshSimplifiers;
            }
        }
        
        static int maxExecutionTime
        {
            set
            {
                EditorPrefs.SetInt(AutoLODConst.k_MaxExecutionTime, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetInt(AutoLODConst.k_MaxExecutionTime, AutoLODConst.k_DefaultMaxExecutionTime); }
        }

        static Type batcherType
        {
            set
            {
                if (typeof(IBatcher).IsAssignableFrom(value))
                    EditorPrefs.SetString(AutoLODConst.k_DefaultBatcher, value.AssemblyQualifiedName);
                else if (value == null)
                    EditorPrefs.DeleteKey(AutoLODConst.k_DefaultBatcher);

                UpdateDependencies();
            }
            get
            {
                var type = Type.GetType(EditorPrefs.GetString(AutoLODConst.k_DefaultBatcher, null));
                if (type == null && batchers.Count > 0)
                    type = Type.GetType(batchers[0].AssemblyQualifiedName);
                return type;
            }
        }
        
        static bool generateOnImport
        {
            set
            {
                EditorPrefs.SetBool(AutoLODConst.k_GenerateOnImport, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetBool(AutoLODConst.k_GenerateOnImport, false); }
        }

        static bool saveAssets
        {
            get { return EditorPrefs.GetBool(AutoLODConst.k_SaveAssets, true); }
            set
            {
                EditorPrefs.SetBool(AutoLODConst.k_SaveAssets, value);
                UpdateDependencies();
            }
        }

        static int initialLODMaxPolyCount
        {
            set
            {
                EditorPrefs.SetInt(AutoLODConst.k_InitialLODMaxPolyCount, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetInt(AutoLODConst.k_InitialLODMaxPolyCount, AutoLODConst.k_DefaultInitialLODMaxPolyCount); }
        }

        static bool sceneLODEnabled
        {
            set
            {
                EditorPrefs.SetBool(AutoLODConst.k_SceneLODEnabled, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetBool(AutoLODConst.k_SceneLODEnabled, true); }
        }

        static bool showVolumeBounds
        {
            set
            {
                EditorPrefs.SetBool(AutoLODConst.k_ShowVolumeBounds, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetBool(AutoLODConst.k_ShowVolumeBounds, false); }
        }
        
        static List<Type> batchers
        {
            get
            {
                if (s_Batchers == null || s_Batchers.Count == 0)
                    s_Batchers = ObjectUtils.GetImplementationsOfInterface(typeof(IBatcher)).ToList();

                return s_Batchers;
            }
        }
        
        static LODHierarchyType hierarchyType
        {
            set
            {
                EditorPrefs.SetInt(AutoLODConst.k_hierarchyType, (int)value);
                UpdateDependencies();
            }
            get { return (LODHierarchyType)EditorPrefs.GetInt(AutoLODConst.k_hierarchyType, (int)LODHierarchyType.ChildOfSource); }
        }
        
        static public int maxLOD
        {
            set
            {
                EditorPrefs.SetInt(AutoLODConst.k_MaxLOD, value);
                UpdateDependencies();
            }
            get { return EditorPrefs.GetInt(AutoLODConst.k_MaxLOD, AutoLODConst.k_DefaultMaxLOD); }
        }

        static public bool useSameMaterialForLODs
        {
            get { return EditorPrefs.GetBool(AutoLODConst.k_useSameMaterialForLODs, false); }
            set
            {
                EditorPrefs.SetBool(AutoLODConst.k_useSameMaterialForLODs, value);
                UpdateDependencies();
            }
        }
        
        
        static List<Type> s_Batchers;
        static List<Type> s_MeshSimplifiers;
        static IPreferences s_SimplifierPreferences;
        
        
        [SettingsProvider]
        static SettingsProvider PreferencesGUI()
        {
            return new SettingsProvider("Preferences/AutoLOD", SettingsScope.User)
            {
                guiHandler = (searchContext) => DisplayPreferencesGUI(),
            };
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

            if (maxExecutionTime == 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (!EditorGUILayout.Toggle(label, true))
                    maxExecutionTime = 1;
                GUILayout.Label("Infinity");
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var maxTime = EditorGUILayout.IntSlider(label, maxExecutionTime, 0, 15);
                if (EditorGUI.EndChangeCheck())
                    maxExecutionTime = maxTime;
            }
        }

        static void MeshSimplifierGUI()
        {
            var type = meshSimplifierType;
            if (type != null)
            {
                var label = new GUIContent("Default Mesh Simplifier", "All simplifiers (IMeshSimplifier) are "
                                                                      + "enumerated and provided here for selection. By allowing for multiple implementations, "
                                                                      + "different approaches can be compared. The default mesh simplifier is used to generate LODs "
                                                                      + "on import and when explicitly called.");

                var displayedOptions = meshSimplifiers.Select(t => t.Name).ToArray();
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.Popup(label, Array.IndexOf(displayedOptions, type.Name), displayedOptions);
                if (EditorGUI.EndChangeCheck())
                    meshSimplifierType = meshSimplifiers[selected];

                if (meshSimplifierType != null && typeof(IMeshSimplifier).IsAssignableFrom(meshSimplifierType))
                {
                    if (s_SimplifierPreferences == null || s_SimplifierPreferences.GetType() != meshSimplifierType)
                        s_SimplifierPreferences = Activator.CreateInstance(meshSimplifierType) as IPreferences;

                    if (s_SimplifierPreferences != null)
                    {
                        EditorGUI.indentLevel++;
                        s_SimplifierPreferences.OnPreferencesGUI();
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
            var type = batcherType;
            if (type != null)
            {
                var label = new GUIContent("Default Batcher", "All simplifiers (IMeshSimplifier) are "
                                                              + "enumerated and provided here for selection. By allowing for multiple implementations, "
                                                              + "different approaches can be compared. The default batcher is used in HLOD generation when "
                                                              + "combining objects that are located within the same LODVolume.");

                var displayedOptions = batchers.Select(t => t.Name).ToArray();
                EditorGUI.BeginChangeCheck();
                var selected = EditorGUILayout.Popup(label, Array.IndexOf(displayedOptions, type.Name), displayedOptions);
                if (EditorGUI.EndChangeCheck())
                    batcherType = batchers[selected];
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
            int maxLODGenerated = EditorGUILayout.IntPopup(label, maxLOD,
                maxLODValues.Select(v => new GUIContent(v.ToString())).ToArray(), maxLODValues);
            if (EditorGUI.EndChangeCheck())
                maxLOD = maxLODGenerated;
        }

        static void MaxLOD0PolyCountGUI ()
        {
            var label = new GUIContent("Initial LOD Max Poly Count", "In the case where non realtime-ready assets "
                                                                     + "are brought into Unity these would normally perform poorly. Being able to set a max poly count "
                                                                     + "for LOD0 allows even the largest of meshes to import with performance-minded defaults.");

            EditorGUI.BeginChangeCheck();
            var maxPolyCount = EditorGUILayout.IntField(label, initialLODMaxPolyCount);
            if (EditorGUI.EndChangeCheck())
                initialLODMaxPolyCount = maxPolyCount;
        }

        static void GenerateLODsOnImportGUI()
        {
            var label = new GUIContent("Generate on Import", "Controls whether automatic LOD generation will happen "
                                                             + "on import. Even if this option is disabled it is still possible to generate LOD chains "
                                                             + "individually on individual files.");

            EditorGUI.BeginChangeCheck();
            var generateLODsOnImport = EditorGUILayout.Toggle(label, generateOnImport);
            if (EditorGUI.EndChangeCheck())
                generateOnImport = generateLODsOnImport;
        }

        static void SaveAssetsGUI()
        {
            var label = new GUIContent("Save Assets",
                "This can speed up performance, but may cause errors with some simplifiers");
            EditorGUI.BeginChangeCheck();
            var saveAssetsOnImport = EditorGUILayout.Toggle(label, saveAssets);
            if (EditorGUI.EndChangeCheck())
                saveAssets = saveAssetsOnImport;
        }

        static void SameMaterialLODsGUI()
        {
            var label = new GUIContent("Use Same Material for LODs", "If enabled, all LODs will use the same material as LOD0.");
            EditorGUI.BeginChangeCheck();
            var useSameMaterial = EditorGUILayout.Toggle(label, useSameMaterialForLODs);
            if (EditorGUI.EndChangeCheck())
                useSameMaterialForLODs = useSameMaterial;
        }

        static void UseSceneLODGUI()
        {
            var label = new GUIContent("Scene LOD", "Enable Hierarchical LOD (HLOD) support for scenes, "
                                                    + "which will automatically generate and stay updated in the background.");

            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.Toggle(label, sceneLODEnabled);
            if (EditorGUI.EndChangeCheck())
                sceneLODEnabled = enabled;

            if (sceneLODEnabled)
            {
                label = new GUIContent("Show Volume Bounds", "This will display the bounds visually of the bounding "
                                                             + "volume hierarchy (currently an Octree)");

                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                var showBounds = EditorGUILayout.Toggle(label, showVolumeBounds);
                if (EditorGUI.EndChangeCheck())
                    showVolumeBounds = showBounds;

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
            LODHierarchyType hierarchy = (LODHierarchyType)EditorGUILayout.EnumPopup(label, hierarchyType);
            if (EditorGUI.EndChangeCheck())
                hierarchyType = hierarchy;
        }
        static public void UpdateDependencies()
        {
            if (meshSimplifierType == null)
            {
                MonoBehaviourHelper.StartCoroutine(AutoLOD.GetDefaultSimplifier());
                ModelImporterLODGenerator.enabled = false;
                return;
            }

            MonoBehaviourHelper.maxSharedExecutionTimeMS = maxExecutionTime == 0 ? Mathf.Infinity : maxExecutionTime;

            LODDataEditor.meshSimplifier = meshSimplifierType.AssemblyQualifiedName;
            LODDataEditor.batcher = batcherType.AssemblyQualifiedName;
            LODDataEditor.maxLODGenerated = maxLOD;
            LODDataEditor.initialLODMaxPolyCount = initialLODMaxPolyCount;
            LODDataEditor.hierarchyType = hierarchyType;
            
            LODVolume.meshSimplifierType = meshSimplifierType;
            LODVolume.batcherType = batcherType;
            LODVolume.drawBounds = sceneLODEnabled && showVolumeBounds;

            ModelImporterLODGenerator.saveAssets = saveAssets;
            ModelImporterLODGenerator.meshSimplifierType = meshSimplifierType;
            ModelImporterLODGenerator.maxLOD = maxLOD;
            ModelImporterLODGenerator.enabled = generateOnImport;
            ModelImporterLODGenerator.initialLODMaxPolyCount = initialLODMaxPolyCount;
            ModelImporterLODGenerator.hierarchyType = (LODHierarchyType)hierarchyType;
            
            if (sceneLODEnabled && !SceneLOD.activated)
            {
                if (!SceneLOD.instance)
                    Debug.LogError("SceneLOD failed to start");
            }
            else if (!sceneLODEnabled && SceneLOD.activated)
            {
                UnityObject.DestroyImmediate(SceneLOD.instance);
            }
        }
    }
}