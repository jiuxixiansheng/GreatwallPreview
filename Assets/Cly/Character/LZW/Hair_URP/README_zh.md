# 细化补密外壳（当前版本）

原始工程只有 Helmet LOD5/6/7，LOD5 已是最细的原生外壳。本版本是对原始外壳的细化派生，并非网上下载的高模，也不是原生独立发丝。直接在 Unity 原始网格上做两次线性细分，保持原有法线、切线、UV 和坐标，并按原始 Groom 深度加入最大 1.2 mm 峰谷差的表面起伏；附加由原始深度和覆盖率生成的表面细节法线。没有采用经过 Blender/FBX 转换的试验模型。

外壳三角面从 701 增至 11,216，含原始 LOD0 发片的完整头发共 48,492 三角面。仍有连续外壳感，不能通过细分恢复原图里不存在的独立发丝细节。

退出 Play，在 Hierarchy 选中 LZW 角色根物体，点击 Tools > Cly > LZW > Update Hair on Selected Character，确认后手动保存场景。修改支持 Ctrl+Z。已更新原有的两个头发 Prefab，保留资产 GUID；旧 LOD5 FBX 和旧模型备份仍存在。

材质 M_LZW_Hair_Backing 新增 Hair Surface Detail Strength，默认 0.4，建议在 0.2～0.7 内微调。调到 0 关闭额外表面法线。White Hair Amount 仍控制 Seed 白发选择，现有黑白比例及亮度设置保留。

新细化网格：Models/Hair_S_SlickBack_DetailedBacking.asset，可在 Inspector 中查看。重新生成网格使用 Tools > Cly > LZW > Rebuild Detailed Hair Mesh；生成全部头发资源使用 Upgrade Hair Density，之后 Enable Strand White Hair Control 恢复 Seed 数据贴图的导入设置。

验证：独立 Unity 6000.0.55f1 / URP17，前/侧/背视角、正常与较暗光照下的新旧对比、模型贴合、Shader 编译无错误或警告。未验证 VR 真机性能。未修改用户场景文件。旧版备份：E:/Unity/lzw-hair/Backup_BeforeDetailedBacking_20261110。

---

# Seed 白发比例控制（当前用法）

白发控制现已改为原始 Groom Seed 选色。退出 Play，在 Materials 同时选中 M_LZW_Hair_SaltPepper 和 M_LZW_Hair_Backing，调 White Hair Amount：0 选择黑发，1 选择白发，默认 0.85。建议比较 0.5、0.8、0.95。Use Strand Seed Colors 应勾选。旧 White Hair Boost 已隐藏并停用。

黑发和白发各自维持固定颜色/亮度，滑块改变 Seed 被选中为白色的区域；保留根部、深度和原图的明暗变化。White Hair Brightness 和 Black Hair Brightness 分别控制两类颜色亮度，先不要改。补密底层色差特意比外层小，减少块状斑纹。

这是一种按覆盖面积校准的图集 Seed 近似，不能理解为精确根数百分比。发片图集最大一组相同 Seed 约占覆盖面积 25.7%，因此部分发丝会成组切换；图集重叠/量化及 mipmap 也会限制细节。补密外壳的 Seed 不等于独立几何发丝。

新增 Pigment 图是线性数据，保留未压缩导入。若以后重建发量资源，再点击 Tools > Cly > LZW > Enable Strand White Hair Control 恢复该导入设置；不会重置已有比例。

独立 Unity 6000.0.55f1 / URP17 已验证 0、0.5、0.8、0.95、1 的渲染和 Shader 编译；未验证 VR 真机。备份在 E:/Unity/lzw-hair/Backup_BeforeSeedControl_20261110。关闭 Use Strand Seed Colors 可回到原始烘焙图颜色。以下较早版本的 White Hair Boost 说明已被本节替代。

---

# 可手动调整白发程度

在 Project 中打开本目录的 Materials，Ctrl 同时选中 M_LZW_Hair_SaltPepper 和 M_LZW_Hair_Backing，调整 Inspector 中 White Hair Boost。默认 0.3；0 恢复原图效果，0.5 较花白，1 深色发丝充分转为银灰。数值代表增强强度，作用于已有发丝颜色，不是精确白发根数百分比。可分别调两个材质，底层建议保留一些深色以增强层次。

White Hair Brightness 控制目标银白亮度（外层默认 0.48，底层 0.18），White Hair Tint 控制银白偏色。先只调整 Boost；不要通过整体 Tint 提亮。退出 Play 后修改资产可保留设置；所有使用这两个材质的角色会一起更新。不需要重建头发或重新导出模型。

原始贴图、透明遮罩、发量和骨骼保持现有状态。Shader 已在 Unity 6000.0.55f1 / URP17 编译并渲染检查 Boost 0、0.3、0.7、1；旧效果可通过两个材质的 Boost=0 恢复。

---

# 2026-11-09 发量加密更新

已更新原有两个 Prefab。采用原始 Groom LOD0 发片（37,276 三角面）加 LOD5 体积底层（701 三角面），总计 37,977 三角面。底层沿法线向内收 4 mm；花白外层保持原始 UV 和发丝方向。没有使用 AI 生成模型。

