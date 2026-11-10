# 其余章节音效粗排

最新状态：玩家交互音效已接入InteractionAudioSystem，跟随实际交互播放，没有放回Timeline。以 Assets/Xcy/Scripts/Audio/README.md 为准；最新原表标注文件为 outputs/timeline-audio-20261108/龙脊之光音效表格_交互音效接入版.xlsx。

当前更新：依赖玩家操作的音效已经从所有实际使用的Timeline移除，音频资源保留且暂不接入交互播放。具体范围见“交互音效暂不播放说明.md”。以下交互提示、修复完成和占位时点属于此前粗排记录；最新原表标注文件为 outputs/timeline-audio-20261108/龙脊之光音效表格_交互音效移除版.xlsx。

本目录保存 Part2、Part2_2、Part3 使用的新增音频。与 Part1 共用的龙神脚步等资源仍引用 Assets/Xcy/Audio/Part1，避免重复导入。

## Timeline 和音源

- Part2/timeline_02：敌台中层环境、点灯、脚步、震动、落石、开裂、修缝提示与完成、窗景、法阵转场。对应预制体 Part2_SFX_Rig。
- part2-2/timeline_02_2：顶层与小楼环境、施法、小楼复原、脚步、来访脚步、望远镜施法，以及罗哲文段落的大提琴背景音乐。对应预制体 Part2_2_SFX_Rig。
- StoryManager/timeline_03：台顶环境、真龙现身、巨龙呼吸。对应预制体 Part3_SFX_Rig。
- Part1 音源保持 Part1_SFX_Rig。四个预制体均位于 Assets/Xcy/Prefabs/Audio，场景实例使用相同名称。

各 Timeline 的新增轨道位于“音效粗排”组；原有动作、配音、信号和移动片段时间及引用保持不变，章节总时长保持不变。

## 时点与待确认内容

timeline_02 的交互音效现由独立交互系统播放，已不在时间线上占位。用户手动调整后的震动、落石、开裂起点分别为101.49136、102.39136、104.29136秒，保持原样。四季按当前60fps从8600帧（143.33333秒）开始，结束沿用11664帧（194.4秒），按春夏秋冬顺序均分，每季766帧、约12.76667秒。四季期间敌台中层环境由0.65降为0.20，边界各0.3秒交叉过渡，结束后恢复原音量。未改动窗景视频、四季音量或交互事件。

timeline_02_2 的小楼复原音对齐33.4087秒的现有信号。历史视频环境、罗哲文消失音仅为静音占位；原剧情与当前演出结构存在差异，尚不能确认对应视频或消失时刻。

timeline_03 的真龙现身音对齐2秒变身信号；巨龙呼吸在本段内估排。登龙提示和起飞音仅为静音占位，正式播放应绑定 DragonBoardingGate 等实际事件；不能将24.95秒的“飞喽”台词等同于玩家已经上龙。起飞占位片段受27.3533秒章长截取，素材本身完整保留。

所有新增音源暂用2D，供审听剧情节奏；未进行完整VR试听。环境随各自Timeline暂停和结束，不覆盖交互等待或章节结束后的独立飞行流程。

## 清单与备份

使用清单位于项目 outputs/timeline-audio-20261108/龙脊之光_Timeline音效使用清单.xlsx，含文件级使用总览与全部实际片段明细。清单是导出快照，修改表格不会自动修改Unity。

三个修改前的 Timeline 副本位于 Assets/Xcy/Timeline/Backups，命名为 timeline_02_before_sfx、timeline_02_2_before_sfx、timeline_03_before_sfx。操作前场景内存快照位于 Temp/RemainingSfx/before_remaining_sfx.unity，Temp可能被清理。

原表缺失的大提琴环境已用 Scott Buckley 的 Echoes 补充，工程名为“大提琴环境.mp3”，用于timeline_02_2的93.174235–141.375782秒背景音乐。音量14%，淡入2秒、淡出3秒，原曲开头截取48.201547秒。授权与发布署名要求见“大提琴环境_授权说明.md”。最新原表标注版为 outputs/timeline-audio-20261108/龙脊之光音效表格_背景音更新版.xlsx，保留四季均分标注，仅更新大提琴所在行的E列。

Part0已新增timeline_00，并接入StoryManager的章节0，仍保留右手A键手动切章。序章环境放在0–60秒，影壁发光35秒开始，光幕45秒开始；后两项为估时。详细接入说明见 Assets/Xcy/Timeline/timeline_00_说明.md。最新原表标注文件为 outputs/timeline-audio-20261108/龙脊之光音效表格_序章更新版.xlsx。
