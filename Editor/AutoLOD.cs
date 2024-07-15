#if ENABLE_INSTALOD
#define HAS_INSTALOD
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityObject = UnityEngine.Object;

namespace Unity.AutoLOD
{
    [InitializeOnLoad]
    class AutoLOD
    {
        static AutoLODSettingsData autoLODSettingsData => AutoLODSettingsData.Instance;

        static SceneLOD s_SceneLOD;

        static AutoLOD()
        {
            if (autoLODSettingsData.MeshSimplifierType == null)
            {
                MonoBehaviourHelper.StartCoroutine(AutoLODSettingsData.GetDefaultSimplifier());
            }
        }
        
        #region GenerateLOD
        static public void GenerateLODs(GameObject go)
        {
            // A NOP to make sure we have an instance before launching into threads that may need to execute on the main thread
            MonoBehaviourHelper.ExecuteOnMainThread(() => {});

            var meshFilters = go.GetComponentsInChildren<MeshFilter>();

            if (meshFilters.Length > 0)
            {
                var lodGroup = go.GetComponent<LODGroup>();
                if (!lodGroup)
                    lodGroup = go.AddComponent<LODGroup>();

                var lods = new LOD[autoLODSettingsData.MaxLOD + 1];
                var lod0 = lods[0];
                lod0.renderers = go.GetComponentsInChildren<MeshRenderer>();
                lod0.screenRelativeTransitionHeight = 0.5f;
                lods[0] = lod0;

                var meshes = new List<Mesh>();

                for (int l = 1; l <= autoLODSettingsData.MaxLOD; l++)
                {
                    var lodRenderers = new List<MeshRenderer>();
                    foreach (var mf in meshFilters)
                    {
                        var sharedMesh = mf.sharedMesh;

                        if (!sharedMesh)
                        {
                            Debug.LogWarning("AutoLOD: Missing mesh " + mf.name, mf);
                            continue;
                        }

                        var lodTransform = EditorUtility.CreateGameObjectWithHideFlags(string.Format("{0} LOD{1}", sharedMesh.name, l),
                            AutoLODConst.k_DefaultHideFlags, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                        lodTransform.SetParent(mf.transform, false);

                        var lodMF = lodTransform.GetComponent<MeshFilter>();
                        var lodRenderer = lodTransform.GetComponent<MeshRenderer>();

                        lodRenderers.Add(lodRenderer);

                        EditorUtility.CopySerialized(mf, lodMF);
                        EditorUtility.CopySerialized(mf.GetComponent<MeshRenderer>(), lodRenderer);

                        if (autoLODSettingsData.UseSameMaterialForLODs)
                        {
                            lodRenderer.sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
                        }

                        var simplifiedMesh = new Mesh();
                        simplifiedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        simplifiedMesh.name = sharedMesh.name + string.Format(" LOD{0}", l);
                        lodMF.sharedMesh = simplifiedMesh;
                        meshes.Add(simplifiedMesh);

                        var meshLOD = MeshLOD.GetGenericInstance(autoLODSettingsData.MeshSimplifierType);
                        meshLOD.InputMesh = sharedMesh;
                        meshLOD.OutputMesh = simplifiedMesh;
                        meshLOD.Quality = Mathf.Pow(0.5f, l);
                        meshLOD.Generate();
                    }

                    var lod = lods[l];
                    lod.renderers = lodRenderers.ToArray();
                    lod.screenRelativeTransitionHeight = l == autoLODSettingsData.MaxLOD ? 0.01f : Mathf.Pow(0.5f, l + 1);
                    lods[l] = lod;
                }

                lodGroup.ForceLOD(0);
                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                lodGroup.ForceLOD(-1);

                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefab)
                {
                    var lodsAssetPath = GetLODAssetPath(prefab);
                    if (File.Exists(lodsAssetPath))
                        meshes.ForEach(m => AssetDatabase.AddObjectToAsset(m, lodsAssetPath));
                    else
                        ObjectUtils.CreateAssetFromObjects(meshes.ToArray(), lodsAssetPath);
                }
            }
        }
        
