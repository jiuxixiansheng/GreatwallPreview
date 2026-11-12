# 第零章配音与石壁提示

`part0/timeline_00` 的配音轨 `VO 画外音（1×2 → 2）` 按顺序播放：

- 0–58.176 秒：`Assets/sza/voice/画外音1.WAV` 第一次。
- 58.176–116.352 秒：同一段画外音1第二次。
- 116.352–123.456 秒：`Assets/sza/voice/画外音2.WAV`。

每个 Clip 都只播放一次，整个 Director 不循环。环境音循环片段延长至旁白结束；声音播完后保持在第零章等待交互。章节0启用 Timeline，关闭自动切章，触碰石壁仍走现有的联机交互与立即转场流程。Host 控制章节和 Timeline，后加入玩家由现有剧情快照对齐播放位置。

石壁 `part0/polySurface9` 上的 `PrologueWallBreathingGlow` 使用独立材质 `Assets/sza/mat/Prologue/M_PrologueWall_Breathing.mat`，保留原贴图和表面参数。发光贴图使用原 Base Map，以保留壁画细节；运行时通过 MaterialPropertyBlock 调整自发光，不改动共享材质资源。

Inspector 中可调 `Glow Color`（发光颜色）、`Cycle Seconds`（一次呼吸的时长）、`Minimum Intensity` 与 `Maximum Intensity`（最低和最高亮度）。默认暖金色、2.8秒一周期、亮度0.08–2。联机时使用共享网络时钟计算呼吸相位；交互开始转场、离开第零章或禁用物体后恢复原始发光值。

需要重新配置时，退出运行与 Timeline 预览，打开 Final 场景，执行 `Tools/Sza/配置第零章配音与石壁呼吸发光`。该命令会重建本功能的三段配音片段，并保留组件上已调好的呼吸参数。
