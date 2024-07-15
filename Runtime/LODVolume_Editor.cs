using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AutoLOD;
using Unity.AutoLOD.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AutoLOD
{
    public partial class LODVolume: MonoBehaviour
    {

#if UNITY_EDITOR
        [ContextMenu("GenerateHLOD")]
        void GenerateHLODContext()
        {
            MonoBehaviourHelper.StartCoroutine(GenerateHLOD());
        }

        public IEnumerator UpdateHLODs()
        {
            // Process children first, since we are now combining children HLODs to make parent HLODs
            foreach (Transform child in transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume)
                    yield return childLODVolume.UpdateHLODs();

                if (!this)
                    yield break;
            }

            if (dirty)
            {
                yield return GenerateHLOD(false);
                dirty = false;
            }
        }

        public IEnumerator GenerateHLOD(bool propagateUpwards = true)
        {
            var mergeChildrenVolumes = false;
            HashSet<Renderer> hlodRenderers = new HashSet<Renderer>();

            var rendererMaterials = renderers.SelectMany(r => r.sharedMaterials);
            yield return null;

            foreach (Transform child in transform)
            {
                var childLODVolume = child.GetComponent<LODVolume>();
                if (childLODVolume && childLODVolume.renderers.Count > 0)
                {
                    if (rendererMaterials.Except(childLODVolume.renderers.SelectMany(r => r.sharedMaterials)).Any())
                    {
                        hlodRenderers.Clear();
                        mergeChildrenVolumes = false;
                        break;
                    }

                    var childHLODRoot = childLODVolume.hlodRoot;
                    if (childHLODRoot)
                    {
                        var childHLODRenderer = childHLODRoot.GetComponent<Renderer>();
                        if (childHLODRenderer)
                        {
                            mergeChildrenVolumes = true;
                            hlodRenderers.Add(childHLODRenderer);
                            continue;
                        }
                    }

                    hlodRenderers.Clear();
                    mergeChildrenVolumes = false;
                    break;
                }

                yield return null;
            }

            if (!mergeChildrenVolumes)
            {
                foreach (var r in renderers)
                {
                    var mr = r as MeshRenderer;
                    if (mr)
                    {
                        // Use the coarsest LOD if it exists
                        var mrLODGroup = mr.GetComponentInParent<LODGroup>();
                        if (mrLODGroup)
                        {
                            var mrLODs = mrLODGroup.GetLODs();
                            var maxLOD = mrLODGroup.GetMaxLOD();
                            var mrLOD = mrLODs[maxLOD];
                            foreach (var lr in mrLOD.renderers)
                            {
                                if (lr && lr.GetComponent<MeshFilter>())
                                    hlodRenderers.Add(lr);
                            }
                        }
                        else if (mr.GetComponent<MeshFilter>())
                        {
                            hlodRenderers.Add(mr);
                        }
                    }

                    yield return null;
                }
            }

            var lodRenderers = new List<Renderer>();
            CleanupHLOD();

            GameObject hlodRootContainer = null;
            yield return ObjectUtils.FindGameObject(k_HLODRootContainer, root =>
            {
                if (root)
                    hlodRootContainer = root;
            });

            if (!hlodRootContainer)
                hlodRootContainer = new GameObject(k_HLODRootContainer);

            var hlodLayer = LayerMask.NameToLayer(HLODLayer);

            hlodRoot = new GameObject("HLOD");
            hlodRoot.layer = hlodLayer;
            hlodRoot.transform.parent = hlodRootContainer.transform;

            if (mergeChildrenVolumes)
            {
                Material sharedMaterial = null;

                CombineInstance[] combine = new CombineInstance[hlodRenderers.Count];
                var i = 0;
                foreach (var r in hlodRenderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var ci = new CombineInstance();
                    ci.transform = r.transform.localToWorldMatrix;
                    ci.mesh = mf.sharedMesh;
                    combine[i] = ci;

                    if (sharedMaterial == null)
                        sharedMaterial = r.sharedMaterial;

                    i++;
                }

                var sharedMesh = new Mesh();
                sharedMesh.indexFormat = IndexFormat.UInt32;
                sharedMesh.CombineMeshes(combine, true, true);
                sharedMesh.RecalculateBounds();
                var meshFilter = hlodRoot.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = sharedMesh;

                var meshRenderer = hlodRoot.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = sharedMaterial;
            }
            else
            {
                var parent = hlodRoot.transform;
                foreach (var r in hlodRenderers)
                {
                    var rendererTransform = r.transform;

                    var child = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer));
                    child.layer = hlodLayer;
                    var childTransform = child.transform;
                    childTransform.SetPositionAndRotation(rendererTransform.position, rendererTransform.rotation);
                    childTransform.localScale = rendererTransform.lossyScale;
                    childTransform.SetParent(parent, true);

                    var mr = child.GetComponent<MeshRenderer>();
                    EditorUtility.CopySerialized(r.GetComponent<MeshFilter>(), child.GetComponent<MeshFilter>());
                    EditorUtility.CopySerialized(r.GetComponent<MeshRenderer>(), mr);

                    lodRenderers.Add(mr);
                }
            }

            LOD lod = new LOD();

            var lodGroup = GetComponent<LODGroup>();
            if (!lodGroup)
                lodGroup = gameObject.AddComponent<LODGroup>();
            this.lodGroup.lodGroup = lodGroup;

            if (!mergeChildrenVolumes)
            {
                var batcher = (IBatcher)Activator.CreateInstance(AutoLODSettingsData.Instance.BatcherType);
                yield return batcher.Batch(hlodRoot);
            }

            lod.renderers = hlodRoot.GetComponentsInChildren<Renderer>(false);
            lodGroup.SetLODs(new LOD[] { lod });

            if (propagateUpwards)
            {
                var lodVolumeParent = transform.parent;
                var parentLODVolume = lodVolumeParent ? lodVolumeParent.GetComponentInParent<LODVolume>() : null;
                if (parentLODVolume)
                    yield return parentLODVolume.GenerateHLOD();
            }
        }

        [ContextMenu("GenerateLODs")]
        void GenerateLODsContext()
        {
            GenerateLODs();
        }

        void GenerateLODs()
        {
            int maxLOD = 1;
            var go = gameObject;

            var hlodLayer = LayerMask.NameToLayer(HLODLayer);

            var lodGroup = go.GetComponent<LODGroup>();
            if (lodGroup)
            {
                var lods = new LOD[maxLOD + 1];
                var lod0 = lodGroup.GetLODs()[0];
                lod0.screenRelativeTransitionHeight = 0.5f;
                lods[0] = lod0;

                var meshes = new List<Mesh>();

                var totalMeshCount = maxLOD * lod0.renderers.Length;
                for (int l = 1; l <= maxLOD; l++)
                {
                    var lodRenderers = new List<MeshRenderer>();
                    foreach (var mr in lod0.renderers)
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        var sharedMesh = mf.sharedMesh;

                        var lodTransform = EditorUtility.CreateGameObjectWithHideFlags(
                            string.Format("{0} LOD{1}", sharedMesh.name, l),
                            k_DefaultHideFlags, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                        lodTransform.gameObject.layer = hlodLayer;
                        lodTransform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                        lodTransform.localScale = mf.transform.lossyScale;
                        lodTransform.SetParent(mf.transform, true);

                        var lodMF = lodTransform.GetComponent<MeshFilter>();
                        var lodRenderer = lodTransform.GetComponent<MeshRenderer>();

                        lodRenderers.Add(lodRenderer);

                        EditorUtility.CopySerialized(mf, lodMF);
                        EditorUtility.CopySerialized(mf.GetComponent<MeshRenderer>(), lodRenderer);

                        var simplifiedMesh = new Mesh();
                        simplifiedMesh.name = sharedMesh.name + string.Format(" LOD{0}", l);
                        lodMF.sharedMesh = simplifiedMesh;
                        meshes.Add(simplifiedMesh);

                        var meshLOD = MeshLOD.GetGenericInstance(AutoLODSettingsData.Instance.MeshSimplifierType);
                        meshLOD.InputMesh = sharedMesh;
                        meshLOD.OutputMesh = simplifiedMesh;
                        meshLOD.Quality = Mathf.Pow(0.5f, l);
                    }

                    var lod = lods[l];
                    lod.renderers = lodRenderers.ToArray();
                    lod.screenRelativeTransitionHeight = l == maxLOD ? 0.01f : Mathf.Pow(0.5f, l + 1);
                    lods[l] = lod;
                }

                lodGroup.ForceLOD(0);
                lodGroup.SetLODs(lods.ToArray());
                lodGroup.RecalculateBounds();
                lodGroup.ForceLOD(-1);

                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefab)
                {
                    var assetPath = AssetDatabase.GetAssetPath(prefab);
                    var pathPrefix = Path.GetDirectoryName(assetPath) + Path.DirectorySeparatorChar +
                                     Path.GetFileNameWithoutExtension(assetPath);
                    var lodsAssetPath = pathPrefix + "_lods.asset";
                    ObjectUtils.CreateAssetFromObjects(meshes.ToArray(), lodsAssetPath);
                }
            }
        }
#endif
    }
}