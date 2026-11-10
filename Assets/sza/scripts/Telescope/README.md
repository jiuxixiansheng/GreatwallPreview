# 望远镜视频交互

`szaScene_Final` 中的配置：

- `NetworkManager` 上的 `TelescopeVideoManager` 管理联机触发与本机播放器。
- `part2-2/wangyuanjing` 上的 `TelescopeVideoInteraction` 检查本机左右手与触碰范围的距离。
- 子物体 `TelescopeTouchPrompt` 显示“请触碰望远镜”，随望远镜启用、关闭，并朝向本机玩家。
- 视频绑定 `Assets/sza/video/春.mp4`，遮罩 Shader 位于 `Assets/sza/Rendering/Telescope`。

任一玩家触碰后，服务端验证距离、拒绝重复请求，并向所有玩家发送同一播放时间。每台电脑读取自己打包内的视频，不传输视频帧。播放期间剧情和玩家 XR 跟踪继续运行；进入时场景淡黑，再淡入双眼望远镜画面；视频结束后淡黑，再回到当前场景。退出联机或视频解码失败也会清理覆盖画面。

双眼遮罩使用两个横向相交的圆形镜口，显示同一幅连续视频，保留暗边和柔和镜框。画面保持原始比例，以居中裁切填满较宽的镜口，不横向拉伸或重复两份视频。

默认 `Play Once` 开启：本轮运行只触发一次，触发后隐藏提示。关闭它可重播，但手必须先离开触碰范围再进入，持续接触不会循环。`Fade Seconds` 调整淡入淡出时长，`Lens Radius` 调整镜口大小。替换视频时在 Manager 上修改 `Video Clip`。

运行时可在 `TelescopeVideoInteraction` 组件菜单使用“测试触碰望远镜”测试播放。实际触碰只读取 `NetworkClient.localPlayer` 的手部同步目标，远端手、玩家身体及射线远距离悬停不会触发。

联机消息为本功能新增，所有电脑需要使用同一次打包。未进行实机头显验证前，不能把编辑器单进程测试视为四台 PICO 的最终验收。
