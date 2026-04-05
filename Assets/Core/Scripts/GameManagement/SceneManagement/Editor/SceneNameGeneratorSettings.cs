using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "SceneNameGeneratorSettings", menuName = "Scriptable Objects/SceneNameGeneratorSettings")]
public sealed class SceneNameGeneratorSettings : ScriptableObject
{
    [Title("Output")]
    [FolderPath(AbsolutePath = false, RequireExistingPath = true)]
    [SerializeField] private string generatedFolder;

    [SerializeField] private string enumFileName = "SceneType.cs";
    [SerializeField] private string mapFileName = "SceneTypeMap.cs";

    [Title("Code")]
    [SerializeField] private bool useNamespace;

    [ShowIf(nameof(useNamespace))]
    [SerializeField] private string namespaceName;

    [Title("Options")]
    [SerializeField] private bool generateMap = true;

    public string GeneratedFolder => generatedFolder;
    public string EnumFileName => enumFileName;
    public string MapFileName => mapFileName;
    public bool UseNamespace => useNamespace;
    public string NamespaceName => namespaceName;
    public bool GenerateMap => generateMap;
}