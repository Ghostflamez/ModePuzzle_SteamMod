# ModePuzzle Mod

《深空当铺：可能是偷的》（Probably Stolen）官方 Mod 系统下的「模组优化器」游戏内版本，基于外部工具 [ModePuzzle](https://github.com/Ghostflamez/ModePuzzle) 的计分与搜索规则。

状态：第一步（入口物品 + 占位面板）。可行性见 `docs/feasibility.md`，构建与测试见 `docs/dev.md`。

## 约定

- 不分发任何游戏素材或代码：游戏 DLL 只在编译时从本机安装目录引用；贴图在运行时从游戏已加载的资源中读取和改色，不打包进 Mod。
- 游戏目录与存档目录在开发中只读；测试只用存档副本。
