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
using UnityEngine.Serialization;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityObject = UnityEngine.Object;

namespace Unity.AutoLOD
{
    [CreateAssetMenu(fileName = "AutoLODSettingsData", menuName = "AutoLOD/SettingsData")]
    public class AutoLODSettingsData : ScriptableObject
    {
        [NonSerialized] private static AutoLODSettingsData _instance;

        private const string DefaultPath = "Assets/Settings/Editor/AutoLODSettingsData.asset";
        
        public event Action OnSettingsUpdated;

        [SerializeField] private LODImportSettings lodImportSettings = new LODImportSettings();

        [SerializeField] private int maxExecutionTime = AutoLODConst.k_DefaultMaxExecutionTime;
        [SerializeField] private bool saveAssets = true;
        [SerializeField] private bool sceneLODEnabled = true;
        [SerializeField] private bool showVolumeBounds = false;
        [SerializeField] private int maxLOD = AutoLODConst.k_DefaultMaxLOD;
        [SerializeField] private bool useSameMaterialForLODs = false;
        
        [SerializeField] private List<Type> meshSimplifiers;
        [SerializeField] private List<Type> batchers;
        [SerializeField] private IPreferences simplifierPreferences;

        public static AutoLODSettingsData Instance
        {
            get
            {
                if (_instance == null)
                {
                    Init();
                }

                return _instance;
            }
        }

        static void Init()
        {
            string path = EditorPrefs.GetString("AutoLODSettingsPath", DefaultPath);
            _instance = AssetDatabase.LoadAssetAtPath<AutoLODSettingsData>(path);
            if (_instance == null)
            {
                _instance = CreateInstance<AutoLODSettingsData>();
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                AssetDatabase.CreateAsset(_instance, path);
                AssetDatabase.SaveAssets();
            }
            
            _instance.OnSettingsUpdated?.Invoke();
        }

        #region Simplifier
        public Type MeshSimplifierType
        {
            set
            {
                if (typeof(IMeshSimplifier).IsAssignableFrom(value))
                    EditorPrefs.SetString(AutoLODConst.k_DefaultMeshSimplifier, value.AssemblyQualifiedName);
                else if (value == null)
                    EditorPrefs.DeleteKey(AutoLODConst.k_DefaultMeshSimplifier);

                OnSettingsUpdated?.Invoke();
            }
            get
            {
                var type = Type.GetType(EditorPrefs.GetString(AutoLODConst.k_DefaultMeshSimplifier, AutoLODConst.k_DefaultMeshSimplifierDefault));
                
                if (type == null || !typeof(IMeshSimplifier).IsAssignableFrom(type))
                    type = Type.GetType(AutoLODConst.k_DefaultMeshSimplifierDefault);
                
                if (type == null && MeshSimplifiers.Count > 0)
                    type = Type.GetType(MeshSimplifiers[0].AssemblyQualifiedName);
                
                
                return type;
            }
        }

        
        public List<Type> MeshSimplifiers
        {
            get
            {
                if (meshSimplifiers == null || meshSimplifiers.Count == 0)
                {
                    meshSimplifiers = ObjectUtils.GetImplementationsOfInterface(typeof(IMeshSimplifier)).ToList();
                    
#if ENABLE_INSTALOD
                    var instaLODSimplifier = Type.GetType("Unity.AutoLOD.InstaLODMeshSimplifier, Assembly-CSharp-Editor");
                    if (instaLODSimplifier != null)
                    {
                        s_MeshSimplifiers.Add(instaLODSimplifier);
                    }
#endif
                }

                return meshSimplifiers;
            }
        }
        
        
        public string DefaultMeshSimplifier
        {
            get => lodImportSettings.meshSimplifier;
            set
            {
                if (typeof(IMeshSimplifier).IsAssignableFrom(Type.GetType(value)))
                    lodImportSettings.meshSimplifier = value;
                else if (value == null)
                    lodImportSettings.meshSimplifier = AutoLODConst.k_DefaultMeshSimplifierDefault;
                OnSettingsUpdated?.Invoke();
            }
        }
        
        public static IEnumerator GetDefaultSimplifier()
        {
            var statusEnumerator = EnsureDefaultSimplifierInstalled();
            while (statusEnumerator.MoveNext())
                yield return statusEnumerator.Current;

            PackageStatus status = (PackageStatus)statusEnumerator.Current;

            var defineEnumerator = SetDefaultSimplifierDefine(status);
            while (defineEnumerator.MoveNext())
                yield return defineEnumerator.Current;   
        }
        
