# 序章 timeline_00

最新状态：timeline_00 已加入画外音1两遍，随后播放画外音2，总长123.456秒；序章环境循环片段延长至配音结束。章节0启用 Timeline，关闭播完自动切章，继续等待影壁交互，右手A仍可跳过。

影壁呼吸发光与配音配置见 `Assets/sza/scripts/Prologue/README.md`。影壁选择仍由InteractionAudioSystem播放发光、光幕音效并走原有立即切章流程，详见 `Assets/Xcy/Scripts/Audio/README.md`。

以下60秒环境和35/45秒音效时点是历史粗排记录；35秒和45秒音效片段已移除，不再随时间自动播放。

场景对象：part0/timeline_00
资源：Assets/Xcy/Timeline/timeline_00.playable
音源预制体：Assets/Xcy/Prefabs/Audio/Part0_SFX_Rig.prefab

StoryManager 的章节0现在启用 Timeline 并绑定上述 Director，继续使用已有 part0 场景根和隐藏龙神的配置。仍然关闭“播完自动切章”，保留右手A键手动切换下一章；Director 不使用 Play On Awake，由剧情管理器或联机Host统一开始播放。

旧版在没有旁白音轨时按60秒粗排音效：

- 0–60秒：序章环境，音量0.65，淡入1秒、淡出2秒。
- 35–40.749秒：麒麟纹影壁发出金光，音量0.45。
- 45–54.221秒：影壁金光形成光幕，音量0.45。

后两项为估时，只添加音频，不增加影壁视觉、触摸交互或自动切章事件。60秒音效播完后仍等待手动切章；若更早按A，会按原有转场流程暂停并结束序章。环境引用现有 Assets/Xcy/Audio/Part1/序章环境.wav，避免重复导入。

已验证本机Host会自动播放timeline_00，随后通过原章节请求入口切到Part1，序章Director停止并随part0隐藏；未验证多机联机及完整头显试听。

石头脚步1–4已在timeline_02_2的“SFX 04 罗哲文来访脚步”中启用，依次开始于90.80、91.58、92.36、93.14秒，最后结束于93.942秒；它们不是Part1龙神脚步。

操作前场景快照：Temp/Part0Timeline/before_timeline00.unity（Temp可能被清理）。
