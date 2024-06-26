﻿
using Unity.Collections;
#if ENABLE_INSTALOD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using InstaLOD;
using UnityEditor;
using UnityEngine;
using Unity.AutoLOD;
using UnityObject = UnityEngine.Object;
#endif

#if ENABLE_INSTALOD
namespace Unity.AutoLOD
{
    public struct InstaLODMeshSimplifier : IMeshSimplifier, IPreferences
    {
        static object executionLock = new object();

        public void Simplify(WorkingMesh inputMesh, WorkingMesh outputMesh, float quality)
        {
            Renderer renderer = null;

            MonoBehaviourHelper.ExecuteOnMainThread(() =>
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("Temp", HideFlags.HideAndDontSave, typeof(MeshRenderer), typeof(MeshFilter));
                var mf = go.GetComponent<MeshFilter>();
                var mesh = new Mesh();
                inputMesh.ApplyToMesh(mesh);
                mf.sharedMesh = mesh;
                renderer = go.GetComponent<MeshRenderer>();
                var material = new Material(Shader.Find("Standard"));
                var sharedMaterials = new Material[mesh.subMeshCount];
                for (int i = 0; i < mesh.subMeshCount; i++)
                    sharedMaterials[i] = material;
                renderer.sharedMaterials = sharedMaterials;
                renderer.enabled = false;
            });

            var settings = new InstaLODOptimizeSettings(quality);
            settings.PercentTriangles = quality;
            var nativeMeshSettings = new InstaLODNativeMeshOperationSettings(true);
            nativeMeshSettings.hideSourceGameObjects = false;

            lock (executionLock)
            {
                if (!MonoBehaviourHelper.IsMainThread())
                {
                    while (InstaLODNative.currentMeshOperationState != null)
                        Thread.Sleep(100);
                }

                MonoBehaviourHelper.ExecuteOnMainThread(() =>
                {
                    EditorWindow.GetWindow<InstaLODToolkitWindow>(); // Necessary for background processing
                    InstaLODNative.Optimize(new List<Renderer>() { renderer }, settings, nativeMeshSettings);
                    Selection.activeGameObject = null; // Necessary to avoid errors from InstaLOD trying to add settings component to imported model
                });
            }

            while (InstaLODNative.currentMeshOperationState != null)
            {
                if (MonoBehaviourHelper.IsMainThread())
                    InstaLODMainThreadAction.RunMainThreadActions();
                else
                    Thread.Sleep(100);
            }

            MonoBehaviourHelper.ExecuteOnMainThread(() =>
            {
                var mf = renderer.GetComponent<MeshFilter>();
                mf.sharedMesh.ToWorkingMesh(Allocator.Persistent);
                mf.sharedMesh.ApplyToWorkingMesh(ref outputMesh);
                UnityObject.DestroyImmediate(mf.gameObject);
            });
        }

        public void OnPreferencesGUI()
        {
            // Implement the preferences GUI if needed for InstaLOD settings
            GUILayout.Label("InstaLOD settings are configured externally.");
        }
    }
}
#endif