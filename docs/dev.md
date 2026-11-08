# 开发与测试

## 环境

- 编译器：Unity 编辑器自带的 Roslyn（`Editor/Data/DotNetSdkRoslyn/csc.dll`，用 `Editor/Data/NetCoreRuntime/dotnet.exe` 运行），无需另装 .NET SDK。`build.py` 从 Unity Hub 的记录中自动找编辑器，也可用环境变量 `UNITY_EDITOR_DATA` 指定 `Data` 目录。
- 游戏：从 Steam 库自动查找，也可用 `PROBABLY_STOLEN_DIR` 指定。编译时只引用 `Probably Stolen_Data/Managed` 下的 DLL，不复制。
- 目标游戏版本：0.46D（`manifest.xml` 的 `GameVersions`）。

## 命令

```
python build.py              # 编译并整理到 dist/ModePuzzle/
python build.py --install    # 另外复制到游戏的 Mods/ModePuzzle/（写入游戏目录，由玩家自己执行）
python tools/icon_preview.py # 在游戏外生成物品贴图预览 build/icon_preview.png（需要 UnityPy、Pillow）
```

`dist/`、`build/` 不进仓库。

## 游戏内测试

1. 关闭游戏，执行 `python build.py --install`。
2. 启动游戏 → 主菜单的模组管理：找到「超科技模组优化器 Hypertech Module Optimizer」，确认权限（本 Mod 只申请「运行时加载或生成代码」，用于把物资箱掉落插入原版生成列表）并启用；如提示需要重启，重启游戏。
3. 在该 Mod 的设置里打开「开发测试：读档后在柜台放一台超科技模组优化器」，读取一个**测试用存档**（物品是高级违禁品，被治安检查发现会有后果）。
4. 检查：
   - 柜台上出现超科技模组优化器，贴图为米白机身、粉色屏框、金色按钮，屏幕上有一块白板金端盖的双格模组；
   - 名称、描述、价值 500、违禁品等级为高级；
   - 左键双击、右键菜单「激活」都能打开「超科技模组优化器」窗口，窗口可拖动、Esc 关闭；
   - 存档后重新读档，物品仍在且贴图正确。
5. 获取途径（数值见 `src/Acquisition/Tuning.cs`）：
   - 杰克逊博士：来店时和去他店里，货架上都有超科技模组优化器（已持有时不出现），价格按博士的常规加价（已持有时可打开「开发测试：杰克逊博士总是出售……」），带赃物标签（热度 20–35%）；
   - 可疑的走私犯：打开「开发测试：可疑的走私犯每天必来」，第二天开店后出现；只卖超科技模组优化器，价格带 +50% 的「供货商加价」，带赃物标签（热度 45–65%）；
   - 物资箱：同时打开「开发测试：……必出超科技模组优化器」和「开发测试：读档后在柜台放一个已开锁的……」，读档后柜台上的两个箱子里各有一台（装不下时按游戏规则销毁）。
6. 测完把设置里的开发测试选项都关掉。

日志在 `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`，搜索 `modepuzzle` 或 `ModePuzzle` 即可找到本 Mod 的记录（加载、物品注册、贴图生成失败时的警告）。
