using UnityEngine;

public static  class Extensions
{
    public static bool HasLODChain(this LODGroup lodGroup)
    {
        if (lodGroup != null)
        {
            var lods = lodGroup.GetLODs();
            if (lods.Length > 0)
            {
                for (var l = 1; l < lods.Length; l++)
                {
                    var lod = lods[l];
                    if (lod.renderers.Length > 0)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
