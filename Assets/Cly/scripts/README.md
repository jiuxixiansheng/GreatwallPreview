# 小龙动画系统脚本说明

## 当前正式使用

- `Character/`：角色自身效果脚本。
- `Navigation/`：NavMesh 移动、移动动画同步、视觉转向。
- `Navigation/Timeline/`：Timeline 的移动和转向轨道。
- `Timeline/`：身体、面部、章节切换与 Timeline 完成事件。

## Backup

`Assets/Cly/Backup` 保存旧 Sequence 测试方案和调试脚本。
新剧情不要再使用这些脚本；现有 Prefab 中的旧组件保持禁用，仅用于避免旧引用立即变成 Missing Script。