场景角色如仍显示旧头发：退出 Play，选中角色根物体，点击 `Tools > Cly > LZW > Update Hair on Selected Character`。操作支持 Ctrl+Z；只替换 head 下的 LZW_Hair，不修改身体、皮肤、动画。确认效果后手动保存场景。

重新生成加密预制体使用 `Tools > Cly > LZW > Upgrade Hair Density`。下面旧版说明中的三角面数和 cutoff 仅代表初版。本版外层 cutoff=0.18，底层 cutoff=0.12。

推荐在实际使用的 URP Asset 中设置 MSAA=4x，并确保相机启用 Allow MSAA。项目现有 PC/Mobile 管线均未开启 MSAA，本次未修改全局渲染配置。不开 MSAA 时，远距离细发仍可能出现锯齿和亮色条纹。

独立 Windows Unity 6000.0.55f1 / URP17 验证工程完成正、侧、背面 4xMSAA，以及中距离无 MSAA 渲染；Shader 无错误或警告。未进行 VR 真机性能测试。近看底层仍可能有块面感，与 UE Groom 发丝渲染存在差异。

旧版头发备份：`E:/Unity/lzw-hair/Backup_BeforeDensity_20261109`。

---

# LZW 花白面片头发（Unity URP）

## 使用

Unity 文件夹：`Assets/Cly/Character/LZW/Hair_URP`。

- 直接预览完整人物：将 `Prefabs/LZW_URP_WithHair.prefab` 拖到场景。
- 给已有动画角色加头发：在 Hierarchy 选中 LZW 角色最外层，选择菜单 `Tools > Cly > LZW > Add Hair to Selected Character`。头发挂到 `head`，保留角色现有动画配置。此操作支持 Undo，不自动保存场景。
- 单独头发：`Prefabs/LZW_Hair_HeadAttachment.prefab` 是按这套角色的 `head` 骨骼配置的附件，优先使用上述菜单，不要手动猜位置和缩放。

原来的 `Materials_URP/Prefabs/LZW_URP.prefab` 和原有皮肤材质不作修改。

## 材质

`Materials/M_LZW_Hair_SaltPepper.mat` 使用 `Cly/LZW/Hair Cards URP` Shader。

- `Tint`：总体颜色，默认白色，保持贴图中的花白发色。
- `Alpha Cutoff`：越低发丝越密、边缘越柔；越高发丝越稀。默认 0.28。
- `Roughness`：越高高光越宽、越哑光。默认 0.4。
- `Strand Highlight Strength`：发丝方向高光强度。
- `Back Light`：背光透亮近似强度。

Shader 为双面透明裁剪，包含投影、深度及深度法线 Pass；支持 URP Forward/Forward+ 的主光和附加光、Unity 立体实例宏。不是透明排序的 Alpha Blend 材质。建议 VR 使用 URP 4x MSAA，让 Alpha-to-Coverage 改善发丝边缘。本次只验证 Windows Editor，未测试 VR 真机。

`LZW_Hair_FlowRoot.png` 存储发丝方向，不是表面法线图。必须作为线性普通贴图使用，不要设置为 Normal Map 或开启绿色通道翻转。

## 来源和处理方法

源 Groom：`/Game/MetaHumans/LZW/Grooms/Hair_S_SlickBack`。

采用原始 `Hair_S_SlickBack_CardsMesh_Group0_LOD1`，约 21,316 三角面。LOD0 也提供为备用模型，没有自动加入实时 LODGroup。

保留现有 UV；按 UE 5.8 的 Card Compact（Layout2）布局解码：Attribute 的 R 为 Coverage、G 为 Depth、B 为 Seed；Tangent 的 RGB 为发丝方向、A 为 Strand U。由原始遮罩生成花白颜色加透明度图，保留发束形状和走向，并处理透明像素颜色扩展、mipmap 覆盖率，避免黑边。

源材质 `WhiteAmount` 约 0.899，`hairMelanin` 约 0.679。烘焙根据这些设置与现有 Seed 图制作花白近似；压缩图集的 Seed 分布与源 Groom 不同，不能把材质中的白发参数直接当作最终白色像素占比。真实束状 Groom 的多重散射、动态细发和面片表现也会不同，不是逐像素复刻 UE 渲染。

评估过 GitHub 项目 https://github.com/Ducklexander/metahuman-to-vrchat 的 `tools/textures/hair_atlas.py`。它重画图集并要求重排 UV，而本工程已有可用原始图集，因此没有运行或复制它的生成算法，也不需要改这套头发 UV。`Source` 中记录源参数与图集处理报告。

## UE 中的独立面片材质

目录：`Content/MetaHumans/Blender_assemble/HairExport`。

- `M_LZW_HairCards_Standalone`：Masked、Two Sided、Hair 着色模型，接入花白颜色、透明遮罩和发丝方向。
- `SM_LZW_HairCards_LOD0`、`SM_LZW_HairCards_LOD1`：已经赋材质的面片副本，双击可查看。原始 Groom、原始网格和原始角色发色材质不变。

UE 版检查了材质节点接线并保存成功；Unity 版在独立 Unity 6000.0.55f1 / URP 17.0.4 工程完成 Shader 编译和正面、侧面、背面渲染检查。UE 版未在当前编辑器视口进行目视确认。跨引擎灯光、曝光和色调映射仍需按实际场景微调。

头发作为刚性头部附件，不包含 Groom 模拟或独立发丝物理。
