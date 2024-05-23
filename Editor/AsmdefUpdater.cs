using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public static class AsmdefUpdater
{
    private const string AutoLODEditorAsmdefPath = "Packages/com.yvesalbuquerque.autolod/Editor/Unity.AutoLOD.Editor.asmdef";
    private const string AutoLODAsmdefPath = "Packages/com.yvesalbuquerque.autolod/Runtime/Unity.AutoLOD.asmdef";
    private const string InstaLODEditorAsmdefPath = "Assets/InstaLOD/Editor/Unity.InstaLOD.Editor.asmdef";
    private const string InstaLODAsmdefPath = "Assets/InstaLOD/Scripts/Unity.InstaLOD.asmdef";

    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        if (File.Exists(InstaLODEditorAsmdefPath) && File.Exists(InstaLODAsmdefPath))
        {
            UpdateAsmdef(AutoLODEditorAsmdefPath, "Unity.InstaLOD.Editor");
            UpdateAsmdef(AutoLODAsmdefPath, "Unity.InstaLOD");
        }
    }

 
    private static void UpdateAsmdef(string asmdefPath, string reference)
    {
        if (!File.Exists(asmdefPath)) return;

        var asmdefText = File.ReadAllText(asmdefPath);
        var asmdef = JsonUtility.FromJson<Asmdef>(asmdefText);

        bool changed = false;

        // Ensure the name property is set
        if (string.IsNullOrEmpty(asmdef.name))
        {
            var fileName = Path.GetFileNameWithoutExtension(asmdefPath);
            asmdef.name = fileName;
            changed = true;
        }

        // Add reference if not already present
        if (asmdef.references == null)
        {
            asmdef.references = new string[] { reference };
            changed = true;
        }
        else if (!ArrayContains(asmdef.references, reference))
        {
            var updatedReferences = new string[asmdef.references.Length + 1];
            asmdef.references.CopyTo(updatedReferences, 0);
            updatedReferences[asmdef.references.Length] = reference;
            asmdef.references = updatedReferences;
            changed = true;
        }

        if (changed)
        {
            var updatedAsmdefText = JsonUtility.ToJson(asmdef, true);
            File.WriteAllText(asmdefPath, updatedAsmdefText);
        }
    }

    private static bool ArrayContains(string[] array, string item)
    {
        foreach (var element in array)
        {
            if (element == item)
            {
                return true;
            }
        }
        return false;
    }

    [System.Serializable]
    private class Asmdef
    {
        public string name;
        public string[] references;
        public string[] includePlatforms;
        public string[] excludePlatforms;
        public bool allowUnsafeCode;
        public bool overrideReferences;
        public string[] precompiledReferences;
        public bool autoReferenced;
        public string[] defineConstraints;
        public VersionDefine[] versionDefines;
        public bool noEngineReferences;
    }
    
    [System.Serializable]
    private class VersionDefine
    {
        public string name;
        public string expression;
        public string define;
    }
}