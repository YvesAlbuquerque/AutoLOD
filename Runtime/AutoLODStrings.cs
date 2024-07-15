using UnityEngine;

namespace Unity.AutoLOD
{
    public static class AutoLODConst
    {
        public const HideFlags k_DefaultHideFlags = HideFlags.None;
        public const string k_MaxExecutionTime = "AutoLOD.MaxExecutionTime";
        public const int k_DefaultMaxExecutionTime = 8;
        public const string k_DefaultMeshSimplifier = "AutoLOD.DefaultMeshSimplifier";
        public const string k_DefaultMeshSimplifierDefault = "QuadricMeshSimplifier";
        public const string k_DefaultMeshSimplifierDefine = "ENABLE_UNITYMESHSIMPLIFIER";
        public const string k_DefaultBatcher = "AutoLOD.DefaultBatcher";
        public const string k_MaxLOD = "AutoLOD.MaxLOD";
        public const int k_DefaultMaxLOD = 2;
        public const string k_GenerateOnImport = "AutoLOD.GenerateOnImport";
        public const string k_SaveAssets = "AutoLOD.SaveAssets";
        public const string k_InitialLODMaxPolyCount = "AutoLOD.InitialLODMaxPolyCount";
        public const int k_DefaultInitialLODMaxPolyCount = 500000;
        public const string k_SceneLODEnabled = "AutoLOD.SceneLODEnabled";
        public const string k_ShowVolumeBounds = "AutoLOD.ShowVolumeBounds";
        public const string k_useSameMaterialForLODs = "AutoLOD.UseSameMaterialForLODs";
        public const string k_hierarchyType = "AutoLOD.HierarchyType";
    }
}