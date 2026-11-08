# 超科技模组优化器 · Hypertech Module Optimizer

《深空当铺：可能是偷的》（Probably Stolen）的游戏内 Mod：新增一件道具「超科技模组优化器」，为机器算出模组舱的最优布局，并能一键应用、一键恢复。计分与搜索规则移植自外部工具 [ModePuzzle](https://github.com/Ghostflamez/ModePuzzle)。

An in-game mod for *Probably Stolen*: adds the **Hypertech Module Optimizer**, a tool that works out the best module-bay layout for a machine and applies (or undoes) it with one click. Scoring and search are ported from the external tool [ModePuzzle](https://github.com/Ghostflamez/ModePuzzle). [English below.](#english)

## 功能

- **怎么获得**：杰克逊博士偶尔上架；可疑的走私犯会上门兜售；工程铸造厂物资箱、治安部物证柜里有小概率开出。它是赃物，也是高级违禁品。
- **怎么打开**：左键双击，或右键选择「激活」；也可以右键「装备」后点击一台机器，或直接把它拖到机器上。
- **优化**：选择目标（性能 / 效率 / 质量，或自定义主次目标和上下限），选择时长，开始搜索。可以：
  - 排除所有退化 / 过载模组；
  - 只用已插入本机的模组；
  - 把模组设为保留 / 固定；
  - 把空闲模组设为必选。
- **对比与应用**：「当前布局 / 优化结果」切换对比，「优化明细」逐件列出每个模组的贡献。「应用布局」会真的移动游戏里的模组，「恢复上一布局」可以撤销。
- **多台机器**：「更换机器」小窗列出所有带模组舱的机器（化学仪器除外）。解锁一台机器后，优化其他机器时可以借用它的模组。
- **空闲模组**：两列卡片，支持搜索、按种类 / 词条 / 状态筛选、按属性排序。
- **词条图标**（可在 Mod 设置里关闭）：游戏里所有模组都会显示特殊词条的小图标，并跟着旋转和翻转移动。
- 支持中文、英文界面；兼容「物品详情美化 · Better Item Tooltips」。

## 安装

- **Steam 创意工坊**：订阅后在游戏的模组管理里启用，按提示重启游戏。
- **手动安装**：把发布包解压到游戏目录的 `Mods/ModePuzzle/` 下。
- 本 Mod **不申请任何额外权限**（`<Capabilities />`）。

## 注意

- 「应用布局」会移动你的模组。移出机器的模组按背包、后柜台、前柜台的顺序存放，空间不够时会拒绝应用。首次使用前建议先存档。
- 卡住的模组（需要模组提取器才能取出）和损坏的模组会固定在原位。
- 机器的锁定状态和必选标记只在本次游戏运行期间有效，不写入存档。

## 从源码构建

需要本机装有游戏和任一 Unity 编辑器（使用其自带的 Roslyn 编译器），以及 Python 3。

```
python build.py              # 编译并整理到 dist/ModePuzzle/
python build.py --install    # 另外复制到游戏的 Mods/ModePuzzle/（游戏运行时会拒绝）
python build.py --dev        # 开发版：测试开关、热重载面板（需要额外权限，仅本地使用）
```

游戏的程序集只在编译时从本机安装目录引用，仓库和发布包里没有任何游戏素材或代码；物品贴图在运行时从游戏已加载的资源中读取并改色。更多开发说明见 [docs/dev.md](docs/dev.md)。

## 协议

[MIT](LICENSE)。本项目为玩家制作，与 Questing Goose Studio、tinyBuild 无关。

---

## English

- **Getting one**: Dr. Jackson sometimes stocks it, a suspicious smuggler drops by to sell one, and engineering and evidence crates have a small chance to contain one. It is stolen goods and high-level contraband.
- **Opening it**: double-click it or choose Activate; or Equip it and click a machine, or drag it onto a machine.
- **Optimizing**: pick a target (Performance / Efficiency / Quality, or a custom primary and secondary target with bounds) and a time limit, then search. You can:
  - exclude all Degrading / Overvolted modules;
  - use only the modules installed in the machine;
  - mark modules Keep or Fixed;
  - mark spare modules Required.
- **Comparing and applying**: switch between Current layout and Result, read each module's contribution in Breakdown, then Apply layout (this really moves your modules) or Restore previous.
- **Several machines**: Change machine lists every machine with a module bay (chemistry machines excepted). An unlocked machine lends its modules when you optimize the others.
- **Spare modules**: cards with search, filters by kind / trait / state, and sorting by a stat.
- **Trait icons** (can be turned off in the mod settings): every module in the game shows small icons for its special traits, following rotation and flips.
- Chinese and English; works with *Better Item Tooltips*.

**Install**: subscribe on the Steam Workshop and enable it in the game's mod manager (restart when asked), or unpack a release into `Mods/ModePuzzle/` in the game folder. The mod requests **no capabilities**.

**Notes**: Apply layout moves your modules. Modules taken out of the machine go to your inventory, then the back counter, then the front counter; if there is not enough room, nothing is applied. Save before first use. Stuck modules (they need a Module Extractor) and broken modules stay in place. Machine locks and Required marks last until the game closes; nothing is written to your save.

**Building**: needs the game, any Unity editor (for its Roslyn compiler) and Python 3; see the commands above and [docs/dev.md](docs/dev.md). No game assets or code are in this repository or in the release; the item picture is read and recoloured at run time from the game's own loaded sprites.

**License**: [MIT](LICENSE). A fan-made project, not affiliated with Questing Goose Studio or tinyBuild.
