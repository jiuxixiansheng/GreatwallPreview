# 长城导览动画系统最小实现

## 1. 角色 Animator 设置

在同一个 Animator 上建立两个 Layer：

1. `Body`（第 0 层）：身体动作状态，例如 `StandUp_PatWall`、`Lookout`、`PointAndExplain`。
2. `Face`（第 1 层）：设置 Avatar Mask，只勾选头部和面部骨骼；默认状态 `Face_Idle`，另一个状态 `Face_Talk`。

`Face` 层的过渡：

```text
Face_Idle  -- Face_Talking == true --> Face_Talk
Face_Talk  -- Face_Talking == false --> Face_Idle
```

建议 `Face_Talk` 使用循环的嘴型/下颌动作，过渡时间约 0.05~0.1 秒，Layer 权重设为 1。

## 2. 场景挂载

把以下组件挂在角色根节点：

- `Animator`
- `AudioSource`（用于讲解语音）
- `FacialSpeechController`
- `AnimationSequencePlayer`

在 `AnimationSequencePlayer` 中把 `bodyAnimator` 和 `facialSpeech` 拖进去。

## 3. 创建这段剧情

在 Project 面板右键：`Create > Greatwall VR > Animation Sequence`，建立一个 Sequence，添加三步：

```text
1. bodyStateName = StandUp_PatWall
   voice = （拍墙这句语音，可为空）
   advanceMode = BodyAnimation

2. bodyStateName = Lookout
   voice = 望孔讲解语音
   advanceMode = BodyAndVoice

3. bodyStateName = PointAndExplain
   voice = 古砖窑遗址讲解语音
   advanceMode = BodyAndVoice
```

把 `AnimationSequencePlayer.Play(sequence)` 绑定到一个按钮、触发器或导览点事件即可开始播放。

`onSubtitleChanged` 会在每一步开始时发送字幕文本，在步骤结束时发送空字符串。可以把它绑定到自己的字幕 UI。

## 4. 面部说话是怎么触发的

每个步骤开始时：

```text
AnimationSequencePlayer
    -> FacialSpeechController.PlaySpeech(voice)
    -> AudioSource 播放语音
    -> Animator.SetBool("Face_Talking", true)
    -> Face 层进入 Face_Talk
```

语音结束或步骤被跳过时：

```text
FacialSpeechController.StopSpeech()
    -> AudioSource.Stop()
    -> Animator.SetBool("Face_Talking", false)
    -> Face 层回到 Face_Idle
```

后续如果需要更真实的口型，可以保留这套触发接口，把 `Face_Talk` 换成 viseme 播放器，不需要修改剧情播放器。
