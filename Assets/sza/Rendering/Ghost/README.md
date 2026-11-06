# 幽灵玩家半透明材质

## 已配置
- Shaders/GhostTransparent.shader：Sza/Ghost/Transparent Depth Prepass。
- Materials/GhostPlayer.mat：保留 texture_diffuse.png 和 texture_normal.png，沿用原材质颜色及 Alpha。
- GhostDepthPrepass.asset：URP 内置 Render Objects Renderer Feature 的独立配置资源。
- 当前场景 base_basic_pbr 已应用 GhostPlayer。
- 当前 PC 渲染器 Assets/Xcy/Rendering/Xcy_PC_Renderer.asset 已引用此 Feature。

## 原理
在普通透明物体绘制之前（RenderPassEvent 449），Render Objects 只绘制 LightMode 为 SzaGhostDepth 的通道。
该通道 Cull Back、ZWrite On、ColorMask 0，只记录最近表面的深度。
随后 ForwardLit 使用 Alpha 混合、ZWrite Off 和默认 LEqual 深度测试绘制颜色，使更远的自身表面被遮挡。
不透明场景已先绘制，因此仍能透过模型看到城墙等不透明背景。
使用 URP 内置 Render Objects 实现，支持本项目 Unity 6 的 Render Graph 和兼容渲染路径；不改相机矩阵，保留 URP 原有立体渲染宏。
尚未在 PICO 实机双眼或多人环境中验证。

## 调整
选择 Materials/GhostPlayer.mat，修改 Base Map 颜色的 Alpha；数值越小越透明。
保持 Surface Type=Transparent、Blending Mode=Alpha、Render Face=Front。
原来的 playerMat.mat 和 playerMat_Transparent.mat 都保留，可重新赋给模型来恢复。
其他新模型可直接使用 GhostPlayer，或复制该材质并更换贴图。
本次仅修改场景中该模型的材质，没有把幽灵模型替换进网络玩家预制体。

## 渲染器依赖
在采用其他 Renderer Data 时，需要在该 Renderer 的 Renderer Features 中配置同样的 Render Objects：
Event=Before Rendering Transparents 前一时刻，Queue=Transparent，LayerMask=Everything，Pass Names=SzaGhostDepth，Overrides=None。
不要使用默认 Pass Names，否则会额外绘制其他透明材质。
当前配置只接入 PC 渲染器，Mobile 渲染器未修改。
不需要在 NetworkManager 或玩家上挂额外脚本。

## 限制
- 深度为摄像机共享：两个幽灵重叠时，较近的幽灵会遮挡较远的幽灵，并非每个幽灵独立求深度。
- 其他在该深度通道之后绘制的透明物体，也会受到幽灵深度遮挡。
- 使用 Alpha=0 的材质会跳过深度写入；不适用于依靠贴图 Alpha 形成柔软半透明镂空的模型。
- 当前 Shader 不提供投影通道，避免半透明幽灵产生实心投影。
- 编辑器预览验证通过；头显实机效果仍需运行确认。
