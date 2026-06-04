#if UNITY_EDITOR
// ============================================================================
// AssetUtilityData.cs
// ScriptableObjectをUnityが正しく認識できるよう、クラス名と同じファイルに分離。
// ============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyTool
{
public class AssetUtilityData : ScriptableObject
{
    public List<MaterialEditData> materialEdits = new List<MaterialEditData>();
    public List<TextureEditData> textureEditDataList = new List<TextureEditData>();
    public List<MeshEditData> meshEditDataList = new List<MeshEditData>();

    public void ClearTemporaryData()
    {
        textureEditDataList.Clear();
        meshEditDataList.Clear();
        Debug.Log("[AssetUtilityData] 一時編集データをクリアしました");
    }

    public void RestoreTemporaryData()
    {
        Debug.Log("[AssetUtilityData] 一時編集データを復元しました（現状では何もしません）");
    }

    public static AssetUtilityData LoadOrCreateData()
    {
        string path = "Assets/Editor/Resources/AssetUtilityData.asset";
        var data = AssetDatabase.LoadAssetAtPath<AssetUtilityData>(path);
        if (data == null)
        {
            data = CreateInstance<AssetUtilityData>();
            if (!Directory.Exists("Assets/Editor/Resources"))
                Directory.CreateDirectory("Assets/Editor/Resources");
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
        }
        return data;
    }

    public void SaveChangesToDisk()
    {
        if (this == null)
        {
            Debug.LogWarning("[AssetUtilityData] 保存対象が破棄済みのため保存をスキップしました");
            return;
        }

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        Debug.Log("📦 AssetUtilityData を保存しました");
    }
}
}
#endif
