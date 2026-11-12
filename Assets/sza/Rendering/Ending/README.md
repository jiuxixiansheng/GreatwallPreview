# 360 度全景片尾

流程：骑龙到达 `FlightPhase.Ending` → 各端渐暗并确认全黑 → Mirror 切换 `szaScene_end` → 准备首帧与本机头显 → 全体准备好后淡入 → 老法师照片溶解出现 → 完整展示 10 秒 → 照片溶解消失 → 完整播放片尾 → 所有仍在线的观众播完后渐暗 → Menu → 清理旧联机会话。

## 场景与调整

- 场景：`Assets/Scenes/szaScene_end.unity`，已追加到 Build Settings。Menu 的关卡入口索引 1 保持为 `szaScene_Final`。
- 球体：`Ending Presentation/360 Ending Video Sphere`，直径 100 米，单面向内渲染，不受光照影响，无碰撞体。
- 位置：`Ending Presentation/Sphere Center`，默认 `(0, 1.6, 0)`。进入时移动本机 XR Origin，使实际头显位置与球心重合，补偿玩家已有的大空间行走偏移。之后球体跟随头显，保持全景观看中心；不逐帧改写追踪姿态。
- 配置：`Assets/sza/Resources/EndingSequenceSettings.asset`。可替换视频、调整淡入/淡出秒数、同步起播预留时间，以及水平旋转角度。
- 照片：`Assets/sza/tex/老法师.jpg`，按原始 4:3 比例导入和显示。场景物体为 `Ending Presentation/老法师 · 溶解照片`。
  默认高 5 米、距眼睛 5 米，进入后放在玩家正前方并保持世界位置稳定。
  `Photo Dissolve In Seconds` 默认 1.5 秒，`Photo Hold Seconds` 为完整展示的 10 秒，`Photo Dissolve Out Seconds` 默认 1.5 秒。
  可在配置中修改尺寸、距离与三段时长；`EndingPhotoDissolve.mat` 可调整边缘颜色、宽度和噪声，参考原 `URP_DissolveBuildUp` 的噪声与边缘方式并改为不受灯光影响的照片显示。
  照片阶段视频已预备首帧，但保持暂停；溶解消失完成后才从第 0 帧播放。联机晚加入的观众也会完整观看照片和视频。
- 默认视频：`Assets/sza/video/大空间片尾V2.mp4`，4000×2000、2:1 单目 360 全景，Unity 读取约 21.75 秒。源文件没有音轨，当前不额外添加音乐；换成含音轨的视频后第一条音轨使用 2D AudioSource 播放。
- Scene 内没有 Directional Light、Skybox 或环境光。相机只渲染 `EndingPresentation` 层，因此其他玩家、手部、手柄和射线不会进入片尾视野。
- 片尾相机单独使用 `EndingRenderer.asset`，不包含游戏关卡 Renderer 的室外雾、调色等全屏效果；PC/Mobile 管线保留各自原来的默认 Renderer。进入片尾时为本机相机选择专用 Renderer，退出后还原。
- 全景球为不透明材质并写入深度，防止后绘制的天空背景覆盖球面。片尾的 Scene 视图也临时使用专用 Renderer，离开片尾场景自动还原，避免编辑预览与实际播放的雾效不同。
- 可通过 `Tools/Sza/片尾/配置全景片尾场景` 重新生成演示对象和配置引用。此操作会重建片尾演示根物体。

## 联机和资源生命周期

`EndingSequenceCoordinator` 在游戏场景加载后自动安装到现有 NetworkManager，不需要往片尾放第二个 NetworkManager。没有网络会话时，直接运行片尾场景也能独立播完并返回 Menu。

服务器等待所有已连接观众分别报告：骑龙黑场完成、视频首帧与相机就绪、视频实际播放结束、返回黑场完成、Menu 加载完成。依据 VideoPlayer 的 `loopPointReached` 判断完成，不用固定等待时长替代播放完成，也不向网络时间跳播以追赶进度。

片尾开始后加入的观众从头观看，服务器等待其完整结束；一旦进入返回 Menu 阶段，暂停新连接入场。断开的观众退出等待，重新连接不会沿用之前的完成状态。解码失败不计为播放完成，Console 会记录视频错误并保持流程等待，避免跳过片尾。

离开游戏场景前会关闭飞行复位循环、解除座位跟随，并暂停场景相关的章节/角色/望远镜网络服务；加载片尾后销毁这些旧服务。NetworkManager 和传输层保留至所有观众进入 Menu，再关闭会话并销毁旧管理器，便于重新点 Host/Client。

原 `FlightFadeController` 增加不受时间缩放影响的黑场、有效相机确认、相机延迟绑定和资源清理，使用始终覆盖画面的 XR 黑幕 Shader，保证近处手部不会挡住黑场。

## 验证范围

已通过 Unity MCP 检查场景/视频引用和 Shader，并实测直接运行片尾、Host 片尾播放及从真实游戏场景触发飞行结束后返回 Menu 的流程。现场的双机和头显最终观感仍应在实际设备上验收。
