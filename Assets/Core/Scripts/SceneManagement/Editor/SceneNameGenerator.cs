using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class SceneNameGenerator
{
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);
    private static bool isGenerateQueued;

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
        "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
        "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long",
        "namespace","new","null","object","operator","out","override","params","private","protected","public","readonly",
        "ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw",
        "true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
    };

    [InitializeOnLoadMethod]
    private static void OnLoad()
    {
        EditorBuildSettings.sceneListChanged -= Generate;
        EditorBuildSettings.sceneListChanged += Generate;

        EditorApplication.delayCall -= GenerateInternal;
        EditorApplication.delayCall += GenerateInternal;
    }

    [MenuItem("Tools/Scene Management/Open Generator Settings")]
    public static void OpenSettings()
    {
        if (!TryGetSingleSettings(out SceneNameGeneratorSettings settings, out string error))
        {
            Debug.LogError(error);
            return;
        }

        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    [MenuItem("Tools/Scene Management/Regenerate SceneType")]
    public static void Generate()
    {
        if (isGenerateQueued)
            return;

        isGenerateQueued = true;
        EditorApplication.delayCall -= GenerateInternal;
        EditorApplication.delayCall += GenerateInternal;
    }

    private static void GenerateInternal()
    {
        isGenerateQueued = false;

        if (!TryGetSingleSettings(out SceneNameGeneratorSettings settings, out string settingsError))
        {
            Debug.LogError(settingsError);
            return;
        }

        List<string> errors = new();
        ValidateSettings(settings, errors);

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        List<SceneEntry> entries = new(scenes.Length);

        ValidateAndCollectScenes(scenes, entries, errors);

        if (errors.Count > 0)
        {
            Debug.LogError(BuildErrorReport(errors));
            return;
        }

        string folder = NormalizeSlashes(settings.GeneratedFolder);
        string enumPath = CombineAssetPath(folder, settings.EnumFileName);
        string enumCode = BuildEnumCode(settings, entries);

        bool enumChanged = WriteIfChanged(enumPath, enumCode);
        bool mapChanged = false;

        if (settings.GenerateMap)
        {
            string mapPath = CombineAssetPath(folder, settings.MapFileName);
            string mapCode = BuildMapCode(settings, entries);
            mapChanged = WriteIfChanged(mapPath, mapCode);
        }

        if (enumChanged || mapChanged)
            AssetDatabase.Refresh();
    }

    private static bool TryGetSingleSettings(out SceneNameGeneratorSettings settings, out string error)
    {
        settings = null;
        error = "";

        string[] guids = AssetDatabase.FindAssets("t:SceneNameGeneratorSettings");

        if (guids.Length == 0)
        {
            error = "SceneNameGeneratorSettings 에셋이 없습니다.";
            return false;
        }

        if (guids.Length > 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SceneNameGeneratorSettings 에셋이 2개 이상 존재합니다.");
            for (int i = 0; i < guids.Length; ++i)
                sb.AppendLine($"- {AssetDatabase.GUIDToAssetPath(guids[i])}");

            error = sb.ToString();
            return false;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        settings = AssetDatabase.LoadAssetAtPath<SceneNameGeneratorSettings>(path);

        if (settings == null)
        {
            error = $"SceneNameGeneratorSettings 로드에 실패했습니다. 경로: {path}";
            return false;
        }

        return true;
    }

    private static void ValidateSettings(SceneNameGeneratorSettings settings, List<string> errors)
    {
        string folder = NormalizeSlashes(settings.GeneratedFolder);
        if (string.IsNullOrWhiteSpace(folder))
            errors.Add("Settings.GeneratedFolder 가 비어 있습니다.");

        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (!IsAssetsPath(folder))
                errors.Add($"Settings.GeneratedFolder는 Assets 아래 경로여야 합니다. 현재 경로: {folder}");

            if (IsAssetsPath(folder) && !AssetDatabase.IsValidFolder(folder))
                errors.Add($"Settings.GeneratedFolder 폴더가 존재하지 않습니다. 현재 경로: {folder}");
        }

        ValidateFileName(settings.EnumFileName, "Settings.EnumFileName", errors);

        if (settings.GenerateMap)
            ValidateFileName(settings.MapFileName, "Settings.MapFileName", errors);

        if (settings.GenerateMap)
        {
            if (!string.IsNullOrWhiteSpace(settings.EnumFileName) && !string.IsNullOrWhiteSpace(settings.MapFileName))
            {
                if (string.Equals(settings.EnumFileName, settings.MapFileName, StringComparison.Ordinal))
                    errors.Add("Settings.EnumFileName과 Settings.MapFileName이 같습니다.");
            }
        }

        if (settings.UseNamespace)
        {
            if (string.IsNullOrWhiteSpace(settings.NamespaceName))
                errors.Add("Settings.NamespaceName이 비어 있습니다.");

            if (!string.IsNullOrWhiteSpace(settings.NamespaceName) && !IsValidNamespace(settings.NamespaceName))
                errors.Add($"Settings.NamespaceName이 C# namespace 형식이 아닙니다. 현재: {settings.NamespaceName}");
        }
    }

    private static void ValidateFileName(string fileName, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add($"{fieldName}이 비어 있습니다.");
            return;
        }

        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            errors.Add($"{fieldName}은 .cs 로 끝나야 합니다. 현재 이름: {fileName}");

        if (fileName.Contains("/") || fileName.Contains("\\"))
            errors.Add($"{fieldName}에 경로 구분자(/ 또는 \\)가 포함되어 있습니다. 현재 이름: {fileName}");

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            errors.Add($"{fieldName} 에 파일명으로 사용할 수 없는 문자가 포함되어 있습니다. 현재 이름: {fileName}");
    }

    private static void ValidateAndCollectScenes(EditorBuildSettingsScene[] scenes, List<SceneEntry> entries, List<string> errors)
    {
        var usedNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (int i = 0; i < scenes.Length; ++i)
        {
            string scenePath = NormalizeSlashes(scenes[i].path);
            if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Build Settings 씬 경로가 .unity가 아닙니다. index={i}, path={scenePath}");

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);

            if (string.Equals(sceneName, "None", StringComparison.Ordinal))
                errors.Add($"씬 이름이 SceneType의 예약 항목이므로 다른 이름으로 바꾸세요. name={sceneName}, path={scenePath}");

            if (!IdentifierRegex.IsMatch(sceneName))
                errors.Add($"씬 이름이 C# 식별자로 유효하지 않습니다. 씬 파일명을 바꾸세요. name={sceneName}, path={scenePath}");

            if (CSharpKeywords.Contains(sceneName))
                errors.Add($"씬 이름이 C# 예약어입니다. 씬 파일명을 바꾸세요. name={sceneName}, path={scenePath}");

            if (!usedNames.TryGetValue(sceneName, out List<string> list))
            {
                list = new List<string>();
                usedNames.Add(sceneName, list);
            }

            list.Add(scenePath);

            entries.Add(new SceneEntry(sceneName, scenePath, scenes[i].enabled));
        }

        foreach (var kv in usedNames)
        {
            if (kv.Value.Count <= 1)
                continue;

            var sb = new StringBuilder();
            sb.AppendLine($"씬 이름 충돌: '{kv.Key}'");
            for (int i = 0; i < kv.Value.Count; ++i)
                sb.AppendLine($"- {kv.Value[i]}");

            errors.Add(sb.ToString().TrimEnd());
        }
    }

    private static string BuildEnumCode(SceneNameGeneratorSettings settings, List<SceneEntry> entries)
    {
        var sb = new StringBuilder(2048);

        string indent = "";
        if (settings.UseNamespace && !string.IsNullOrWhiteSpace(settings.NamespaceName))
        {
            sb.AppendLine($"namespace {settings.NamespaceName}");
            sb.AppendLine("{");
            indent = "    ";
        }

        sb.AppendLine($"{indent}public enum SceneType");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    None = 0,");

        for (int i = 0; i < entries.Count; ++i)
            sb.AppendLine($"{indent}    {entries[i].Name} = {i + 1},");

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrEmpty(indent))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildMapCode(SceneNameGeneratorSettings settings, List<SceneEntry> entries)
    {
        var sb = new StringBuilder(6144);

        string indent = "";
        if (settings.UseNamespace && !string.IsNullOrWhiteSpace(settings.NamespaceName))
        {
            sb.AppendLine($"namespace {settings.NamespaceName}");
            sb.AppendLine("{");
            indent = "    ";
        }

        sb.AppendLine($"{indent}using System;");
        sb.AppendLine($"{indent}using System.Collections.Generic;");
        sb.AppendLine();

        sb.AppendLine($"{indent}public static class SceneTypeMap");
        sb.AppendLine($"{indent}{{");

        sb.AppendLine($"{indent}    private static readonly string[] SceneNames =");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        \"\",");
        for (int i = 0; i < entries.Count; ++i)
            sb.AppendLine($"{indent}        \"{EscapeString(entries[i].Name)}\",");
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();

        sb.AppendLine($"{indent}    private static readonly string[] ScenePaths =");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        \"\",");
        for (int i = 0; i < entries.Count; ++i)
            sb.AppendLine($"{indent}        \"{EscapeString(entries[i].Path)}\",");
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();

        sb.AppendLine($"{indent}    private static readonly bool[] EnabledInBuildSettings =");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        false,");
        for (int i = 0; i < entries.Count; ++i)
            sb.AppendLine(entries[i].Enabled ? $"{indent}        true," : $"{indent}        false,");
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();

        sb.AppendLine($"{indent}    private static readonly Dictionary<string, SceneType> NameToType = new(StringComparer.Ordinal)");
        sb.AppendLine($"{indent}    {{");
        for (int i = 0; i < entries.Count; ++i)
            sb.AppendLine($"{indent}        {{ \"{EscapeString(entries[i].Name)}\", SceneType.{entries[i].Name} }},");
        sb.AppendLine($"{indent}    }};");
        sb.AppendLine();

        sb.AppendLine($"{indent}    public static int TotalCount => SceneNames.Length;");
        sb.AppendLine($"{indent}    public static int BuildSceneCount => SceneNames.Length - 1;");
        sb.AppendLine($"{indent}    public static string GetName(SceneType sceneType) => SceneNames[(int)sceneType];");
        sb.AppendLine($"{indent}    public static string GetPath(SceneType sceneType) => ScenePaths[(int)sceneType];");
        sb.AppendLine($"{indent}    public static bool IsEnabledInBuildSettings(SceneType sceneType) => EnabledInBuildSettings[(int)sceneType];");
        sb.AppendLine($"{indent}    public static bool TryGetTypeByName(string sceneName, out SceneType sceneType) => NameToType.TryGetValue(sceneName, out sceneType);");

        sb.AppendLine($"{indent}}}");

        if (!string.IsNullOrEmpty(indent))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static bool WriteIfChanged(string assetPath, string contents)
    {
        if (File.Exists(assetPath))
        {
            string existing = File.ReadAllText(assetPath, Encoding.UTF8);
            if (existing == contents)
                return false;
        }

        File.WriteAllText(assetPath, contents, new UTF8Encoding(false));
        return true;
    }

    private static bool IsAssetsPath(string path)
    {
        if (string.Equals(path, "Assets", StringComparison.Ordinal))
            return true;

        return path.StartsWith("Assets/", StringComparison.Ordinal);
    }

    private static bool IsValidNamespace(string ns)
    {
        string[] parts = ns.Split('.');
        for (int i = 0; i < parts.Length; ++i)
        {
            string p = parts[i];
            if (!IdentifierRegex.IsMatch(p))
                return false;

            if (CSharpKeywords.Contains(p))
                return false;
        }

        return true;
    }

    private static string BuildErrorReport(List<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SceneNameGenerator: 생성/업데이트 실패. 아래 문제를 수정한 뒤 다시 시도하세요.");
        for (int i = 0; i < errors.Count; ++i)
        {
            sb.Append("- ");
            sb.AppendLine(errors[i].Replace("\n", "\n  "));
        }

        return sb.ToString();
    }

    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string NormalizeSlashes(string path) => path.Replace("\\", "/");
    private static string CombineAssetPath(string folder, string fileName) => NormalizeSlashes(Path.Combine(folder, fileName));
}

internal sealed class SceneAssetPostProcessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (HasSceneAsset(importedAssets))
            SceneNameGenerator.Generate();

        if (HasSceneAsset(deletedAssets))
            SceneNameGenerator.Generate();

        if (HasSceneAsset(movedAssets) || HasSceneAsset(movedFromAssetPaths))
            SceneNameGenerator.Generate();
    }

    private static bool HasSceneAsset(string[] paths)
    {
        for (int i = 0; i < paths.Length; ++i)
        {
            if (IsUnityScenePath(paths[i]))
                return true;
        }

        return false;
    }

    private static bool IsUnityScenePath(string path) => path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
}

internal readonly struct SceneEntry
{
    public readonly string Name;
    public readonly string Path;
    public readonly bool Enabled;

    public SceneEntry(string name, string path, bool enabled)
    {
        Name = name;
        Path = path;
        Enabled = enabled;
    }
}