        /// <summary>
        /// Ensures that the default mesh simplifier is installed.
        /// </summary>
        /// <returns>PackageStatus indicating the status of the mesh simplifier package.</returns>
        static IEnumerator EnsureDefaultSimplifierInstalled()
        {
            PackageStatus status = PackageStatus.Unknown;
            var result = PackageInfo.GetAllRegisteredPackages();
            foreach (var package in result)
            {
                if (package.name == "com.whinarn.unitymeshsimplifier")
                {
                    status = PackageStatus.Available;
                    break;
                }
            }

            if (status != PackageStatus.Available
                && EditorUtility.DisplayDialog("Install Default Mesh Simplifier?",
                    "You are missing a default mesh simplifier. Would you like to install one?",
                    "Yes", "No"))
            {
                var request = Client.Add("https://github.com/Whinarn/UnityMeshSimplifier.git");
                while (!request.IsCompleted)
                    yield return null;

                switch (request.Status)
                {
                    case StatusCode.Success:
                        status = PackageStatus.Available;
                        break;
                    case StatusCode.InProgress:
                        status = PackageStatus.InProgress;
                        break;
                    case StatusCode.Failure:
                        Debug.LogError($"AutoLOD: {request.Error.message}");
                        break;
                }
            }

            if (status != PackageStatus.Available && status != PackageStatus.InProgress)
            {
                Debug.LogError("AutoLOD: You must set a valid Default Mesh Simplifier under Edit -> Preferences");
            }

            yield return status;
        }

        /// <summary>
        /// Sets the scripting define symbol for the default mesh simplifier if it is available.
        /// </summary>
        /// <param name="status">The current status of the mesh simplifier package.</param>
        /// <returns></returns>
        static IEnumerator SetDefaultSimplifierDefine(PackageStatus status)
        {
            if (status == PackageStatus.Available)
            {
                // Cribbed from ConditionalCompilationUtility
                // TODO: Remove when minimum version is 2019 LTS and use define constraints instead
                var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;

                NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
                string previousProjectDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);

                var projectDefines = previousProjectDefines.Split(';').ToList();
                if (!projectDefines.Contains(AutoLODConst.k_DefaultMeshSimplifierDefine, StringComparer.OrdinalIgnoreCase))
                {
                    EditorApplication.LockReloadAssemblies();

                    try
                    {
                        projectDefines.Add(AutoLODConst.k_DefaultMeshSimplifierDefine);

                        // This will trigger another re-compile, which needs to happen, so all the custom attributes will be visible
                        PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", projectDefines.ToArray()));

                        // Let other systems execute before reloading assemblies
                        yield return null;
                    }
                    finally
                    {
                        EditorApplication.UnlockReloadAssemblies();
                    }
                }
            }
        }
        #endregion

        #region Bather

        public Type BatcherType
        {
            set
            {
                if (typeof(IBatcher).IsAssignableFrom(value))
                    EditorPrefs.SetString(AutoLODConst.k_DefaultBatcher, value.AssemblyQualifiedName);
                else if (value == null)
                    EditorPrefs.DeleteKey(AutoLODConst.k_DefaultBatcher);

                OnSettingsUpdated?.Invoke();
            }
            get
            {
                var type = Type.GetType(EditorPrefs.GetString(AutoLODConst.k_DefaultBatcher, null));
                if (type == null && Batchers.Count > 0)
                    type = Type.GetType(Batchers[0].AssemblyQualifiedName);
                return type;
            }
        }

        public List<Type> Batchers
        {
            get
            {
                if (batchers == null || batchers.Count == 0)
                    batchers = ObjectUtils.GetImplementationsOfInterface(typeof(IBatcher)).ToList();

                return batchers;
            }
        }
        
        public string DefaultBatcher
        {
            get => lodImportSettings.batcher;
            set
            {
                if (typeof(IBatcher).IsAssignableFrom(Type.GetType(value)))
                    lodImportSettings.batcher = value;
                else if (value == null)
                    lodImportSettings.batcher = null;
                OnSettingsUpdated?.Invoke();
            }
        }
        
        #endregion
        
        public int MaxExecutionTime
        {
            get => maxExecutionTime;
            set
            {
                maxExecutionTime = value;
                OnSettingsUpdated?.Invoke();
            }
        }
        
        public bool GenerateOnImport
        {
            get => lodImportSettings.generateOnImport;
            set
            {
                lodImportSettings.generateOnImport = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public bool SaveAssets
        {
            get => saveAssets;
            set
            {
                saveAssets = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public int InitialLODMaxPolyCount
        {
            get => lodImportSettings.initialLODMaxPolyCount;
            set
            {
                lodImportSettings.initialLODMaxPolyCount = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public bool SceneLODEnabled
        {
            get => sceneLODEnabled;
            set
            {
                sceneLODEnabled = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public bool ShowVolumeBounds
        {
            get => showVolumeBounds;
            set
            {
                showVolumeBounds = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public LODHierarchyType HierarchyType
        {
            get => lodImportSettings.hierarchyType;
            set
            {
                lodImportSettings.hierarchyType = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public int MaxLOD
        {
            get => maxLOD;
            set
            {
                maxLOD = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public bool UseSameMaterialForLODs
        {
            get => useSameMaterialForLODs;
            set
            {
                useSameMaterialForLODs = value;
                OnSettingsUpdated?.Invoke();
            }
        }

        public IPreferences SimplifierPreferences
        {
            get => simplifierPreferences;
            set => simplifierPreferences = value;
        }

        private void OnValidate()
        {
            OnSettingsUpdated?.Invoke();
        }
    }
    
    enum PackageStatus
    {
        Unknown,
        Available,
        InProgress,
        Failure
    }
}