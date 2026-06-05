#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyTool
{
    public static class AssetApplyService
    {
        public static bool ApplyMeshPlan(MeshOptimizationPlan plan, bool dryRun, AssetUtilityOptimizationReport report = null)
        {
            if (plan == null) return false;

            if (!plan.IsValid(out string reason))
            {
                AddError(report, "Cannot apply mesh plan: " + reason);
                Debug.LogWarning("Cannot apply mesh plan: " + reason);
                return false;
            }

            string meshPath = plan.MeshPathToApply;
            Mesh newMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (newMesh == null)
            {
                AddError(report, "Mesh to apply not found: " + meshPath);
                Debug.LogWarning("Mesh to apply not found: " + meshPath);
                return false;
            }

            if (dryRun)
            {
                Debug.Log("Dry-run: would apply " + meshPath + " to " + plan.targetObjectPath);
                AddChange(report, plan, "DryRun", meshPath, "No scene reference changed.");
                return true;
            }

            GameObject target = GameObject.Find(plan.targetObjectPath);
            if (target == null)
            {
                AddError(report, "Target object not found: " + plan.targetObjectPath);
                Debug.LogWarning("Target object not found: " + plan.targetObjectPath);
                return false;
            }

            bool changed = false;

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null && plan.updateMeshFilter)
            {
                Undo.RecordObject(mf, "Apply optimized mesh");
                mf.sharedMesh = newMesh;
                EditorUtility.SetDirty(mf);
                AddChange(report, plan, "MeshFilter", meshPath, "Updated sharedMesh.");
                changed = true;
            }

            SkinnedMeshRenderer smr = target.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && plan.updateSkinnedMeshRenderer)
            {
                Undo.RecordObject(smr, "Apply optimized skinned mesh");
                smr.sharedMesh = newMesh;
                EditorUtility.SetDirty(smr);
                AddChange(report, plan, "SkinnedMeshRenderer", meshPath, "Updated sharedMesh. Visual validation required for skin/blendshape data.");
                changed = true;
            }

            MeshCollider mc = target.GetComponent<MeshCollider>();
            if (mc != null && plan.updateMeshCollider)
            {
                Undo.RecordObject(mc, "Apply optimized collider mesh");
                mc.sharedMesh = newMesh;
                EditorUtility.SetDirty(mc);
                AddChange(report, plan, "MeshCollider", meshPath, "Updated sharedMesh.");
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                if (target.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.scene);
                Debug.Log("Applied mesh plan to " + plan.targetObjectPath + " using " + meshPath);
            }

            return changed;
        }

        private static void AddChange(AssetUtilityOptimizationReport report, MeshOptimizationPlan plan, string componentType, string appliedMeshPath, string note)
        {
            if (report == null) return;
            report.changes.Add(new AssetUtilityChangeRecord
            {
                objectPath = plan.targetObjectPath,
                scenePath = plan.targetScenePath,
                componentType = componentType,
                sourceAssetPath = plan.sourceMeshPath,
                generatedAssetPath = appliedMeshPath,
                note = note
            });
        }

        private static void AddError(AssetUtilityOptimizationReport report, string error)
        {
            if (report != null) report.errors.Add(error);
        }
    }
}
#endif
