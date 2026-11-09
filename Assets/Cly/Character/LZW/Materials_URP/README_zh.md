# LZW 的 Unity URP 材质

适用工程：Unity 6000.0.55f1、URP 17.0.4。

## 直接使用

目录：`Assets/Cly/Character/LZW/Materials_URP`。

把 `Prefabs/LZW_URP.prefab` 拖到场景，即可查看配好的材质。这个 Prefab 引用工程原有的 `LZW_Assembled_ue.FBX`，没有改动模型、骨架或动画。

如果场景中已有绑定动画的角色，先选中该角色最外层对象，再点击菜单 `Tools > Cly > LZW > Apply URP Materials to Selected Character`。此操作只更换材质，支持 Ctrl+Z 撤销，不会自动保存场景。不要用它操作其他角色。

`Materials` 是已经生成的材质球；`Textures` 是从 UE 导出的贴图副本；`Shaders` 是自定义 URP Shader；`Editor` 是材质生成及分配工具。

## 调整效果

- `M_LZW_Face`：脸部使用 UE 当前配置的老人颜色贴图、MetaHuman 法线。`Roughness Multiplier` 越高越哑光；`Normal Strength` 控制皱纹细节；`Skin Light Wrap` 和 `Skin Backlight` 控制柔和光照近似。
- `M_LZW_Body`：使用 UE 身体材质实际引用的颜色和法线。源颜色图偏灰紫，已通过材质的 `Tint` 参数向脸部肤色调整，源贴图仍保留原样；需要时可以继续调色。
- `M_LZW_Clothes`：使用 UE 衣服材质的颜色和粗糙度图。
- `M_LZW_Eye_Left/Right`：眼白与虹膜纹理分开采样。`Iris UV Radius`、`Iris UV Center`、`Iris Tint` 可调整虹膜大小、位置和颜色；当前为近似效果。
- `M_LZW_Teeth/MouthSecondary`：使用牙齿和口腔图集；第二个口腔槽按照 UE 当前的材质配置处理。

颜色贴图使用 sRGB；法线和粗糙度为线性数据。法线已通过 Unity 导入器翻转绿色通道，不要再手动翻一次。所有图最大导入尺寸为 2048，开启 mipmap，关闭 CPU Read/Write。独立设备仍需要根据实际性能调整模型 LOD、骨骼和贴图尺寸。

## 当前边界

这是针对现有材质参数和贴图的 URP 近似实现。没有复制 UE 的屏幕空间皮肤次表面散射、复杂角膜折射、动态皱纹混合、RigLogic 材质曲线或发丝着色。两边灯光、曝光及色调映射不同，观感也会不同。

当前 `LZW_Assembled` 没有可用的头发材质：`M_Hide_8` 槽引用 UE 默认棋盘材质，睫毛材质透明度为 0。Unity 中这些槽暂时使用不写深度、不投影的透明材质。眼部原有的程序化 EyeShell 也暂时隐藏，避免错误覆盖眼球；泪膜保留轻微透明高光。后续有正确毛发卡片及透明贴图后应单独配置。

`Source` 保存 UE 源材质槽、参数与贴图对应报告；`validation.json` 保存独立 Unity 验证工程的结果，不代表当前业务场景或 VR 真机的验证结果。

## 重新生成

菜单 `Tools > Cly > LZW > Build URP Materials and Prefab` 可补建缺失资源。为保留你的调色，已存在的材质和 Prefab 不会覆盖。一般直接使用现有材质球即可。

## 前臂色带修正

身体 UV 在第二个区域（U=1..2），身体材质 Base Map 的 Offset X 已设为 -1。Shader 的颜色和法线使用同一套修正后的 UV，避免 Clamp 采样边缘而产生色带。模型、骨架和动画无需修改。
