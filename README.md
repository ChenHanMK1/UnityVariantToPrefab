# Variant to Prefab / 变体转换本体

把 Prefab Variant（预制体变体）**就地**转换成普通 Prefab（本体），**保留原路径与 GUID**，场景、预制体、代码、配置表里已有的引用全部继续有效。

Convert Prefab Variants into regular Prefabs **in place**, keeping the asset path and GUID, so every existing reference keeps working.

---

## 安装 / Installation

### 方式一：Git URL（推荐 / recommended）

Unity → `Window` → `Package Manager` → 左上角 `+` → `Add package from git URL...`

```
https://github.com/ChenHanMK1/UnityVariantToPrefab.git
```

### 方式二：写进 manifest.json

编辑 `Packages/manifest.json`，在 `dependencies` 里加一行：

```json
{
  "dependencies": {
    "com.variant-to-prefab": "https://github.com/ChenHanMK1/UnityVariantToPrefab.git"
  }
}
```

### 方式三：本地路径

把仓库放到工程外，然后 `Add package from disk...` 选择 `package.json`，
或在 `manifest.json` 里写 `"com.variant-to-prefab": "file:E:/GitHubProjects/VariantToPrefab"`。

---

## 使用 / Usage

在 **Project 窗口**里选中预制体或文件夹 → 右键 →

```
变体转换本体（Variant to Prefab）
├─ 转换选中变体（Convert Selected）   ← 直接转换，已是本体的自动跳过
└─ 扫描变体（Scan Variants）          ← 只统计并列出变体，不改任何文件
```

- **转换选中变体**：对选中的变体执行转换；选中的是文件夹则递归处理整个文件夹，非变体自动跳过。转换前有确认框，过程中有进度条（可取消），结束后列出成功/失败清单。
- **扫描变体**：扫描选中项所在的文件夹（选中预制体时取其所在目录），在 Console 输出全部变体路径，弹窗显示总数。**不修改任何文件**，适合先摸清家底。

转换完成后建议：保存当前场景，并重开一次引用了这些预制体的场景，确认表现正常。

---

## 原理 / How it works

1. 在预览场景里实例化这个变体，得到“变体覆盖 + 基类”叠加后的最终形态。
2. **只剥掉根节点上的预制体身份**：反复以 `PrefabUnpackMode.OutermostRoot` 解包，直到根节点变成普通 `GameObject`。
3. 用 `PrefabUtility.SaveAsPrefabAsset` 覆盖保存回**原路径**，`.meta` 不动 → GUID 不变。

### 为什么不用 `Unpack Prefab Completely`

`Completely` 会把子物体里的嵌套预制体（树叶、灯、水草……）一并拍平，它们就失去了与源预制体的联动。
本工具只在**根节点**上逐层解包，子层级里的嵌套预制体原样保留，转换后仍然跟随各自的源预制体。

### 为什么不能只解一层

Unity 判断“是不是变体”的依据是**资产的根节点是不是一个预制体实例**。
变体本身就是“指向基类的一个实例”，只解一层的话根仍然是基类的实例，保存后 Unity 重新导入时照样判定为 Variant（Inspector 顶部仍显示 `Variant Parent xxx`）。所以必须解到根是普通物体为止。

---

## 注意事项 / Notes

- **会覆盖原文件**：转换是破坏性操作，请先提交或备份工程。
- **GUID 不变**：脚本会在转换后比对前后的 GUID，若发生变化会打黄色警告。
- **基类不再联动**：转换后这个预制体不再继承原基类，基类的后续修改不会传导过来（这正是“转本体”的目的）。
- **子预制体保留**：内部嵌套的预制体会保留为 Prefab 实例，不会被展开。
- **编辑器缓存**：保存后脚本会 `ImportAsset(..., ForceUpdate)` 刷新缓存；如果你的 Unity 版本仍显示旧状态，右键预制体 → `Reimport` 一次即可。
- 只在 **Unity 2020.3+ 的编辑器**下运行，不参与打包。

---

## 目录结构 / Layout

```
VariantToPrefab/
├─ package.json
├─ README.md
├─ CHANGELOG.md
├─ LICENSE
└─ Editor/
   ├─ VariantToPrefab.Editor.asmdef
   └─ VariantToPrefabMenu.cs
```

---

## License

MIT
