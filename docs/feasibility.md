# 可行性评估

依据：对本机游戏安装中 `Assembly-CSharp.dll` 的只读反汇编（2026-11-08 版本）。

## 官方 Mod 系统

- `ModLoader` 从游戏目录 `Mods/` 加载 C# DLL，入口为 `IMod`（`Init` / `OnEnable` / `OnDisable`）。清单 `ModManifest` 含 `ID`、`Name`、`Author`、`ModVersion`、`GameVersions`、`Prerequisites`、`Assemblies`、`Capabilities`。
- 加载 DLL 时游戏自动创建 Harmony 实例并 `PatchAll`（游戏自带 `0Harmony.dll`），可补丁私有方法。
- 权限声明：`files`、`network`、`process`、`dynamic-code`、`native`、`reflection-external`、`steam`，由 `ModScanner` 扫描并向玩家展示。
- `ModWorkshop`：支持 Steam 创意工坊。
- `ModHelper`：`SpawnItem`、`MoveItemTo*`、`SetSource`（给物品打 Mod 来源标签）、`AddLootEntry` / `RegisterLootTable`（数据型掉落表）、`GetConfig` / 游戏选项、`ReadModFileBytes`（Mod 目录内文件）、`Read/WriteModDataFile*`（`persistentDataPath/ModData/<id>/`）、本地化等。
- `ModHook`：`OnModItemDirectoryInit`（注册新物品）、`OnGenerateCustomer{VeryEarly…VeryLate}`（插入顾客）、`OnGameLoaded*`、开关店、睡觉、离店/回店、夜间服务等事件。

## 方案各部分

| 需求 | 游戏中的对应实现 | 可行性 |
|---|---|---|
| 新物品 | `OnModItemDirectoryInit` + `GameItem.SetSpriteAndShape` / `SetGameItemType` / `EnableTag` / `SetValue`（均为 public） | 可行 |
| 类型：工具 + 高级违禁品 | 类型 `TOOL`；违禁品标签 `CONTRABAND_ITEM_TAG` + `CONTRABAND_LEVEL_HIGH` | 可行（持有会在治安检查时被查处） |
| 价值 500 | `SetValue(500)`（基因扫描仪同为 500） | 可行 |
| 双击 / 右键激活 | `onActivateSlotItemFunc` 等三件套；双击 `ItemMouseDoubleClickHandler`、右键菜单 `ItemContextHandler`（按钮文字「激活」）、默认激活键都调用 `ActivateSlotItem` | 可行 |
| 贴图：基因扫描仪改为超科技配色 + 屏幕画双格模组 | 运行时读取已加载的 Sprite，用 RenderTexture 复制像素后改色并合成，生成新 Sprite | 可行；不得把改色后的图片打包进 Mod |
| 杰克逊博士出售 | 来店和他的店铺都调用 `PlaceInventorInventory`，其中触发官方钩子 `ModHook.OnPlaceInventorInventoryItemLate`；在钩子里上架，沿用博士的加价逻辑 | 可行（已实现） |
| 特殊 NPC「可疑的走私犯」 | 仿照「黄牛贩子」构建 `StoreClient`，在 `OnGenerateCustomerNormal` 中按概率加入；注册到 `StoreClientListDict.storeClientDict` 以便存读档；溢价用 `onItemAddedToWeighted` 加 `ItemFeatureList.ScalperMarkup(50)` | 可行（已实现） |
| 工程 / 证据战利品箱 | `PreBuiltItemHelper.FillEngineering` / `FillEvidence` 为代码硬编码（非数据表，private static） | 可行（已实现，Harmony 后置补丁） |
| 读取机器与模组 | 机器的模组舱即 `GameInventory`；模组为其中的 `GameItem` | 可行 |
| 应用布局 | `GameInventory.CheckShapePlacement` / `TryInventorySlot`（public）；放入 / 取出触发 `ModuleEffectHelper.OnAddedModuleToGrid` / `OnRemovedModuleFromGrid` → `ComputeAllModuleTempStat` 重算属性 | 可行 |
| 卡住的模组 | 物品特性「模组卡住」，需模组提取器才能取出 | 必须固定不动 |
| 面板「套用外部工具界面」 | 游戏内没有网页渲染组件；游戏为 Mod 提供 `CustomUIManager.Window(...)` 构建器（标签、按钮、输入框、下拉框、滚动区、网格、分页、图片、进度条，可拖动、Esc 关闭） | 不能直接嵌入 HTML，用 CustomUI 构建器重做界面，计分与搜索移植为 C# |

## 主要工作量与风险

1. 计分器与精确搜索从 JS 移植到 C#（约 800 行核心 + 机器换算），用外部工具的测试数据做对照测试。
2. Unity UI 重做面板，并适配游戏的像素窗口系统。
3. 构建需要 .NET SDK；编译时引用本机游戏 DLL（不提交、不分发）。
4. 只能由玩家在游戏内实测；游戏更新可能需要跟进适配。
