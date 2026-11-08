# 交互音效

场景：Assets/Scenes/szaScene_Final.unity
场景对象：InteractionAudioSystem
预制体：Assets/Xcy/Prefabs/Audio/InteractionAudioSystem.prefab
音效配置：Assets/Xcy/Audio/InteractionAudioLibrary.asset

三个脚本分别负责配置、播放和联机事件。场景中的InteractionAudioSystem实例保存StoryManager、修墙区域、四条裂缝协调器、登龙传送板、FlightDirector和序章选择物体的引用。预制体保存通用组件和音效配置，场景引用保存在实例覆盖中。

## 已接入的入口

- 序章选择part0/polySurface9：先播放影壁发光声，结束后播放光幕声，结束后走原来的下一章流程。重复选择不会启动第二条流程。右手A键仍可跳过，跳过或切章会取消待执行的序列。此次不新增影壁视觉动画；播放器暴露onWallGlow和onLightCurtain供后续绑定已有视觉效果。
- timeline_01修墙暂停Signal：启动破损处提示循环。该信号仍在原来的暂停点，不添加音频片段。
- BrickDropZone首次接收有效砖块：播放放砖声；同一刚体的多个碰撞体不重复播放。三块砖完成时停止提示循环并播放咬合声。强制完成入口只播放完成声，不补播三次放砖声。
- Part2CrackRepairCoordinator.BeginRepair：进入四裂缝修墙流程时立即播放“破损处提示音”（CrackPrompt，音量0.35），每次播完等待1.5秒再播放。持续到四条裂缝全部修完；选择某条裂缝不会重新触发或停止这个流程提示。可在音效配置的CrackPrompt条目中调整repeatInterval（播完后的间隔秒数）；该条目loop关闭。等待期间也会去重联机状态包，完成、切章或禁用时取消待播放的提示。
- 每条CrackRepairDecal实际开始修复：单独启动“补墙”循环（CrackRepair）；该条完成或禁用时只停止其修补声。四条裂缝全部完成时停止流程提示并播放一次“修墙完成”。“发光提示音”当前不用于四裂缝流程。
- Part2实际发起切章转场的zhuanchang事件：播放法阵过渡声。目前未启用的旧transboard不额外绑定，也不新增站位交互。
- DragonBoardingGate.onBoardingShown：本地播放地面闪烁提示循环；onBoarded时停止。每个玩家的登龙提示独立，不广播另一个玩家的个人站位状态。
- FlightDirector的实际阶段进入TakeOff：每台电脑依据已同步的飞行阶段播放一次起飞声。登龙成功不等于起飞，不在onBoarded时提前播放。

## 播放和联机

环境、台词、四季、角色演出、石头脚步和大提琴背景音乐继续保留在Timeline。上述交互音效没有重新放回Timeline。

InteractionAudioPlayer使用独立音源池，不占用配音或Timeline音源；循环声按类型和裂缝编号去重。切章、系统禁用和断线会清理播放。Inspector中的library条目可调整AudioClip、音量、循环和空间混合比例，logPlayback控制运行日志。

公共修墙和修缝音效由Host确认并广播，Host不会在收到自己的消息时重复播放。客户端只能请求序章或指定裂缝的交互，不能上传任意音效播放请求。请求验证配置、章节、代次、交互解锁状态、玩家身份、距离和重复开始；默认最大距离12米，可在NetworkInteractionAudioRelay调整。

裂缝修复也接入同一Host确认路径。Host同步未开始/修复中/完成及开始时间，客户端恢复视觉进度和持续音；状态包不重播历史完成音等一次性音效。所有参与联机的电脑需要使用这次更新后的相同代码与场景。

## 操作

Quest 3实机保持XR Device Simulator关闭。当前场景直接Play后，从NetworkManager HUD启动Host。序章可选择影壁，或用右手A跳章；砖块用握持键抓放；裂缝阶段选择各裂缝，编辑器仍可用数字键1–4；上龙仍然走入传送板。

修改了BrickDropZone、CrackRepairDecal、Part2CrackRepairCoordinator的事件和联机修复入口；没有改动XR玩家预制体或FlightDirector源码。

修改前源码和场景快照位于Temp/InteractionAudioImplementation/backup。Temp可能被清理。原表最后一列已另存更新为 outputs/timeline-audio-20261108/龙脊之光音效表格_交互音效接入版.xlsx。
