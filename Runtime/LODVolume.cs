using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AutoLOD;
using Unity.AutoLOD.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AutoLOD
{
    [RequiresLayer(HLODLayer)]
    public partial class LODVolume : MonoBehaviour
    {
        public const string HLODLayer = "HLOD";
        
        public LODGroupHelper lodGroup
        {
            get
            {
                if (m_LODGroupHelper == null)
                {
                    m_LODGroupHelper = new LODGroupHelper();
                    m_LODGroupHelper.lodGroup = GetComponent<LODGroup>();
                }

                return m_LODGroupHelper;
            }
        }

        public bool dirty;
        public Bounds bounds;
        public GameObject hlodRoot;
        public List<Renderer> renderers = new List<Renderer>();
        public List<LODVolume> childVolumes = new List<LODVolume>();

        public List<object> cached
        {
            get
            {
                if (m_Cached.Count == 0)
                {
                    renderers.RemoveAll(r => r == null);
                    foreach (var r in renderers)
                    {
                        var lg = r.GetComponentInParent<LODGroup>();
                        if (lg)
                        {
                            var lgh = new LODGroupHelper();
                            lgh.lodGroup = lg;
                            m_Cached.Add(lgh);
                        }
                        else
                            m_Cached.Add(r);
                    }
                }

                return m_Cached;
            }
        }

        const HideFlags k_DefaultHideFlags = HideFlags.None;
        const ushort k_VolumeSplitRendererCount = 32; //ushort.MaxValue;
        const string k_DefaultName = "LODVolumeNode";
        const string k_HLODRootContainer = "HLODs";
        const int k_Splits = 2;

        static int s_VolumesCreated;

        LODGroupHelper m_LODGroupHelper;
        List<object> m_Cached = new List<object>();

        IMeshSimplifier m_MeshSimplifier;

        static readonly Color[] k_DepthColors = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.magenta,
            Color.yellow,
            Color.cyan,
            Color.grey,
        };

        void Awake()
        {
            if (Application.isPlaying)
            {
                // Prime helper object properties on start to avoid hitches later
                var primeCached = cached;
                var primeLODGroup = lodGroup;
                var primeTransform = transform;
                if (lodGroup.lodGroup)
                {
                    var primeLODs = lodGroup.lods;
                    var primeMaxLOD = lodGroup.maxLOD;
                    var primeReferencePoint = lodGroup.referencePoint;
                    var primeWorldSize = lodGroup.worldSpaceSize;
                }

                foreach (var c in cached)
                {
                    var lgh = c as LODGroupHelper;
                    if (lgh != null)
                    {
                        var primeLODs = lgh.lods;
                        var primeMaxLOD = lgh.maxLOD;
                        var primeReferencePoint = lgh.referencePoint;
                        var primeWorldSize = lgh.worldSpaceSize;
                    }
                }
            }
        }

        public static LODVolume Create()
        {
            GameObject go = new GameObject(k_DefaultName + s_VolumesCreated++, typeof(LODVolume));
            go.layer = LayerMask.NameToLayer(HLODLayer);
            LODVolume volume = go.GetComponent<LODVolume>();
            return volume;
        }

        public IEnumerator UpdateRenderer(Renderer renderer)
        {
            yield return RemoveRenderer(renderer);

            if (!this)
                yield break;

            var parent = transform.parent;
            var rootVolume = parent ? parent.GetComponentInParent<LODVolume>() : this; // In case the BVH has shrunk
            yield return rootVolume.AddRenderer(renderer);
        }

        public IEnumerator AddRenderer(Renderer renderer)
        {
            if (!this)
                yield break;

            if (!renderer)
                yield break;

            if (renderer.gameObject.layer == LayerMask.NameToLayer(HLODLayer))
                yield break;

            {
                var mf = renderer.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh && mf.sharedMesh.GetTopology(0) != MeshTopology.Triangles)
                    yield break;
            }


            if (renderers.Count == 0 && Mathf.Approximately(bounds.size.magnitude, 0f))
            {
                bounds = renderer.bounds;
                bounds = GetCuboidBounds(bounds);

                transform.position = bounds.min;
            }

            // Each LODVolume maintains it's own list of renderers, which includes renderers in children nodes
            if (WithinBounds(renderer, bounds))
            {
                if (!renderers.Contains(renderer))
                    renderers.Add(renderer);

                if (transform.childCount == 0)
                {
                    if (renderers.Count > k_VolumeSplitRendererCount)
                        yield return Split();
                    else
                        yield return SetDirty();
                }
                else
                {
                    foreach (Transform child in transform)
                    {
                        if (!renderer)
                            yield break;

                        var lodVolume = child.GetComponent<LODVolume>();
                        if (WithinBounds(renderer, lodVolume.bounds))
                        {
                            yield return lodVolume.AddRenderer(renderer);
                            break;
                        }

                        yield return null;
                    }
                }
            }
            else if (!transform.parent)
            {
                if (transform.childCount == 0 && renderers.Count < k_VolumeSplitRendererCount)
                {
                    bounds.Encapsulate(renderer.bounds);
                    bounds = GetCuboidBounds(bounds);
                    if (!renderers.Contains(renderer))
                        renderers.Add(renderer);

                    yield return SetDirty();
                }
                else
                {
                    // Expand and then try to add at the larger bounds
                    var targetBounds = bounds;
                    targetBounds.Encapsulate(renderer.bounds);
                    targetBounds = GetCuboidBounds(targetBounds);
                    yield return Grow(targetBounds);
                    yield return transform.parent.GetComponent<LODVolume>().AddRenderer(renderer);
                }
            }

        }

        public IEnumerator RemoveRenderer(Renderer renderer)
        {
            if (renderers != null && renderers.Remove(renderer))
            {
                foreach (Transform child in transform)
                {
                    var lodVolume = child.GetComponent<LODVolume>();
                    if (lodVolume)
                        yield return lodVolume.RemoveRenderer(renderer);

                    yield return null;
                }

                if (!transform.parent)
                    yield return Shrink();

                if (!this)
                    yield break;

                if (transform.childCount == 0)
                    yield return SetDirty();
            }
        }

        [ContextMenu("Split")]
        void SplitContext()
        {
            MonoBehaviourHelper.StartCoroutine(Split());
        }

        IEnumerator Split()
        {
            Vector3 size = bounds.size;
            size.x /= k_Splits;
            size.y /= k_Splits;
            size.z /= k_Splits;

            for (int i = 0; i < k_Splits; i++)
            {
                for (int j = 0; j < k_Splits; j++)
                {
                    for (int k = 0; k < k_Splits; k++)
                    {
                        var lodVolume = Create();
                        var lodVolumeTransform = lodVolume.transform;
                        lodVolumeTransform.parent = transform;
                        var center = bounds.min + size * 0.5f + Vector3.Scale(size, new Vector3(i, j, k));
                        lodVolumeTransform.position = center;
                        lodVolume.bounds = new Bounds(center, size);

                        foreach (var r in renderers)
                        {
                            if (r && WithinBounds(r, lodVolume.bounds))
                            {
                                lodVolume.renderers.Add(r);
                                yield return lodVolume.SetDirty();
                            }
                        }

                        yield return null;
                    }
                }
            }
        }

        [ContextMenu("Grow")]
        void GrowContext()
        {
            var targetBounds = bounds;
            targetBounds.center += Vector3.up;
            MonoBehaviourHelper.StartCoroutine(Grow(targetBounds));
        }

        IEnumerator Grow(Bounds targetBounds)
        {
            var direction = Vector3.Normalize(targetBounds.center - bounds.center);
            Vector3 size = bounds.size;
            size.x *= k_Splits;
            size.y *= k_Splits;
            size.z *= k_Splits;

            var corners = new Vector3[]
            {
                bounds.min,
                bounds.min + Vector3.right * bounds.size.x,
                bounds.min + Vector3.forward * bounds.size.z,
                bounds.min + Vector3.up * bounds.size.y,
                bounds.min + Vector3.right * bounds.size.x + Vector3.forward * bounds.size.z,
                bounds.min + Vector3.right * bounds.size.x + Vector3.up * bounds.size.y,
                bounds.min + Vector3.forward * bounds.size.x + Vector3.up * bounds.size.y,
                bounds.min + Vector3.right * bounds.size.x + Vector3.forward * bounds.size.z +
                Vector3.up * bounds.size.y
            };

            // Determine where the current volume is situated in the new expanded volume
            var best = 0f;
            var expandedVolumeCenter = bounds.min;
            foreach (var c in corners)
            {
                var dot = Vector3.Dot(c, direction);
                if (dot > best)
                {
                    best = dot;
                    expandedVolumeCenter = c;
                }

                yield return null;
            }

            var expandedVolume = Create();
            var expandedVolumeTransform = expandedVolume.transform;
            expandedVolumeTransform.position = expandedVolumeCenter;
            expandedVolume.bounds = new Bounds(expandedVolumeCenter, size);
            expandedVolume.renderers = new List<Renderer>(renderers);
            var expandedBounds = expandedVolume.bounds;

            transform.parent = expandedVolumeTransform;

            var splitSize = bounds.size;
            var currentCenter = bounds.center;
            for (int i = 0; i < k_Splits; i++)
            {
                for (int j = 0; j < k_Splits; j++)
                {
                    for (int k = 0; k < k_Splits; k++)
                    {
                        var center = expandedBounds.min + splitSize * 0.5f +
                                     Vector3.Scale(splitSize, new Vector3(i, j, k));
                        if (Mathf.Approximately(Vector3.Distance(center, currentCenter), 0f))
                            continue; // Skip the existing LODVolume we are growing from

                        var lodVolume = Create();
                        var lodVolumeTransform = lodVolume.transform;
                        lodVolumeTransform.parent = expandedVolumeTransform;
                        lodVolumeTransform.position = center;
                        lodVolume.bounds = new Bounds(center, splitSize);
                    }
                }
            }
        }

        IEnumerator Shrink()
        {
            var populatedChildrenNodes = 0;
            foreach (Transform child in transform)
            {
                var lodVolume = child.GetComponent<LODVolume>();
                var renderers = lodVolume.renderers;
                if (renderers != null && renderers.Count > 0 && renderers.Count(r => r != null) > 0)
                    populatedChildrenNodes++;

                yield return null;
            }

            if (populatedChildrenNodes <= 1)
            {
                var lodVolumes = GetComponentsInChildren<LODVolume>();
                LODVolume newRootVolume = null;
                if (lodVolumes.Length > 0)
                {
                    newRootVolume = lodVolumes[lodVolumes.Length - 1];
                    newRootVolume.transform.parent = null;
                }

                // Clean up child HLODs before destroying the GameObject; Otherwise, we'd leak into the scene
                foreach (var lodVolume in lodVolumes)
                {
                    if (lodVolume != newRootVolume)
                        lodVolume.CleanupHLOD();
                }

                DestroyImmediate(gameObject);

                if (newRootVolume)
                    yield return newRootVolume.Shrink();
            }
        }

        IEnumerator SetDirty()
        {
            dirty = true;

            childVolumes.Clear();
            foreach (Transform child in transform)
            {
                var cv = child.GetComponent<LODVolume>();
                if (cv)
                    childVolumes.Add(cv);
            }

            cached.Clear();

            var lodVolumeParent = transform.parent;
            var parentLODVolume = lodVolumeParent ? lodVolumeParent.GetComponentInParent<LODVolume>() : null;
            if (parentLODVolume)
                yield return parentLODVolume.SetDirty();
        }

        static Bounds GetCuboidBounds(Bounds bounds)
        {
            // Expand bounds side lengths to maintain a cube
            var maxSize = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), bounds.size.z);
            var extents = Vector3.one * maxSize * 0.5f;
            bounds.center = bounds.min + extents;
            bounds.extents = extents;

            return bounds;
        }

        void OnDrawGizmos()
        {
            var depth = GetDepth(transform);
            DrawGizmos(Mathf.Max(1f - Mathf.Pow(0.9f, depth), 0.2f), GetDepthColor(depth));
        }


        void DrawGizmos(float alpha, Color color)
        {
            color.a = alpha;
            Gizmos.color = color;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
        

        void CleanupHLOD()
        {
            if (hlodRoot) // Clean up old HLOD
            {
#if UNITY_EDITOR
                var mf = hlodRoot.GetComponent<MeshFilter>();
                if (mf)
                    DestroyImmediate(mf.sharedMesh, true); // Clean up file on disk
#endif
                DestroyImmediate(hlodRoot);
            }
        }

        public static int GetDepth(Transform transform)
        {
            int count = 0;
            Transform parent = transform.parent;
            while (parent)
            {
                count++;
                parent = parent.parent;
            }

            return count;
        }

        public static Color GetDepthColor(int depth)
        {
            return k_DepthColors[depth % k_DepthColors.Length];
        }

        static bool WithinBounds(Renderer r, Bounds bounds)
        {
            // Use this approach if we are not going to split meshes and simply put the object in one volume or another
            return Mathf.Approximately(bounds.size.magnitude, 0f) || bounds.Contains(r.bounds.center);
        }
    }
}