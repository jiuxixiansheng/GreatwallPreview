# 望远镜视频交互

`szaScene_Final` 中的配置：

- `NetworkManager` 上的 `TelescopeVideoManager` 管理联机触发与本机播放器。
- `part2-2/wangyuanjing` 上的 `TelescopeVideoInteraction` 检查本机左右手与触碰范围的距离。
- 子物体 `TelescopeTouchPrompt` 显示“请触碰望远镜”，随望远镜启用、关闭，并朝向本机玩家。
- 视频由 Manager 的 `Video Clip` 指定，遮罩 Shader 位于 `Assets/sza/Rendering/Telescope`。

任一玩家触碰后，服务端验证距离、拒绝重复请求，并向所有玩家发送同一播放时间。每台电脑读取自己打包内的视频，不传输视频帧。播放期间剧情和玩家 XR 跟踪继续运行；进入时场景淡黑，再淡入双眼望远镜画面；视频结束后淡黑，再回到当前场景。退出联机或视频解码失败也会清理覆盖画面。

双眼遮罩使用两个横向相交的圆形镜口，显示同一幅连续视频，保留暗边和柔和镜框。画面保持原始比例，以居中裁切填满较宽的镜口，不横向拉伸或重复两份视频。

默认 `Play Once` 开启：本轮运行只触发一次，触发后隐藏提示。关闭它可重播，但手必须先离开触碰范围再进入，持续接触不会循环。`Fade Seconds` 调整淡入淡出时长，`Lens Radius` 调整镜口大小。替换视频时在 Manager 上修改 `Video Clip`。

望远镜出现并启用后，`TelescopeVideoInteraction` 会带动模型、触碰范围和子物体提示整体上下浮动。默认 `Float Telescope` 开启，`Telescope Float Amplitude` 为上下各0.06米，`Telescope Float Period` 为3.2秒一轮；按世界竖直方向计算，不受父物体缩放影响。联机使用共享网络时钟，剧情暂停时照常浮动；隐藏、禁用或关闭浮动后恢复原摆放位置，再次出现不会叠加偏移。原有 `Float Amplitude` 继续控制提示 UI 自身的小幅浮动。

运行时可在 `TelescopeVideoInteraction` 组件菜单使用“测试触碰望远镜”测试播放。实际触碰只读取 `NetworkClient.localPlayer` 的手部同步目标，远端手、玩家身体及射线远距离悬停不会触发。

`timeline_02_2` 的 `Telescope - Wait at Frame 8330` 信号轨在 8330 帧（60 fps 下为 138.833 秒）调用 `NetworkManager/TelescopeVideoManager.WaitForVideo`，由 Host 暂停并同步剧情。任一玩家的有效触碰触发全员视频时，Host 立即从暂停位置继续 Timeline，视频和后续剧情并行播放。视频结束只恢复视野，不再发送继续命令。如果已提前触碰播放过视频，经过等待点时直接放行，避免错过继续信号后卡住。Client 不会因本机求值或收到视频状态而单独控制剧情。

`Waiting Timeline` 指向该章节 Director，防止误恢复其他章节。需要重新绑定时，退出 Timeline 预览后使用 `Tools/Sza/配置望远镜剧情等待（8330帧）`；此命令保留已有视频和遮罩设置。修改等待时刻可直接移动信号 Marker；再次运行配置命令会将其恢复到 8330 帧。

联机消息为本功能新增，所有电脑需要使用同一次打包。未进行实机头显验证前，不能把编辑器单进程测试视为四台 PICO 的最终验收。
