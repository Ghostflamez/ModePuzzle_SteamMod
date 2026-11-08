# 开发与测试

## 环境

- 编译器：Unity 编辑器自带的 Roslyn（`Editor/Data/DotNetSdkRoslyn/csc.dll`，用 `Editor/Data/NetCoreRuntime/dotnet.exe` 运行），无需另装 .NET SDK。`build.py` 从 Unity Hub 的记录中自动找编辑器，也可用环境变量 `UNITY_EDITOR_DATA` 指定 `Data` 目录。
- 游戏：从 Steam 库自动查找，也可用 `PROBABLY_STOLEN_DIR` 指定。编译时只引用 `Probably Stolen_Data/Managed` 下的 DLL，不复制。
- 目标游戏版本：0.46D（`manifest.xml` 的 `GameVersions`）。

## 命令

```
python build.py              # 编译并整理到 dist/ModePuzzle/
python build.py --install    # 另外复制到游戏的 Mods/ModePuzzle/；游戏运行中会拒绝安装
python tools/icon_preview.py # 在游戏外生成物品贴图预览 build/icon_preview.png（需要 UnityPy、Pillow）
```

`dist/`、`build/` 不进仓库。

## 引擎对照测试

`src/Engine/` 是外部工具 `src/core/engine.js`（计分与搜索）的 C# 移植，不依赖游戏或 Unity。

```
python tools/parity/run_parity.py              # 生成用例并比对
python tools/parity/run_parity.py --keep-cases # 复用 build/parity_cases.json
```

`gen_cases.mjs` 用 Node 运行外部工具的 JS 引擎（`MODEPUZZLE_DIR`，默认 `../ModePuzzle`），生成随机计分用例（逐行比对 self / 吸收 / 接收器 / 合计，以及每件的全部摆放）和小规模求解用例（比对已证明最优的目标值）；`Parity.cs` 与引擎源码一起编译，用 Unity 自带的 Mono 运行。改动引擎后必须跑通。

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
   - 杰克逊博士：正常为每次上货 10%（已持有时不出现）；打开「开发测试：杰克逊博士总是出售……」后来店时和去他店里必定上架，忽略是否已持有。价格按博士的常规加价，带赃物标签（热度 20–35%）；
   - 可疑的走私犯：打开「开发测试：可疑的走私犯每天必来」，第二天开店后出现；只卖超科技模组优化器，价格带 +50% 的「供货商加价」，带赃物标签（热度 45–65%）；若到店时你已持有一台（例如测试时开着「读档后放一台」），他改为收赃模式：换一套台词，按 75 折收购你的赃物；
   - 物资箱：同时打开「开发测试：……必出超科技模组优化器」和「开发测试：读档后在柜台放一个已开锁的……」，读档后柜台上的两个箱子里各有一台（装不下时按游戏规则销毁）。
6. 面板（第三步，只读）：激活后选择机器，检查
   - 机器列表包含店里和背包里所有带模组舱的机器（化学类机器除外），同名机器带编号；
   - 模组舱格子形状与游戏一致（未升级的标准机器四角和中间十字不可用），已安装模组的颜色块位置与游戏内摆放一致；
   - 已安装列表的名称、效率/质量/性能、词条与游戏提示框一致，卡住的模组有标注；
   - 「可放入本机器的空闲模组」只列出类型符合的模组；在游戏里挪动模组后点「刷新」能看到变化。
7. 测完把设置里的开发测试选项都关掉。

日志在 `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`，搜索 `modepuzzle` 或 `ModePuzzle` 即可找到本 Mod 的记录（加载、物品注册、贴图生成失败时的警告）。

## 第五步：优化、应用与恢复

面板保留机械窗口样式和半屏宽度。先选择机器、目标和时限（默认 15 秒）；已安装模块可设为“保留在此机器”或“固定位置”，卡住模块自动固定。默认候选为该机模块加兼容且放得下的空闲模块，可切换为只用该机模块。

“开始 / 重新搜索”创建主线程快照，在单独工作线程上搜索；“停止”保留当前最好结果，再次开始会重新搜索，不续跑旧 DFS 栈。状态约 8 Hz 刷新；“查看预览”可在运行时查看当前最好布局，完成后自动展示最终结果。预览不移动物品，只有“应用布局”修改游戏。机器、候选模块或设置变化后保留旧预览并标记失效。

应用先验证快照、游戏锁及摆放，为所有移出模块按背包、后柜台、前柜台顺序预留空间，然后使用游戏的 Expel 和经校验的 SlotMarker.TryAcceptOnce。成功后可一键恢复上一布局；读档清空恢复记录，变更后的布局拒绝盲目恢复。空间预留是保守的顺序放置，不利用即将移入机器的模块腾出的空间；提示空间不足时先整理库存。

离线验证（均不启动游戏、不安装）：

```
python tools/parity/run_parity.py --keep-cases
python tools/parity/run_fixed.py
python tools/parity/run_shapes.py
python build.py
python tools/scan/scan_mod.py
```

`run_fixed.py` 对比小棋盘穷举，并检查工作线程取消、心跳和超时；`run_shapes.py` 直接使用本机游戏 DLL 的 GridShape 检查摆放变换。能力扫描只应检测到 `dynamic-code`；此离线脚本未传游戏程序集白名单，因此仍显示已有的 `unlisted-reference` hard flags。

真实规模基准：先手动把测试存档复制为 `build/save_7.es3`，然后执行 `node tools/benchmark/read_real.mjs`、`python tools/benchmark/run_real.py`。导入器只读取相邻 ModePuzzle 仓库，结果写入本仓库 `build/`。不要提交存档或生成的数据。

故障测试仅用测试存档：打开“开发测试：应用首次放入后模拟失败”，对一个确实需要移动的结果点击应用。应报告“放入”阶段失败、恢复成功，原机器和原库存摆放均恢复；检查 Player.log。测试后关闭此选项。完整验收清单和本次数据见 `build/astra_step5_report.md`；本次未运行游戏。

## 后续版本待选项

- **候选超过 100 件**：目前直接报错（`opt_too_many`），请玩家移走一部分或勾选“只用本机模组”。待选做法：
  开始搜索前自动剔除被其他模组完全压过、不可能进入最优解的候选（三项属性都不更好、形状不更小、
  也没有有用的词条），剔除后仍超过上限才报错。
- 词条图标：拖动模组时原位置的半透明虚影上没有图标（目前有意不做）。