        [MenuItem("Assets/AutoLOD/Generate LOD", false)]
        static void ForceGenerateLOD()
        {
            var selection = Selection.activeGameObject;
            if (selection)
            {
                var prefabType = PrefabUtility.GetPrefabAssetType(selection);
                if (prefabType == PrefabAssetType.Model)
                {
                    var assetPath = AssetDatabase.GetAssetPath(selection);

                    if (!autoLODSettingsData.GenerateOnImport)
                    {
                        // If AutoLOD's generate on import is disabled for the whole project, then generate LODs for this model specifically
                        var lodData = ModelImporterLODGenerator.GetLODData(assetPath);
                        lodData.overrideDefaults = true;
                        lodData.importSettings.generateOnImport = true;

                        var lodPath = ModelImporterLODGenerator.GetLODDataPath(assetPath);
                        AssetDatabase.CreateAsset(lodData, lodPath);
                    }

                    AssetDatabase.ImportAsset(assetPath);
                }
                else if (prefabType == PrefabAssetType.Regular)
                {
                    GenerateLODs(new MenuCommand(null));
                }
            }
        }
        
        
        [MenuItem("Assets/AutoLOD/Generate LOD", true)]
        static bool CanForceGenerateLOD()
        {
            var selection = Selection.activeGameObject;
            var prefabType = selection ? PrefabUtility.GetPrefabAssetType(selection) : PrefabAssetType.NotAPrefab;
            return selection && prefabType == PrefabAssetType.Model || prefabType == PrefabAssetType.Regular;
        }
        
        [MenuItem("GameObject/AutoLOD/Generate LODs (Prefabs and Scene GameObjects)", priority = 11)]
        static void GenerateLODs(MenuCommand menuCommand)
        {
            MonoBehaviourHelper.StartCoroutine(GenerateLODsCoroutine(menuCommand));
        }
        
        static public IEnumerator GenerateLODsCoroutine(MenuCommand menuCommand)
        {
            var activeObject = Selection.activeObject;
            DefaultAsset folderAsset = null;
            if (IsDirectoryAsset(activeObject))
            {
                folderAsset = (DefaultAsset)activeObject;
                SelectAllGameObjectsUnderneathFolder(folderAsset, prefab => !HasLODChain(prefab));
            }

            yield return null;

            var go = menuCommand.context as GameObject;
            if (go)
            {
                AutoLOD.GenerateLODs(go);
            }
            else
            {
                IterateOverSelectedGameObjects(current =>
                {
                    AutoLOD.RemoveChildrenLODGroups(current);
                    AutoLOD.GenerateLODs(current);
                });
            }

            if (folderAsset)
                Selection.activeObject = folderAsset;
        }

        [MenuItem("GameObject/AutoLOD/Generate LODs (Prefabs and Scene GameObjects)", validate = true, priority = 11)]
        static bool CanGenerateLODs()
        {
            bool enabled = true;

            // Allow processing of whole directories
            var activeObject = Selection.activeObject;
            if (IsDirectoryAsset(activeObject))
                return true;

            var gameObjects = Selection.gameObjects;
            if (gameObjects.Length == 0)
                return false;

            foreach (var go in gameObjects)
            {
                enabled = !HasLODChain(go);

                if (!enabled)
                    break;
            }

            return enabled;
        }

        #endregion
        
        #region RemoveLOD
        static public void RemoveChildrenLODGroups(GameObject go)
        {
            var mainLODGroup = go.GetComponent<LODGroup>();
            var lodGroups = go.GetComponentsInChildren<LODGroup>();
            foreach (var lodGroup in lodGroups)
            {
                if (mainLODGroup != lodGroup)
                    UnityObject.DestroyImmediate(lodGroup);
            }
        }

        static public void RemoveLODs(GameObject go)
        {
            var lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup)
            {
                var lods = lodGroup.GetLODs();
                for (var i = 1; i < lods.Length; i++)
                {
                    var lod = lods[i];
                    var renderers = lod.renderers;
                    foreach (var r in renderers)
                    {
                        if (r)
                            UnityObject.DestroyImmediate(r.gameObject);
                    }
                }

                UnityObject.DestroyImmediate(lodGroup);
            }

