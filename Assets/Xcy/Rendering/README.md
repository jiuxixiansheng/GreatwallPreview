# Xcy 室外雾

新 Shader 位于 Shaders/OutdoorFog.shader；没有修改 Assets/sza/shader/VolumetricFog.shader。

## 当前接入
- PC_RPAsset 的默认 Renderer 指向 Xcy_PC_Renderer.asset；原 PC_Renderer.asset 保留。
- Xcy Outdoor Fog 在透明物体之前、天空盒之后执行，使用深度纹理。不会改动灯光、天空材质或烘焙数据。
- 调参数：Materials/M_OutdoorFog.mat。Clear Near Distance 保持近处清晰；Outdoor Density 调浓度；Sky Horizon Strength 调天空地平线融合；Height Reference 和 Height Falloff 控制高度。
- 在场景姊妹楼的 ZiMeiLou1 层级下创建了 Xcy Indoor Fog Zone A/B，跟随各章节的启用状态。范围按模型包围盒生成，是初始范围，可能包括建筑外部平台和基座；应按实际室内边界微调。

## 室内排除
- 选择 Xcy Indoor Fog Zone 对象，在 Inspector 调 Center/Size，或在 Scene 拖动 Box 手柄。
- 新增范围：GameObject > Xcy > Indoor Fog Zone。复杂室内用多个 Box 拼接，允许重叠；没有添加物理 Collider。
- 支持旋转和缩放。Shader 扣除视线穿过室内 Box 的路段，因此室内看墙不加雾，通过窗户看到的室外仍有雾。
- 每台相机最多使用 16 个最近的激活 Box；章节停用时对应 Box 不参与。

## 限制与回退
- 透明玻璃、粒子、透明建造 Shader 在雾之后绘制，不单独接收本效果。这避免深度不匹配把透明物体涂成雾，但需要时应给透明 Shader 单独接入雾。
- Built-in 全局 Fog 建议保持关闭，避免重复计算。Mobile Renderer 尚未接入。
- 如需关闭，只需停用 Xcy_PC_Renderer 的 Xcy Outdoor Fog。完全回退时把 PC_RPAsset 的 Renderer List 恢复为 Assets/Settings/PC_Renderer.asset。
- XR 使用 stereo eye setup 和 TEXTURE2D_X，已经按双眼纹理接口编写；实际头显画面和 GPU 性能仍需要设备验证。

## Part2 运行时参数
- Part2 根对象上挂有 Part2OutdoorFog。章节逻辑启用 Part2 时，在 2 秒内切换到 Part2 参数；停用该根对象时立即恢复原材质。
- 选中 Hierarchy 的 Part2，在 Part2 Outdoor Fog 组件里修改 Part2 的颜色、密度、距离、高度、天空强度等参数，以及 Transition Seconds。
- 目前目标值：Density 0.00095、Sky Strength 0.3，其余沿用配置时的材质值。part2-2 没有绑定此控制器。
- 参数变化发生在运行时材质副本上，M_OutdoorFog.mat 本身不会被运行时修改。后续编辑原材质的默认值时，Part2 目标参数仍以组件里的设置为准。
- 已验证进入过渡、离开恢复、重复进入和退出 Play 清理。若替换 Renderer，需将组件的 Fog Feature 引用改为新 Renderer 中实际执行的雾 Feature。
