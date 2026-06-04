#if UNITY_EDITOR
// ============================================================================
// AssetUtilityData.cs
// ScriptableObjectはUnity側でScript Assetと紐づく必要があるため、
// AssetUtilityDataクラスだけを同名ファイルへ分離しています。
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
                // 以前の版でMissing Script状態のassetが残っている場合、同じパスにCreateAssetできないため削除します。
                UnityEngine.Object broken = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (broken != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }

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
            // SaveAndReimport や Scene open/restore の直後に ScriptableObject の参照が
            // Unity側で破棄済みになることがあるため、SetDirty前に必ず確認します。
            if (object.ReferenceEquals(this, null) || this == null)
            {
                Debug.Log("[AssetUtilityData] 保存対象が既に破棄されていたため、保存をスキップしました");
                return;
            }

            try
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                Debug.Log("📦 AssetUtilityData を保存しました");
            }
            catch (MissingReferenceException)
            {
                Debug.Log("[AssetUtilityData] 参照が破棄済みだったため、保存をスキップしました");
            }
        }
    }
}
#endif