            var meshFilters = go.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (!mf.sharedMesh)
                    UnityObject.DestroyImmediate(mf.gameObject);
            }

            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefab)
            {
                var lodAssetPath = GetLODAssetPath(prefab);
                AssetDatabase.DeleteAsset(lodAssetPath);
            }
        }
        
                [MenuItem("GameObject/AutoLOD/Remove LODs", validate = true, priority = 11)]
        static bool RemoveLODsValidate()
        {
            // Allow processing of whole directories
            var activeObject = Selection.activeObject;
            if (IsDirectoryAsset(activeObject))
                return true;

            var gameObjects = Selection.gameObjects;
            if (gameObjects.Length == 0)
                return false;

            foreach (var go in gameObjects)
            {
                if (go.GetComponent<LODGroup>())
                    return true;
            }

            return false;
        }

        [MenuItem("GameObject/AutoLOD/Remove LODs", priority = 11)]
        static void RemoveLODs(MenuCommand menuCommand)
        {
            var activeObject = Selection.activeObject;
            DefaultAsset folderAsset = null;
            if (IsDirectoryAsset(activeObject))
            {
                folderAsset = (DefaultAsset)activeObject;
                SelectAllGameObjectsUnderneathFolder(folderAsset, HasLODChain);
            }

            var go = menuCommand.context as GameObject;
            if (go)
                AutoLOD.RemoveLODs(go);
            else
                IterateOverSelectedGameObjects(AutoLOD.RemoveLODs);

            if (folderAsset)
                Selection.activeObject = folderAsset;
        }

        [MenuItem("GameObject/AutoLOD/Remove Children LODGroups", priority = 11)]
        static void RemoveChildrenLODGroups(MenuCommand menuCommand)
        {
            var folderAsset = Selection.activeObject as DefaultAsset;
            if (folderAsset)
                SelectAllGameObjectsUnderneathFolder(folderAsset, prefab => prefab.GetComponent<LODGroup>());

            var go = menuCommand.context as GameObject;
            if (go)
                AutoLOD.RemoveChildrenLODGroups(go);
            else
                IterateOverSelectedGameObjects(AutoLOD.RemoveChildrenLODGroups);

            if (folderAsset)
                Selection.activeObject = folderAsset;
        }
        #endregion
        
        #region Aux Methods
        
        static string GetLODAssetPath(UnityObject prefab)
        {
            var assetPath = AssetDatabase.GetAssetPath(prefab);
            var pathPrefix = Path.GetDirectoryName(assetPath) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(assetPath);
            var lodsAssetPath = pathPrefix + "_lods.asset";
            return lodsAssetPath;
        }
        
        static void IterateOverSelectedGameObjects(Action<GameObject> callback)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                var gameObjects = Selection.gameObjects;
                var count = gameObjects.Length;
                for (int i = 0; i < count; i++)
                {
                    var selection = gameObjects[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Prefabs", selection.name, i / (float)count))
                        break;

                    if (selection && PrefabUtility.GetPrefabAssetType(selection) == PrefabAssetType.Regular)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(selection);
                        callback(go);
                        PrefabUtility.SaveAsPrefabAsset(go, AssetDatabase.GetAssetPath(selection));
                        UnityObject.DestroyImmediate(go);
                    }
                    else
                    {
                        callback(selection);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
            }
        }
        
        static bool HasLODChain(GameObject go)
        {
            var lodGroup = go.GetComponent<LODGroup>();
            return lodGroup.HasLODChain();
        }

        static bool IsDirectoryAsset(UnityObject unityObject)
        {
            if (unityObject is DefaultAsset)
            {
                var path = AssetDatabase.GetAssetPath(unityObject);
                if (File.GetAttributes(path) == FileAttributes.Directory)
                    return true;
            }

            return false;
        }
        
        static void SelectAllGameObjectsUnderneathFolder(DefaultAsset folderAsset, Func<GameObject, bool> predicate)
        {
            var path = AssetDatabase.GetAssetPath(folderAsset);
            if (File.GetAttributes(path) == FileAttributes.Directory)
            {
                var gameObjects = new List<UnityObject>();
                var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                foreach (var p in prefabs)
                {
                    var prefab = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(p));
                    if (prefab && (predicate == null || predicate((GameObject)prefab)))
                        gameObjects.Add(prefab);
                }
                Selection.objects = gameObjects.ToArray();
            }
        }
        #endregion

    }
}