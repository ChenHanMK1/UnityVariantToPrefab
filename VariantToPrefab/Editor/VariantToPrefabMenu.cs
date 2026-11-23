using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VariantToPrefab.EditorTools
{
    /// <summary>
    /// 把 Prefab Variant 就地转换成普通 Prefab（本体），保留原路径与 GUID。
    ///
    /// Convert Prefab Variants into regular Prefabs in place. The asset path and the
    /// .meta file are untouched, so the GUID never changes and every reference
    /// (scenes, prefabs, code, configs) keeps working.
    ///
    /// 原理 / How it works:
    ///   1. 在预览场景里实例化变体 / instantiate the variant in a preview scene;
    ///   2. 反复解包【最外层】，直到根节点不再是预制体实例
    ///      / peel the root only, until the root is a plain GameObject;
    ///   3. 覆盖保存回原路径 / save back over the original path.
    ///
    /// 为什么不用 Completely / Why not PrefabUnpackMode.Completely:
    ///   Completely 会把子物体里的嵌套预制体也一起拍平；
    ///   这里只在根节点上逐层解包，子层级里的嵌套预制体原样保留。
    ///   Completely would also flatten nested prefabs inside the hierarchy.
    ///
    /// 为什么不能只解一层 / Why one level is not enough:
    ///   变体本身就是一个"指向基类的实例"，解一层后根仍然是基类的实例，
    ///   保存后 Unity 会继续把它判定为 Variant。必须解到根是普通物体为止。
    ///   A variant *is* an instance of its base, so one level leaves a root-level
    ///   PrefabInstance and Unity keeps treating the asset as a variant.
    /// </summary>
    public static class VariantToPrefabMenu
    {
        private const string MenuRoot = "Assets/变体转换本体（Variant to Prefab）";
        private const string MenuConvert = MenuRoot + "/转换选中变体（Convert Selected）";
        private const string MenuScan = MenuRoot + "/扫描变体（Scan Variants）";
        // 25：紧跟在内置的 Copy Path (24) 之后；想让菜单项更靠下，把这个数字改大即可。
        private const int MenuPriority = 30;
        private const string LogTag = "[Variant→Prefab] ";
        private const int MaxInheritanceDepth = 16;

        // ------------------------------------------------------------------ 菜单 / Menu items

        [MenuItem(MenuConvert, false, MenuPriority)]
        public static void ConvertSelected()
        {
            List<string> variants = CollectSelectedVariants(out int totalPrefabs);

            if (variants.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "变体转换本体（Variant to Prefab）",
                    "选中项里没有可转换的变体预制体。\n\nNo Prefab Variant found in the current selection.",
                    "确定 / OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "变体转换本体（Variant to Prefab）",
                    "共扫描 " + totalPrefabs + " 个预制体，其中 " + variants.Count + " 个是变体。\n" +
                    "Scanned " + totalPrefabs + " prefab(s), " + variants.Count + " of them are variants.\n\n" +
                    "· 原地覆盖保存，GUID 保持不变 / Converted in place, GUID preserved\n" +
                    "· 子层级里的嵌套预制体保持不动 / Nested prefabs inside are left intact\n" +
                    "· 建议先提交或备份工程 / Commit or back up your project first\n\n" +
                    "继续吗？/ Continue?",
                    "开始转换 / Convert", "取消 / Cancel"))
                return;

            Run(variants);
        }

        [MenuItem(MenuConvert, true)]
        private static bool ConvertSelectedValidate()
        {
            return HasPrefabOrFolderSelection();
        }

        [MenuItem(MenuScan, false, MenuPriority + 1)]
        public static void ScanSelected()
        {
            List<string> folders = CollectSelectedFolders();
            if (folders.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "扫描变体（Scan Variants）",
                    "请先在 Project 窗口选中预制体或文件夹。\n\nSelect a prefab or a folder in the Project window first.",
                    "确定 / OK");
                return;
            }

            var variants = new List<string>();
            int totalPrefabs = 0;

            foreach (string folder in folders)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    totalPrefabs++;
                    if (IsVariantPrefab(path))
                        variants.Add(path);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(LogTag + "扫描文件夹 / scanned folders:");
            foreach (string folder in folders)
                sb.AppendLine("  " + folder);
            sb.AppendLine("预制体总数 / total prefabs: " + totalPrefabs);
            sb.AppendLine("变体数量 / variants: " + variants.Count);
            foreach (string variant in variants)
                sb.AppendLine("  - " + variant);
            Debug.Log(sb.ToString());

            EditorUtility.DisplayDialog(
                "扫描变体（Scan Variants）",
                "文件夹 / folders:\n" + string.Join("\n", folders.ToArray()) + "\n\n" +
                "预制体总数 / total prefabs: " + totalPrefabs + "\n" +
                "变体数量 / variants: " + variants.Count + "\n\n" +
                "完整列表见 Console。/ See the Console for the full list.",
                "确定 / OK");
        }

        [MenuItem(MenuScan, true)]
        private static bool ScanSelectedValidate()
        {
            return HasPrefabOrFolderSelection();
        }

        // ------------------------------------------------------------------ 转换流程 / Conversion

        private static void Run(List<string> paths)
        {
            var converted = new List<string>();
            var failed = new List<string>();

            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "变体转换本体（Variant to Prefab）", paths[i], (float)i / paths.Count);
                    if (cancelled)
                    {
                        Debug.LogWarning(LogTag + "用户取消，已处理 " + i + "/" + paths.Count + " / cancelled");
                        break;
                    }

                    try
                    {
                        ConvertOne(paths[i]);
                        converted.Add(paths[i]);
                    }
                    catch (Exception e)
                    {
                        failed.Add(paths[i] + "  (" + e.Message + ")");
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            var sb = new StringBuilder();
            sb.AppendLine(LogTag + "完成 / done. 成功 " + converted.Count + "，失败 " + failed.Count);
            foreach (string s in converted)
                sb.AppendLine("  OK  " + s);
            foreach (string s in failed)
                sb.AppendLine("  FAIL " + s);
            Debug.Log(sb.ToString());

            EditorUtility.DisplayDialog(
                "变体转换本体（Variant to Prefab）",
                "成功 / converted: " + converted.Count + "\n" +
                "失败 / failed: " + failed.Count + "\n\n" +
                "详细结果见 Console。/ See the Console for details.\n\n" +
                "建议保存当前场景，并重开一次引用了这些预制体的场景确认。\n" +
                "Save your open scene, then reopen the scenes that use these prefabs.",
                "确定 / OK");
        }

        private static void ConvertOne(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
                throw new InvalidOperationException("无法加载 / cannot load asset");

            // 如果这个预制体正开在 Prefab Mode 里，先回主场景，避免保存冲突
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, path, StringComparison.OrdinalIgnoreCase))
                StageUtility.GoToMainStage();

            string guidBefore = AssetDatabase.AssetPathToGUID(path);
            Scene previewScene = EditorSceneManager.NewPreviewScene();

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, previewScene);
                if (instance == null)
                    throw new InvalidOperationException("实例化失败 / instantiate failed");

                if (PrefabUtility.GetPrefabInstanceStatus(instance) != PrefabInstanceStatus.Connected)
                    throw new InvalidOperationException("不是有效的预制体实例 / not a connected prefab instance");

                // 只剥掉根节点上的预制体身份，子层级里的嵌套预制体保持不动
                int depth = 0;
                while (PrefabUtility.GetPrefabInstanceStatus(instance) == PrefabInstanceStatus.Connected)
                {
                    if (++depth > MaxInheritanceDepth)
                        throw new InvalidOperationException(
                            "继承层数超过 " + MaxInheritanceDepth + " 层 / inheritance deeper than " + MaxInheritanceDepth);

                    PrefabUtility.UnpackPrefabInstance(
                        instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                }

                PrefabUtility.SaveAsPrefabAsset(instance, path, out bool success);
                if (!success)
                    throw new InvalidOperationException("保存失败 / SaveAsPrefabAsset failed");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            // 刷新编辑器里缓存的旧资产对象
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // 以磁盘内容为准校验（LoadAssetAtPath 此时可能还是旧的缓存对象）
            if (IsVariantFile(path))
                throw new InvalidOperationException("转换后仍残留根级预制体实例 / root prefab instance still present");

            if (!string.Equals(guidBefore, AssetDatabase.AssetPathToGUID(path), StringComparison.Ordinal))
            {
                Debug.LogWarning(LogTag + "GUID 发生变化 / GUID changed: " + path +
                                 "\n  " + guidBefore + " -> " + AssetDatabase.AssetPathToGUID(path) +
                                 "\n  请检查引用是否断开 / check for broken references");
            }

            int kept = CountNestedInstances(path);
            if (kept > 0)
                Debug.Log(LogTag + "保留 " + kept + " 个嵌套预制体 / kept " + kept + " nested prefab instance(s): " + path);
        }

        // ------------------------------------------------------------------ 选择项 / Selection helpers

        private static bool HasPrefabOrFolderSelection()
        {
            foreach (UnityEngine.Object obj in Selection.objects)
            {
                if (obj == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static List<string> CollectSelectedVariants(out int totalPrefabs)
        {
            List<string> paths = CollectPrefabPaths(Selection.objects);
            totalPrefabs = paths.Count;

            var variants = new List<string>();
            foreach (string path in paths)
            {
                if (IsVariantPrefab(path))
                    variants.Add(path);
            }

            return variants;
        }

        private static List<string> CollectSelectedFolders()
        {
            var folders = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityEngine.Object obj in Selection.objects)
            {
                if (obj == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                string folder = AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder))
                    continue;

                folder = folder.Replace('\\', '/');
                if (seen.Add(folder))
                    folders.Add(folder);
            }

            return folders;
        }

        private static List<string> CollectPrefabPaths(UnityEngine.Object[] selection)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityEngine.Object obj in selection)
            {
                if (obj == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
                    {
                        string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(prefabPath) && seen.Add(prefabPath))
                            result.Add(prefabPath);
                    }
                }
                else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    if (seen.Add(path))
                        result.Add(path);
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // ------------------------------------------------------------------ 变体判定 / Variant detection

        private static bool IsVariantPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant;
        }

        /// <summary>
        /// 判断 .prefab 文件会不会被 Unity 导入成变体。满足任一条件即是：
        ///   1. 第 4 行是 "PrefabInstance:"（整个文件只有一个根实例）；
        ///   2. 存在 m_TransformParent 为 {fileID: 0} 的 PrefabInstance 块
        ///      （根级预制体实例，Unity 会把它当作资产根）。
        ///
        /// Note: this reads the file itself on purpose. Right after
        /// SaveAsPrefabAsset, AssetDatabase may still hand back the cached,
        /// pre-conversion asset object.
        /// </summary>
        private static bool IsVariantFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                string[] lines = File.ReadAllLines(path);

                if (lines.Length >= 4 && lines[3].Trim() == "PrefabInstance:")
                    return true;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() != "PrefabInstance:")
                        continue;

                    for (int j = i + 1; j < Math.Min(i + 64, lines.Length); j++)
                    {
                        string trimmed = lines[j].Trim();

                        if (trimmed.StartsWith("m_TransformParent:"))
                        {
                            if (trimmed.Contains("{fileID: 0}"))
                                return true;
                            break;
                        }

                        if (trimmed == "PrefabInstance:")
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogTag + "读取文件失败，跳过校验 / cannot read file: " + path + " (" + e.Message + ")");
            }

            return false;
        }

        private static int CountNestedInstances(string path)
        {
            try
            {
                int count = 0;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.Trim() == "PrefabInstance:")
                        count++;
                }
                return count;
            }
            catch
            {
                return -1;
            }
        }
    }
}
