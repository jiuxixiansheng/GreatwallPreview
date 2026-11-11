# 罗哲文 Timeline 角色

预制体：`Assets/sza/Characters/LuoZhewen/LuoZhewen.prefab`。原始带头发模型及材质仍引用 Cly 资源。

## 场景中的调整位置

在 `szaScene_Final` 中：

- `LuoZhewen_root`：角色本体，由 Timeline 的显示轨道控制激活。
- `LuoZhewen_Points/SpawnPoint_LZW`：出场位置与初始朝向。运行时角色激活会读取这里。
- `LuoZhewen_Points/MovePoint_LZW_01`：示例导航终点；移动位置须落在该章 NavMesh 上。
- `NetworkManager/NetworkTimelineActorCoordinator`：附加角色的联机同步注册。

角色和导航点位于场景根层级，避免章节美术物体的缩放影响身高和导航距离。角色的 `Timeline Character Actor` 上可替换 Spawn Point 和 Director；模型的离地偏移在 `Visual` 子物体本地 Y 上调整。

## timeline_02_2

新增的“罗哲文 · 动画与导航”轨道组可以整体移动，片段时间只是可编辑的初始示例：90.8 秒出场，走到导航点，94 秒开始说话与嘴型。配音轨留空，请自行拖入配音并排列。

| 轨道 | 绑定对象 | 片段填写 |
| --- | --- | --- |
| 显示 | LuoZhewen_root | Active 片段控制出场区间 |
| 身体 | Visual/LZW_URP_WithHair 上的 CharacterTimelineAnimator | `BaseLayer.Idle`、`BaseLayer.Talk`、`BaseLayer.Walk` |
| 独立上半身 | 同上 | `UpperLayer.Talk`、`UpperLayer.Walk` |
| 嘴型表情（Timeline Face Track） | 根物体上的 TimelineCharacterActor | `FaceLayer.Speech`，中性表情为 `FaceLayer.Idle` |
| 导航位移 | 根物体上的 DragonNavMeshMover | Destination 选择导航点，片段长度决定走完路径的目标时间 |
| 转向 | 根物体上的 DragonTurnController | 片段角度控制累计转向 |
| 配音（自行排） | Voice 上的 AudioSource | 拖入需要的声音文件 |

嘴型使用 `AS_LZW_Face_all_assemble.FBX`，原速约 87.42 秒，未按配音长度拉伸。现有时间线中的声音排列保留；请自行移动这段嘴型、拆分片段或安排配音。罗哲文专用的 Timeline Face Track 支持编辑器拖动预览和 Clip In 裁切，暂停后按片段进度继续，片段结束回到 FaceLayer.Idle。普通播放保持 1 倍速。

添加新的嘴型时，在 `AC_LuoZhewen.controller/FaceLayer` 中添加状态、指定动画，再将完整状态路径填入 Timeline Face Clip。身体和上半身同理。Reference Animation Clip 用于提供片段长度；真正播放的动画由 State Path 对应的 Animator 状态决定，两处应保持一致。

## 上下半身与步速

`Distance Driven Locomotion` 上的“步速匹配倍率”默认 1。数值越大，同样的移动速度下腿部动画越快，越小越慢；没有固定的动画倍速上下限。0 会停止腿部动画。

腿部倍速 = 实际水平移动速度 / Reference Movement Speed × 步速匹配倍率。

排走路时同时添加身体 Walk 片段与导航位移片段；走路上半身会自动以 1 倍速播放。想边走边说话时，另外添加 UpperLayer.Talk 的上半身片段及嘴型、配音即可。

Reference Movement Speed 是走路动画以 1 倍速播放时对应的每秒移动米数，初始按脚部运动估算，可手动校准。位移由 NavMesh 控制，模型动画不使用 Root Motion。

NavMeshAgent 的 Agent Type 使用项目已烘焙的 `Dragon` 类型，共用当前关卡导航网格；名称不代表模型骨架类型。若以后重新烘焙为其他 Agent Type，角色上的设置也要一起修改。

Animator 的四层：BaseLayer 控制身体和腿；WalkUpperLayer 在走路时自动以 1 倍速覆盖上半身；UpperLayer 用于时间线指定的说话手势等；FaceLayer 最后叠加嘴型。只有 BaseLayer.Walk 使用 MoveAnimationSpeed，手势与嘴型不跟随腿部倍速。

角色读取时间线的暂停状态：等待望远镜交互时暂停动画和导航，继续时间线后恢复。联机由 Host 控制导航，并同步角色的显示、位置、转向、动画参数和状态；客户端不独立计算导航。若增加更多角色，Actor Id 必须唯一，并在 NetworkManager 的附加角色列表注册。

## 再次使用

其他时间线也可绑定同一预制体上的组件。需要独立角色实例时复制实例、修改 Actor Id，并配置自己的出场点、Director 和同步注册。菜单 `Tools/Sza/罗哲文/创建角色并接入 part2-2` 用于首次创建；重复运行不会覆盖已有预制体或重复添加轨道，也不会替你重排声音。
