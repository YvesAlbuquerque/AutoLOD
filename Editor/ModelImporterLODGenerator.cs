using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.AutoLOD
{
    /// <summary>
    /// Handles the automatic generation of LODs (Level of Detail) for models upon their import in Unity.
    /// </summary>
    public class ModelImporterLODGenerator : AssetPostprocessor
    {
        public static bool saveAssets { set; get; }
        public static bool enabled { set; get; }
        public static Type meshSimplifierType { set; get; }
        public static int maxLOD { set; get; }
        public static int initialLODMaxPolyCount { set; get; }

        const HideFlags k_DefaultHideFlags = HideFlags.None;

        static List<string> s_ModelAssetsProcessed = new List<string>();

        public static bool IsEditable(string assetPath)
        {
            var attributes = File.GetAttributes(assetPath);

            return AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.ForceUpdate)
                && (attributes & FileAttributes.ReadOnly) == 0;
        }

        /// <summary>
        /// Processes the imported model to generate LODs if necessary.
        /// This method is called automatically after a model is imported into Unity.
        /// It checks if LODs need to be generated, calculates polygon counts, and creates LOD meshes.
        /// It also handles saving LOD data and creating LOD groups on the imported GameObject.
        /// </summary>
        /// <param name="go">The imported GameObject.</param>
        void OnPostprocessModel(GameObject go)
        {
            // Check if the model should be processed based on the absence of an LODGroup and other conditions.
            if (!ShouldProcessModel(go))
                return;

            // Retrieve LOD data associated with the asset path.
            var lodData = GetLODData(assetPath);
            var importSettings = lodData.importSettings;

            // If LOD generation is enabled in the import settings.
            if (importSettings.generateOnImport)
            {
                // Skip processing if the model contains SkinnedMeshRenderer components.
                if (HasSkinnedMeshRenderer(go))
                    return;

                // Get all MeshFilters in the imported GameObject.
                var originalMeshFilters = go.GetComponentsInChildren<MeshFilter>();
                
                // Calculate the total polygon count of the meshes.
                uint polyCount = CalculatePolyCount(originalMeshFilters);

                var meshLODs = new List<IMeshLOD>();
                var preprocessMeshes = new HashSet<int>();

                // Process the initial LOD if the polygon count exceeds the maximum allowed for the initial LOD.
                if (polyCount > importSettings.initialLODMaxPolyCount)
                    ProcessInitialLOD(originalMeshFilters, importSettings, polyCount, meshLODs, preprocessMeshes);

                // Clear previous LOD data to prepare for new LOD generation.
                ClearPreviousLODData(lodData);

                // Set LOD 0 to the original renderers.
                lodData[0] = originalMeshFilters.Select(mf => mf.GetComponent<Renderer>()).ToArray();

                // Generate LOD meshes and add them to the LOD data.
                GenerateLODs(go, originalMeshFilters, importSettings, lodData, meshLODs);

                // Append LOD names to the renderers for identification.
                AppendLODNameToRenderers(go.transform, 0);

                // Save the generated LOD data to the asset database.
                SaveLODData(assetPath, lodData, meshLODs, preprocessMeshes);

                // Track the processed asset path to handle post-processing actions.
                s_ModelAssetsProcessed.Add(assetPath);
            }
            else
            {
                // Handle custom LODs specified in the LOD data.
                HandleCustomLODs(go, lodData);
            }

            // Create and configure an LODGroup component on the GameObject.
            CreateLODGroup(go, lodData);
        }

        
        /// <summary>
        /// Checks if the model should be processed based on certain conditions.
        /// </summary>
        /// <param name="go">The GameObject to check.</param>
        /// <returns>True if the model should be processed; otherwise, false.</returns>
        bool ShouldProcessModel(GameObject go)
        {
            return !go.GetComponentInChildren<LODGroup>() && meshSimplifierType != null && IsEditable(assetPath);
        }

        /// <summary>
        /// Checks if the GameObject contains any SkinnedMeshRenderer components.
        /// </summary>
        /// <param name="go">The GameObject to check.</param>
        /// <returns>True if a SkinnedMeshRenderer is found; otherwise, false.</returns>
        bool HasSkinnedMeshRenderer(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>().Any())
            {
                Debug.LogWarning("Automatic LOD generation on skinned meshes is not currently supported");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Calculates the total number of polygons in the mesh filters.
        /// </summary>
        /// <param name="meshFilters">The array of MeshFilters.</param>
        /// <returns>The total polygon count.</returns>
        uint CalculatePolyCount(MeshFilter[] meshFilters)
        {
            uint polyCount = 0;
            foreach (var mf in meshFilters)
            {
                var m = mf.sharedMesh;
                for (int i = 0; i < m.subMeshCount; i++)
                {
                    var topology = m.GetTopology(i);
                    var indexCount = m.GetIndexCount(i);
                    indexCount = AdjustIndexCountByTopology(topology, indexCount);
                    polyCount += indexCount;
                }
            }
            return polyCount;
        }

        /// <summary>
        /// Adjusts the index count based on the mesh topology.
        /// </summary>
        /// <param name="topology">The mesh topology.</param>
        /// <param name="indexCount">The original index count.</param>
        /// <returns>The adjusted index count.</returns>
        uint AdjustIndexCountByTopology(MeshTopology topology, uint indexCount)
        {
            switch (topology)
            {
                case MeshTopology.Quads:
                    return indexCount / 4;
                case MeshTopology.Triangles:
                    return indexCount / 3;
                case MeshTopology.Lines:
                case MeshTopology.LineStrip:
                    return indexCount / 2;
                default:
                    return indexCount;
            }
        }
        
        /// <summary>
        /// Processes the initial LOD by simplifying the meshes.
        /// </summary>
        /// <param name="originalMeshFilters">The array of original MeshFilters.</param>
        /// <param name="importSettings">The import settings for LOD generation.</param>
        /// <param name="polyCount">The total polygon count.</param>
        /// <param name="meshLODs">The list to store the generated mesh LODs.</param>
        /// <param name="preprocessMeshes">The set of meshes to preprocess.</param>
        void ProcessInitialLOD(MeshFilter[] originalMeshFilters, LODImportSettings importSettings, uint polyCount, List<IMeshLOD> meshLODs, HashSet<int> preprocessMeshes)
        {
            var simplifierType = Type.GetType(importSettings.meshSimplifier) ?? meshSimplifierType;
            foreach (var mf in originalMeshFilters)
            {
                var inputMesh = mf.sharedMesh;

                var outputMesh = new Mesh
                {
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                    name = inputMesh.name,
                    bounds = inputMesh.bounds
                };
                mf.sharedMesh = outputMesh;

                var meshLOD = MeshLOD.GetGenericInstance(simplifierType);
                meshLOD.InputMesh = inputMesh;
                meshLOD.OutputMesh = outputMesh;
                meshLOD.Quality = (float)importSettings.initialLODMaxPolyCount / (float)polyCount;
                meshLODs.Add(meshLOD);

                preprocessMeshes.Add(outputMesh.GetInstanceID());
            }
        }
        
        /// <summary>
        /// Clears previous LOD data to prepare for new LOD generation.
        /// </summary>
        /// <param name="lodData">The LODData to clear.</param>
        void ClearPreviousLODData(LODData lodData)
        {
            for (int i = 0; i <= LODData.MaxLOD; i++)
            {
                lodData[i] = null;
            }
        }
        
        /// <summary>
        /// Generates LODs for the given mesh filters.
        /// </summary>
        /// <param name="go">The GameObject to generate LODs for.</param>
        /// <param name="originalMeshFilters">The array of original MeshFilters.</param>
        /// <param name="importSettings">The import settings for LOD generation.</param>
        /// <param name="lodData">The LODData to store generated LODs.</param>
        /// <param name="meshLODs">The list to store the generated mesh LODs.</param>
        void GenerateLODs(GameObject go, MeshFilter[] originalMeshFilters, LODImportSettings importSettings, LODData lodData, List<IMeshLOD> meshLODs)
        {
            for (int i = 1; i <= importSettings.maxLODGenerated; i++)
            {
                var lodMeshes = new List<Renderer>();

                foreach (var mf in originalMeshFilters)
                {
                    var inputMesh = mf.sharedMesh;

                    var lodTransform = CreateLODTransform(mf, i);
                    var lodMF = lodTransform.GetComponent<MeshFilter>();
                    var lodRenderer = lodTransform.GetComponent<MeshRenderer>();

                    var outputMesh = new Mesh
                    {
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                        name = $"{inputMesh.name} LOD{i}",
                        bounds = inputMesh.bounds
                    };
                    lodMF.sharedMesh = outputMesh;

                    lodMeshes.Add(lodRenderer);

                    CopyRendererSettings(mf, lodRenderer);

                    var meshLOD = MeshLOD.GetGenericInstance(meshSimplifierType);
                    meshLOD.InputMesh = inputMesh;
                    meshLOD.OutputMesh = outputMesh;
                    meshLOD.Quality = Mathf.Pow(0.5f, i);
                    meshLODs.Add(meshLOD);
                }

                lodData[i] = lodMeshes.ToArray();
            }
        }

        /// <summary>
        /// Creates a new LOD transform for the given MeshFilter.
        /// </summary>
        /// <param name="mf">The original MeshFilter.</param>
        /// <param name="lodIndex">The LOD index.</param>
        /// <returns>The created LOD transform.</returns>
        Transform CreateLODTransform(MeshFilter mf, int lodIndex)
        {
            var lodTransform = EditorUtility.CreateGameObjectWithHideFlags(mf.name, k_DefaultHideFlags, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            lodTransform.parent = mf.transform;
            lodTransform.localPosition = Vector3.zero;
            lodTransform.localRotation = Quaternion.identity;
            lodTransform.localScale = Vector3.one;
            AppendLODNameToRenderer(lodTransform.GetComponent<Renderer>(), lodIndex);
            return lodTransform;
        }

        /// <summary>
        /// Copies the settings from one renderer to another.
        /// </summary>
        /// <param name="mf">The source MeshFilter.</param>
        /// <param name="lodRenderer">The destination MeshRenderer.</param>
        void CopyRendererSettings(MeshFilter mf, MeshRenderer lodRenderer)
        {
            EditorUtility.CopySerialized(mf.GetComponent<MeshRenderer>(), lodRenderer);
        }

        /// <summary>
        /// Saves the generated LOD data to the asset database.
        /// </summary>
        /// <param name="assetPath">The path to the asset.</param>
        /// <param name="lodData">The LODData to save.</param>
        /// <param name="meshLODs">The list of generated mesh LODs.</param>
        /// <param name="preprocessMeshes">The set of meshes to preprocess.</param>
        void SaveLODData(string assetPath, LODData lodData, List<IMeshLOD> meshLODs, HashSet<int> preprocessMeshes)
        {
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(lodData)))
            {
                AssetDatabase.CreateAsset(lodData, GetLODDataPath(assetPath));
            }
            else
            {
                var objects = AssetDatabase.LoadAllAssetsAtPath(GetLODDataPath(assetPath));
                foreach (var o in objects)
                {
                    if (o is Mesh mesh)
                        UnityObject.DestroyImmediate(mesh, true);
                }
                EditorUtility.SetDirty(lodData);
            }
            meshLODs.ForEach(ml => AssetDatabase.AddObjectToAsset(ml.OutputMesh, lodData));
            if (saveAssets)
                AssetDatabase.SaveAssets();

            ProcessMeshLODDependencies(meshLODs, preprocessMeshes);
        }
        
        /// <summary>
        /// Processes dependencies for the mesh LODs.
        /// </summary>
        /// <param name="meshLODs">The list of mesh LODs.</param>
        /// <param name="preprocessMeshes">The set of meshes to preprocess.</param>
        void ProcessMeshLODDependencies(List<IMeshLOD> meshLODs, HashSet<int> preprocessMeshes)
        {
            if (preprocessMeshes.Count > 0)
            {
                var jobDependencies = new List<JobHandle>();
                meshLODs.RemoveAll(ml =>
                {
                    if (preprocessMeshes.Contains(ml.OutputMesh.GetInstanceID()))
                    {
                        jobDependencies.Add(ml.Generate());
                        return true;
                    }
                    return false;
                });

                foreach (var ml in meshLODs)
                {
                    MonoBehaviourHelper.StartCoroutine(ml.GenerateAfterDependencies(jobDependencies));
                }
            }
            else
            {
                foreach (var ml in meshLODs)
                {
                    ml.Generate();
                }
            }
        }
        
        /// <summary>
        /// Handles custom LODs for the given GameObject.
        /// </summary>
        /// <param name="go">The GameObject to handle custom LODs for.</param>
        /// <param name="lodData">The LODData to update.</param>
        void HandleCustomLODs(GameObject go, LODData lodData)
        {
            lodData[0] = go.GetComponentsInChildren<MeshFilter>().Select(mf =>
            {
                var r = mf.GetComponent<Renderer>();
                AppendLODNameToRenderer(r, 0);
                return r;
            }).ToArray();

            for (int i = 1; i <= LODData.MaxLOD; i++)
            {
                var renderers = lodData[i];
                for (int j = 0; j < renderers.Length; j++)
                {
                    var r = renderers[j];
                    var lodTransform = CreateLODTransformForCustomLODs(go, r, i);
                    renderers[j] = lodTransform.GetComponent<MeshRenderer>();
                }
            }
        }
        
        /// <summary>
        /// Creates a new LOD transform for custom LODs.
        /// </summary>
        /// <param name="go">The GameObject to create the LOD transform for.</param>
        /// <param name="r">The original renderer.</param>
        /// <param name="lodIndex">The LOD index.</param>
        /// <returns>The created LOD transform.</returns>
        Transform CreateLODTransformForCustomLODs(GameObject go, Renderer r, int lodIndex)
        {
            var lodTransform = EditorUtility.CreateGameObjectWithHideFlags(r.name, k_DefaultHideFlags, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            lodTransform.parent = go.transform;
            lodTransform.localPosition = Vector3.zero;

            var lodMF = lodTransform.GetComponent<MeshFilter>();
            var lodRenderer = lodTransform.GetComponent<MeshRenderer>();

            EditorUtility.CopySerialized(r.GetComponent<MeshFilter>(), lodMF);
            EditorUtility.CopySerialized(r, lodRenderer);

            AppendLODNameToRenderer(lodRenderer, lodIndex);
            return lodTransform;
        }
        
        /// <summary>
        /// Creates an LOD group for the GameObject.
        /// </summary>
        /// <param name="go">The GameObject to add the LOD group to.</param>
        /// <param name="lodData">The LODData to use for the LOD group.</param>
        void CreateLODGroup(GameObject go, LODData lodData)
        {
            var lodGroup = go.AddComponent<LODGroup>();
            var lods = new List<LOD>();
            int maxLODFound = GetMaxLODFound(lodData);

            var importerRef = new SerializedObject(assetImporter);
            var importerLODLevels = importerRef.FindProperty("m_LODScreenPercentages");

            for (int i = 0; i <= maxLODFound; i++)
            {
                var lod = new LOD { renderers = lodData[i], screenRelativeTransitionHeight = GetScreenPercentage(i, maxLODFound, importerLODLevels) };
                lods.Add(lod);
            }

            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();

            SyncImporterLODLevels(importerRef, importerLODLevels, lods);

            if (importerLODLevels.arraySize != 0 && maxLODFound != importerLODLevels.arraySize - 1)
            {
                Debug.LogWarning("The model has an existing LOD group, but its settings will not be used because the specified LOD count in the AutoLOD settings is different.");
            }
        }
        
        /// <summary>
        /// Gets the maximum LOD index found in the LOD data.
        /// </summary>
        /// <param name="lodData">The LODData to check.</param>
        /// <returns>The maximum LOD index found.</returns>
        int GetMaxLODFound(LODData lodData)
        {
            int maxLODFound = -1;
            for (int i = 0; i <= LODData.MaxLOD; i++)
            {
                if (lodData[i] == null || lodData[i].Length == 0)
                    break;

                maxLODFound++;
            }
            return maxLODFound;
        }

        /// <summary>
        /// Gets the screen percentage for the given LOD index.
        /// </summary>
        /// <param name="index">The LOD index.</param>
        /// <param name="maxLODFound">The maximum LOD index found.</param>
        /// <param name="importerLODLevels">The SerializedProperty representing LOD screen percentages.</param>
        /// <returns>The screen percentage for the given LOD index.</returns>
        float GetScreenPercentage(int index, int maxLODFound, SerializedProperty importerLODLevels)
        {
            if (index == maxLODFound)
                return 0.01f;

            if (index < importerLODLevels.arraySize && maxLODFound == importerLODLevels.arraySize)
            {
                return importerLODLevels.GetArrayElementAtIndex(index).floatValue;
            }

            return Mathf.Pow(0.5f, index + 1);
        }

        /// <summary>
        /// Synchronizes the importer LOD levels with the LOD data.
        /// </summary>
        /// <param name="importerRef">The SerializedObject representing the asset importer.</param>
        /// <param name="importerLODLevels">The SerializedProperty representing LOD screen percentages.</param>
        /// <param name="lods">The list of LODs.</param>
        void SyncImporterLODLevels(SerializedObject importerRef, SerializedProperty importerLODLevels, List<LOD> lods)
        {
            importerLODLevels.ClearArray();
            for (int i = 0; i < lods.Count; i++)
            {
                var lod = lods[i];
                importerLODLevels.InsertArrayElementAtIndex(i);
                importerLODLevels.GetArrayElementAtIndex(i).floatValue = lod.screenRelativeTransitionHeight;
            }
            importerRef.ApplyModifiedPropertiesWithoutUndo();
        }
        
        /// <summary>
        /// Invoked after all asset imports, deletes, and moves are processed.
        /// Ensures any LOD data modifications are saved back to the asset database.
        /// </summary>
        /// <param name="importedAssets">Array of paths for the imported assets.</param>
        /// <param name="deletedAssets">Array of paths for the deleted assets.</param>
        /// <param name="movedAssets">Array of paths for the assets moved within the project.</param>
        /// <param name="movedFromAssetPaths">Array of original paths for the moved assets.</param>
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool assetsImported = false;

            foreach (var asset in importedAssets)
            {
                if (s_ModelAssetsProcessed.Remove(asset))
                {
                    var go = (GameObject)AssetDatabase.LoadMainAssetAtPath(asset);
                    var lodData = AssetDatabase.LoadAssetAtPath<LODData>(GetLODDataPath(asset));

                    var lodGroup = go.GetComponentInChildren<LODGroup>();
                    var lods = lodGroup.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                    {
                        var lod = lods[i];
                        lodData[i] = lod.renderers;
                    }

                    EditorUtility.SetDirty(lodData);
                    assetsImported = true;
                }
            }

            if (assetsImported && saveAssets)
                AssetDatabase.SaveAssets();
        }

        #region Aux Methods
        /// <summary>
        /// Gets the LODData path for the specified asset path.
        /// </summary>
        internal static string GetLODDataPath(string assetPath)
        {
            var pathPrefix = Path.GetDirectoryName(assetPath) + Path.DirectorySeparatorChar + Path.GetFileNameWithoutExtension(assetPath);
            return pathPrefix + "_lods.asset";
        }

        /// <summary>
        /// Gets the LODData for the specified asset
        /// </summary>
        internal static LODData GetLODData(string assetPath)
        {
            var lodData = AssetDatabase.LoadAssetAtPath<LODData>(GetLODDataPath(assetPath));
            if (!lodData)
                lodData = ScriptableObject.CreateInstance<LODData>();

            var overrideDefaults = lodData.overrideDefaults;

            var importSettings = lodData.importSettings;
            if (importSettings == null)
            {
                importSettings = new LODImportSettings();
                lodData.importSettings = importSettings;
            }

            if (!overrideDefaults)
            {
                importSettings.generateOnImport = enabled;
                importSettings.meshSimplifier = meshSimplifierType.AssemblyQualifiedName;
                importSettings.maxLODGenerated = maxLOD;
                importSettings.initialLODMaxPolyCount = initialLODMaxPolyCount;
            }

            return lodData;
        }

        /// <summary>
        /// Appends the LOD name to the renderer.
        /// </summary>
        static void AppendLODNameToRenderers(Transform root, int lod)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                AppendLODNameToRenderer(r, lod);
            }
        }

        /// <summary>
        /// Appends the LOD name to the renderer
        /// </summary>
        static void AppendLODNameToRenderer(Renderer r, int lod)
        {
            if (r.name.IndexOf("_LOD", StringComparison.OrdinalIgnoreCase) == -1)
                r.name = string.Format("{0}_LOD{1}", r.name, lod);
        }
        #endregion
    }
}
