using System;
using UnityEngine.Serialization;

namespace Unity.AutoLOD
{
    [Serializable]
    public class LODImportSettings
    {
        public bool generateOnImport = true;
        public string meshSimplifier = AutoLODConst.k_DefaultMeshSimplifierDefault;
        public string batcher = "UnityDefaultBatcher";
        public int maxLODGenerated = 3;
        public int initialLODMaxPolyCount = Int32.MaxValue;
        public LODHierarchyType hierarchyType = LODHierarchyType.ChildOfSource;
        public string parentName = String.Empty;
    }
}
