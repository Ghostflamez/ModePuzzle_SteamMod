"""Narration and shots of the Bilibili promo video for the in-game mod (Chinese).

Each segment is a card scene (scenes/card.html#<scene>) or a part recorded in the game ('game': True),
driven through the development build's command channel with the real mouse. `lines` are narration sentences
and double as subtitles; `shots` describes what the recording does while they are spoken (the recorder
implements them per segment id).
"""

SEGMENTS = [
    {'id': 'title', 'card': 'title', 'lines': [
        '在《深空当铺》里，机器的模组舱就那么几格。',
        '模组越攒越多，怎么摆属性才最高？每次都要拼上半天。',
        '上期我们介绍了网页工具「模组拼合实验室」，这次，我们把它直接搬进了游戏里。',
    ]},
    {'id': 'intro', 'card': 'intro', 'lines': [
        '它叫超科技模组优化器，是一件可以拿在手里的新道具。',
        '计分和搜索沿用了模组拼合实验室的规则，但不用再导入存档、来回切窗口。',
        '它直接读取机器里和仓库里的模组，算出最优布局，还能一键应用到游戏里，不满意随时恢复。',
    ]},
    {'id': 'obtain', 'card': 'obtain', 'lines': [
        '怎么拿到它？杰克逊博士偶尔会上架，可疑的走私犯也会上门兜售。',
        '运气好的话，工程物资箱和治安部物证柜里也能开出来。',
        '它是偷来的超科技，算赃物，也是高级违禁品，小心治安检查。',
    ], 'shots': 'card: three sources with the item art'},
    {'id': 'open', 'game': True, 'lines': [
        '拿到之后，左键双击就能打开。',
        '也可以右键装备，再点一下要优化的机器。',
        '面板会直接切换到这台机器。',
    ], 'shots': 'double-click the item in the inventory; close; equip; click a machine'},
    {'id': 'tour', 'game': True, 'lines': [
        '左边是模组舱当前的样子，右边是三项属性。',
        '点「更换机器」，可以在店里所有带模组舱的机器之间切换。',
        '鼠标停在机器上，还能看到游戏原生的物品详情。',
    ], 'shots': 'hover the bay; open the picker; hover a machine; pick one'},
    {'id': 'run', 'game': True, 'lines': [
        '选择优化目标，比如性能，再选一个搜索时长。',
        '点击开始优化，搜索在后台进行，不影响游戏。',
        '几秒钟后结果就出来了：右边列出当前、优化后和变化。',
        '左上角可以在当前布局和优化结果之间来回切换对比。',
    ], 'shots': 'target 性能; time 5 s; start; wait; toggle 当前布局 / 优化结果'},
    {'id': 'result', 'game': True, 'lines': [
        '下面列出了布局里的每一件模组，新加入和挪了位置的都有标注。',
        '优化明细逐件列出每一分是从哪来的。',
    ], 'shots': 'scroll to the cards; Breakdown tab'},
    {'id': 'apply', 'game': True, 'lines': [
        '满意的话，点「应用布局」，模组会真的在机器里重新摆好。',
        '换下来的模组会放回背包或柜台。',
        '想退回去，点「恢复上一布局」就行。',
    ], 'shots': 'apply; show the real machine bay; restore'},
    {'id': 'control', 'game': True, 'lines': [
        '每件模组都可以设为不锁、保留或固定。',
        '卡住的模组会自动固定，退化和过载模组也可以一键排除。',
        '解锁一台机器之后，优化别的机器时还能借用它的模组。',
    ], 'shots': 'hover the lock buttons; tick the exclusions; unlock a machine'},
    {'id': 'custom', 'game': True, 'lines': [
        '只追求一项属性，另一项可能掉得很厉害，这时可以用自定义目标。',
        '选好主次目标，再给效率设一个下限。',
        '搜索会先满足所有限制，再让主目标尽量高。',
    ], 'shots': 'custom target; primary 性能; efficiency min; search'},
    {'id': 'spare', 'game': True, 'lines': [
        '空闲模组页可以搜索名称和词条，也能按种类、状态筛选，按属性排序。',
        '看中哪件，勾上必选，优化时就一定会用上它。',
    ], 'shots': 'Spare modules; type a search; filter; tick 必选'},
    {'id': 'badges', 'game': True, 'lines': [
        '另外，游戏里的模组都会显示词条小图标，跟着旋转翻转，也可以在设置里关闭。',
        '界面支持中英双语，也兼容物品详情美化模组。',
    ], 'shots': 'close the panel; show the inventory with trait icons'},
    {'id': 'install', 'card': 'install', 'lines': [
        '安装很简单：在创意工坊订阅，进游戏在模组管理里启用，重启一次就好。',
        '它不申请任何额外权限，也不会改动你的存档文件。',
    ]},
    {'id': 'outro', 'card': 'outro', 'lines': [
        '超科技模组优化器和模组拼合实验室一样，是非官方的粉丝作品，代码以 MIT 协议在 GitHub 开源。',
        '喜欢在游戏外慢慢研究的，网页版依然可以用；想在游戏里一键搞定的，就试试这个模组。',
        '觉得好用的话，欢迎一键三连，也欢迎到创意工坊点个赞、提提建议。',
        '祝各位老板的当铺生意兴隆，我们下期再见！',
    ]},
]

# English words in the narration, as Kokoro (misaki US) phonemes.
EN_PHONEMES = {
    'github': 'ɡˈɪthʌb',
    'mit': 'ˌɛmˌItˈi',
}

LEAD_IN = 0.45      # seconds before the first sentence of a segment
GAP = 0.32          # pause between sentences
TAIL = 0.7          # hold after the last sentence
