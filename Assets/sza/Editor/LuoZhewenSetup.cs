using System;
using System.Collections.Generic;
using Greatwall.Navigation;
using Greatwall.Navigation.Timeline;
using Greatwall.VRAnimation.Timeline;
using Sza.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace Sza.Editor
{
    public static class LuoZhewenSetup
    {
        public const string Folder = "Assets/sza/Characters/LuoZhewen";
        public const string PrefabPath = Folder + "/LuoZhewen.prefab";
        private const string SourcePrefab = "Assets/Cly/Character/LZW/Hair_URP/Prefabs/LZW_URP_WithHair.prefab";
        private const string TalkPath = "Assets/Cly/Animations/lzw/LZW_Assembled_Talk.FBX";
        private const string WalkPath = "Assets/Cly/Animations/lzw/LZW_walkassemble.FBX";
        private const string FacePath = "Assets/Cly/Animations/lzw/AS_LZW_Face_all_assemble.FBX";
        private const string GroupName = "罗哲文 · 动画与导航";

        [MenuItem("Tools/Sza/罗哲文/创建角色并接入 part2-2")]
        public static void Configure()
        {
            if (Application.isPlaying || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请先退出运行和 Timeline 预览。");
            EnsureFolder(Folder);
            var prefab = BuildPrefab();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/szaScene_Final.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/szaScene_Final.unity", OpenSceneMode.Additive);
            try { ConfigureScene(scene, prefab); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static GameObject BuildPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            EnsureFolder(Folder + "/Animation"); EnsureFolder(Folder + "/Masks");
            MakeLooping(TalkPath); MakeLooping(WalkPath);
            var talk = LoadClip(TalkPath); var walk = LoadClip(WalkPath); var face = LoadClip(FacePath);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("LuoZhewen_root");
                SceneManager.MoveGameObjectToScene(root, preview);
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
                if (source == null) throw new InvalidOperationException("缺少带头发的罗哲文预制体。");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                model.transform.SetParent(visual.transform, false);
                model.transform.localRotation = Quaternion.identity;
                var animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;

                var defaults = CaptureDefaults(model, talk, face);
                var idle = MakeNeutralClip(model, talk, defaults, false, "LZW_Idle");
                var faceIdle = MakeNeutralClip(model, face, defaults, true, "LZW_FaceIdle");
                talk.SampleAnimation(model, 0f);
                var head = FindBone(model.transform, "head");
                var nose = FindBone(model.transform, "FACIAL_C_NoseTip");
                var left = FindBone(model.transform, "thigh_l");
                var right = FindBone(model.transform, "thigh_r");
                Vector3 forward = head != null && nose != null ? Vector3.ProjectOnPlane(nose.position - head.position, Vector3.up) : Vector3.forward;
                if (left != null && right != null)
                {
                    var bodyForward = Vector3.Cross(right.position - left.position, Vector3.up);
                    if (Vector3.Dot(bodyForward, forward) < 0f) bodyForward = -bodyForward;
                    if (bodyForward.sqrMagnitude > 0.00001f) forward = bodyForward;
                }
                if (forward.sqrMagnitude > 0.00001f) model.transform.localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward);
                idle.SampleAnimation(model, 0f); faceIdle.SampleAnimation(model, 0f);
                Bounds standing = BakedBounds(model);
                visual.transform.localPosition = Vector3.up * -standing.min.y;
                float height = standing.size.y;
                if (height < 1f || height > 2.5f) throw new InvalidOperationException("角色站姿或缩放异常，身高=" + height);
                float referenceSpeed = EstimateWalkSpeed(model, walk);
                idle.SampleAnimation(model, 0f); faceIdle.SampleAnimation(model, 0f);
                var upperMask = MakeMask(model.transform, false);
                var faceMask = MakeMask(model.transform, true);
                var controller = MakeController(idle, talk, walk, faceIdle, face, upperMask, faceMask);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = new Mesh(); skin.BakeMesh(mesh);
                    var bounds = mesh.bounds; bounds.Expand(new Vector3(0.7f, 0.4f, 0.7f));
                    skin.localBounds = bounds; UnityEngine.Object.DestroyImmediate(mesh);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

                var agent = root.AddComponent<NavMeshAgent>(); agent.enabled = false;
                agent.agentTypeID = NavigationAgentType();
                agent.height = height; agent.radius = 0.23f; agent.baseOffset = 0f;
                agent.speed = referenceSpeed; agent.angularSpeed = 180f; agent.acceleration = 4f;
                agent.stoppingDistance = 0.03f;
                var mover = root.AddComponent<DragonNavMeshMover>();
                SetReference(mover, "agent", agent); SetBool(mover, "agentControlsRotation", true);
                var turn = root.AddComponent<DragonTurnController>(); SetReference(turn, "rotationRoot", visual.transform);
                var bridge = model.AddComponent<CharacterTimelineAnimator>();
                var bridgeData = new SerializedObject(bridge);
                bridgeData.FindProperty("animator").objectReferenceValue = animator;
                bridgeData.FindProperty("bodyLayerControlMode").enumValueIndex = (int)BodyLayerControlMode.SingleBodyLayer;
                bridgeData.FindProperty("bodyActionLayerIndex").intValue = 0;
                bridgeData.FindProperty("upperBodyLayerIndex").intValue = 2;
                bridgeData.FindProperty("faceLayerIndex").intValue = 3;
                bridgeData.FindProperty("singleLayerIdleStatePath").stringValue = "BaseLayer.Idle";
                bridgeData.FindProperty("resetBodyActionLayerOnAwake").boolValue = false;
                bridgeData.ApplyModifiedPropertiesWithoutUndo();
                var voiceObject = new GameObject("Voice"); voiceObject.transform.SetParent(root.transform, false);
                voiceObject.transform.localPosition = Vector3.up * height * 0.88f;
                var voice = voiceObject.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.spatialBlend = 1f; voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Linear; voice.minDistance = 2f; voice.maxDistance = 20f;
                var actor = root.AddComponent<TimelineCharacterActor>();
                var locomotion = root.AddComponent<DistanceDrivenLocomotion>();
                actor.agent = agent; actor.mover = mover; actor.animator = animator; actor.timelineAnimator = bridge;
                actor.visualRoot = visual.transform; actor.voice = voice; actor.locomotion = locomotion;
                locomotion.actor = actor; locomotion.animator = animator; locomotion.referenceMovementSpeed = referenceSpeed;
                var result = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (result == null) throw new InvalidOperationException("罗哲文预制体保存失败。");
                Debug.Log("[罗哲文] 预制体已创建，身高=" + height + "m，参考步速=" + referenceSpeed + "m/s。");
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static bool Facial(EditorCurveBinding b) => b.type == typeof(SkinnedMeshRenderer) || b.path.Contains("/FACIAL");
        private static string GroupKey(EditorCurveBinding b)
        {
            string property = b.propertyName;
            if (b.type == typeof(Transform)) property = property.Substring(0, property.LastIndexOf('.'));
            return b.path + "|" + b.type.FullName + "|" + property;
        }

        private static Dictionary<EditorCurveBinding, float> CaptureDefaults(GameObject model, params AnimationClip[] clips)
        {
            var result = new Dictionary<EditorCurveBinding, float>();
            foreach (var clip in clips) foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (result.ContainsKey(binding)) continue;
                if (!AnimationUtility.GetFloatValue(model, binding, out float value))
                    throw new InvalidOperationException("动画路径未对应模型：" + binding.path + " / " + binding.propertyName);
                result.Add(binding, value);
            }
            return result;
        }

        private static AnimationClip MakeNeutralClip(GameObject model, AnimationClip source,
            Dictionary<EditorCurveBinding, float> defaults, bool facial, string name)
        {
            string path = Folder + "/Animation/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); if (existing != null) return existing;
            var bindings = AnimationUtility.GetCurveBindings(source);
            var groups = new HashSet<string>();
            foreach (var b in bindings)
            {
                if (Facial(b) != facial) continue;
                var curve = AnimationUtility.GetEditorCurve(source, b);
                float value = defaults[b]; bool needed = false;
                if (facial) { foreach (var k in curve.keys) if (Mathf.Abs(k.value - value) > 0.00001f) { needed = true; break; } }
                else needed = Mathf.Abs(curve.Evaluate(0f) - value) > 0.000001f;
                if (needed) groups.Add(GroupKey(b));
            }
            var clip = new AnimationClip { name = name, frameRate = 30f };
            foreach (var b in bindings) if (Facial(b) == facial && groups.Contains(GroupKey(b)))
            {
                float value = facial ? defaults[b] : AnimationUtility.GetEditorCurve(source, b).Evaluate(0f);
                AnimationUtility.SetEditorCurve(clip, b, AnimationCurve.Constant(0f, 1f, value));
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static AvatarMask MakeMask(Transform model, bool facial)
        {
            string path = Folder + "/Masks/" + (facial ? "LZW_Face.mask" : "LZW_Upper.mask");
            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(path); if (existing != null) return existing;
            var mask = new AvatarMask { name = facial ? "LZW_Face" : "LZW_Upper" };
            var transforms = model.GetComponentsInChildren<Transform>(true); mask.transformCount = transforms.Length;
            for (int i = 0; i < transforms.Length; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(transforms[i], model);
                bool active = p.Length == 0 || (facial ? p.Contains("/FACIAL") || transforms[i].GetComponent<SkinnedMeshRenderer>() != null
                    : p.Contains("/spine_01") && !p.Contains("/FACIAL"));
                mask.SetTransformPath(i, p); mask.SetTransformActive(i, active);
            }
            AssetDatabase.CreateAsset(mask, path); return mask;
        }

        private static AnimatorController MakeController(AnimationClip idle, AnimationClip talk, AnimationClip walk,
            AnimationClip faceIdle, AnimationClip speech, AvatarMask upper, AvatarMask face)
        {
            string path = Folder + "/AC_LuoZhewen.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path); if (existing != null) return existing;
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            c.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
            c.AddParameter("MoveAnimationSpeed", AnimatorControllerParameterType.Float);
            c.layers[0].stateMachine.name = "BaseLayer";
            var layers = c.layers; layers[0].name = "BaseLayer"; c.layers = layers;
            var machine = c.layers[0].stateMachine;
            var idleState = State(machine, "Idle", idle, 260, 0);
            var talkState = State(machine, "Talk", talk, 260, 100);
            var walkState = State(machine, "Walk", walk, 560, 0);
            walkState.speedParameter = "MoveAnimationSpeed"; walkState.speedParameterActive = true;
            machine.defaultState = idleState;
            Transition(idleState, walkState, true); Transition(talkState, walkState, true); Transition(walkState, idleState, false);

            c.AddLayer("WalkUpperLayer"); layers = c.layers;
            layers[1].avatarMask = upper; layers[1].defaultWeight = 1f; c.layers = layers;
            machine = c.layers[1].stateMachine;
            var empty = State(machine, "Empty", null, 260, 0); empty.writeDefaultValues = false;
            var upperWalk = State(machine, "Walk", walk, 560, 0); machine.defaultState = empty;
            Transition(empty, upperWalk, true); Transition(upperWalk, empty, false);

            c.AddLayer("UpperLayer"); layers = c.layers;
            layers[2].avatarMask = upper; layers[2].defaultWeight = 0f; c.layers = layers;
            machine = c.layers[2].stateMachine;
            empty = State(machine, "Empty", null, 260, 0); empty.writeDefaultValues = false; machine.defaultState = empty;
            State(machine, "Talk", talk, 560, 0); State(machine, "Walk", walk, 560, 100);

            c.AddLayer("FaceLayer"); layers = c.layers;
            layers[3].avatarMask = face; layers[3].defaultWeight = 1f; c.layers = layers;
            machine = c.layers[3].stateMachine;
            var neutral = State(machine, "Idle", faceIdle, 260, 0);
            var speaking = State(machine, "Speech", speech, 560, 0); machine.defaultState = neutral;
            var end = speaking.AddTransition(neutral); end.hasExitTime = true; end.exitTime = 1f;
            end.hasFixedDuration = true; end.duration = 0.08f;
            EditorUtility.SetDirty(c); AssetDatabase.SaveAssetIfDirty(c); return c;
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, float x, float y)
        { var state = machine.AddState(name, new Vector3(x, y, 0)); state.motion = motion; state.speed = 1f; state.writeDefaultValues = true; return state; }

        private static void Transition(AnimatorState from, AnimatorState to, bool moving)
        {
            var t = from.AddTransition(to); t.hasExitTime = false; t.hasFixedDuration = true; t.duration = 0.12f;
            t.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsMoving");
        }

        private static void ConfigureScene(Scene scene, GameObject prefab)
        {
            GameObject manager = null, chapterRoot = null; PlayableDirector director = null;
            TimelineCharacterActor actor = null; StoryTimelineManager story = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "NetworkManager") manager = root;
                if (root.name == "part2-2") chapterRoot = root;
                foreach (var t in root.GetComponentsInChildren<PlayableDirector>(true)) if (t.name == "timeline_02_2") director = t;
                foreach (var a in root.GetComponentsInChildren<TimelineCharacterActor>(true)) if (a.actorId == "luo_zhewen") actor = a;
                foreach (var s in root.GetComponentsInChildren<StoryTimelineManager>(true)) story = s;
            }
            if (manager == null || chapterRoot == null || director == null || story == null)
                throw new InvalidOperationException("Final 缺少章节或 NetworkManager。");
            var timeline = (TimelineAsset)director.playableAsset;
            double originalDuration = timeline.duration;
            GroupTrack group = null;
            foreach (var track in timeline.GetRootTracks()) if (track.name == GroupName) group = track as GroupTrack;
            if (actor == null)
            {
                actor = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<TimelineCharacterActor>();
                actor.gameObject.name = "LuoZhewen_root";
                Undo.RegisterCreatedObjectUndo(actor.gameObject, "Add Luo Zhewen");
            }
            var points = FindRoot(scene, "LuoZhewen_Points");
            if (points == null)
            {
                points = new GameObject("LuoZhewen_Points"); SceneManager.MoveGameObjectToScene(points, scene);
                var chapters = new SerializedObject(story).FindProperty("chapters");
                var dragonSpawn = (Transform)chapters.GetArrayElementAtIndex(3).FindPropertyRelative("characterSpawnPoint").objectReferenceValue;
                Vector3 target = dragonSpawn.position + new Vector3(1.1f, 0f, -1.2f);
                Vector3 start = target + Vector3.back * 1.9f;
                bool wasActive = chapterRoot.activeSelf;
                chapterRoot.SetActive(true);
                try
                {
                    var filter = new NavMeshQueryFilter { agentTypeID = actor.agent.agentTypeID, areaMask = NavMesh.AllAreas };
                    if (NavMesh.SamplePosition(target, out var hit, 1.5f, filter)) target = hit.position;
                    if (NavMesh.SamplePosition(start, out hit, 1.5f, filter)) start = hit.position;
                }
                finally { chapterRoot.SetActive(wasActive); }
                var spawn = new GameObject("SpawnPoint_LZW"); spawn.transform.SetParent(points.transform, false); spawn.transform.position = start;
                var destination = new GameObject("MovePoint_LZW_01"); destination.transform.SetParent(points.transform, false); destination.transform.position = target;
                if ((target - start).sqrMagnitude > 0.01f) spawn.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(target - start, Vector3.up));
                destination.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(dragonSpawn.position - target, Vector3.up));
            }
            actor.spawnPoint = points.transform.Find("SpawnPoint_LZW"); actor.director = director;
            if (group == null)
            {
                actor.transform.SetPositionAndRotation(actor.spawnPoint.position, actor.spawnPoint.rotation);
                actor.gameObject.SetActive(false);
                group = timeline.CreateTrack<GroupTrack>(null, GroupName);
                var visibility = timeline.CreateTrack<ActivationTrack>(group, "LZW · 显示");
                visibility.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
                var active = visibility.CreateDefaultClip(); active.start = 90.8d; active.duration = Math.Max(1d, originalDuration - active.start);
                director.SetGenericBinding(visibility, actor.gameObject);
                var body = timeline.CreateTrack<BodyActionTrack>(group, "LZW · 身体"); director.SetGenericBinding(body, actor.timelineAnimator);
                AddBody(body, LoadClip(WalkPath), "BaseLayer.Walk", 90.8d, 3.2d, "走向导航点");
                AddBody(body, LoadClip(TalkPath), "BaseLayer.Talk", 94d, LoadClip(FacePath).length, "站立说话 · 可调整");
                var upperTrack = timeline.CreateTrack<UpperBodyActionTrack>(group, "LZW · 独立上半身");
                director.SetGenericBinding(upperTrack, actor.timelineAnimator);
                var upperClip = upperTrack.CreateClip<UpperBodyActionClip>(); var upperAsset = (UpperBodyActionClip)upperClip.asset;
                upperAsset.referenceAnimationClip = LoadClip(TalkPath); upperAsset.statePath = "UpperLayer.Talk";
                upperClip.start = 94d; upperClip.duration = LoadClip(FacePath).length; upperClip.displayName = "说话手势 · 固定1倍速";
                var faceTrack = timeline.CreateTrack<TimelineFaceTrack>(group, "LZW · 嘴型表情"); director.SetGenericBinding(faceTrack, actor);
                var faceClip = faceTrack.CreateClip<TimelineFaceClip>(); var faceAsset = (TimelineFaceClip)faceClip.asset;
                faceAsset.referenceAnimationClip = LoadClip(FacePath); faceAsset.statePath = "FaceLayer.Speech"; faceAsset.crossFadeSeconds = 0.08f;
                faceClip.start = 94d; faceClip.duration = faceAsset.referenceAnimationClip.length; faceClip.displayName = "罗哲文嘴型 · 原速待精调";
                var move = timeline.CreateTrack<NavMeshMoveTrack>(group, "LZW · 导航位移"); director.SetGenericBinding(move, actor.mover);
                var moveClip = move.CreateClip<NavMeshMoveClip>(); var moveAsset = (NavMeshMoveClip)moveClip.asset;
                moveAsset.destination.exposedName = new PropertyName("LuoZhewen_MovePoint_01");
                director.SetReferenceValue(moveAsset.destination.exposedName, points.transform.Find("MovePoint_LZW_01").gameObject);
                moveClip.start = 90.8d; moveClip.duration = 3.2d; moveClip.displayName = "走到 MovePoint_LZW_01";
                var turn = timeline.CreateTrack<DragonTurnTrack>(group, "LZW · 转向");
                director.SetGenericBinding(turn, actor.GetComponent<DragonTurnController>());
                var turnClip = turn.CreateClip<DragonTurnClip>();
                ((DragonTurnClip)turnClip.asset).turnAngleDegrees = Mathf.DeltaAngle(actor.spawnPoint.eulerAngles.y, points.transform.Find("MovePoint_LZW_01").eulerAngles.y);
                turnClip.start = 94d; turnClip.duration = 1d; turnClip.displayName = "转向小龙 · 可调整角度";
                var voice = timeline.CreateTrack<AudioTrack>(group, "LZW · 配音（自行排）"); director.SetGenericBinding(voice, actor.voice);
                actor.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor.gameObject);
            }
            var network = manager.GetComponent<NetworkTimelineActorCoordinator>();
            if (network == null) network = Undo.AddComponent<NetworkTimelineActorCoordinator>(manager);
            var registered = new List<TimelineCharacterActor>(network.actors);
            if (!registered.Contains(actor)) registered.Add(actor); network.actors = registered.ToArray();
            foreach (var o in new UnityEngine.Object[] { actor, director, timeline, network }) EditorUtility.SetDirty(o);
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
            AssetDatabase.SaveAssetIfDirty(timeline);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Final 保存失败。");
            Debug.Log("[罗哲文] 已接入 part2-2：出场和导航示例90.8–94秒，嘴型94秒原速开始；配音轨留空，由你排。");
        }

        private static void AddBody(BodyActionTrack track, AnimationClip animation, string path, double start, double duration, string label)
        {
            var clip = track.CreateClip<BodyActionClip>(); var asset = (BodyActionClip)clip.asset;
            asset.referenceAnimationClip = animation; asset.statePath = path; asset.layerFadeSeconds = 0.1f;
            clip.start = start; clip.duration = duration; clip.displayName = label;
        }

        private static GameObject FindRoot(Scene scene, string name)
        { foreach (var root in scene.GetRootGameObjects()) if (root.name == name) return root; return null; }

        private static Transform FindBone(Transform root, string name)
        { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }

        private static Bounds BakedBounds(GameObject model)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = new Mesh(); r.BakeMesh(mesh);
                foreach (var p in mesh.vertices)
                { var point = r.transform.TransformPoint(p); if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            return bounds;
        }

        private static float EstimateWalkSpeed(GameObject model, AnimationClip walk)
        {
            var left = FindBone(model.transform, "foot_l"); var right = FindBone(model.transform, "foot_r");
            if (left == null || right == null) return 0.6f;
            var a = new List<Vector3>(); var b = new List<Vector3>(); float lowA = float.MaxValue, lowB = float.MaxValue;
            for (int i = 0; i < 50; i++)
            {
                walk.SampleAnimation(model, i * 0.1f); a.Add(left.position); b.Add(right.position);
                lowA = Mathf.Min(lowA, left.position.y); lowB = Mathf.Min(lowB, right.position.y);
            }
            var speeds = new List<float>();
            for (int i = 1; i < a.Count; i++)
            {
                if (a[i].y < lowA + 0.035f) { float speed = -(a[i].z - a[i - 1].z) / 0.1f; if (speed > 0.08f) speeds.Add(speed); }
                if (b[i].y < lowB + 0.035f) { float speed = -(b[i].z - b[i - 1].z) / 0.1f; if (speed > 0.08f) speeds.Add(speed); }
            }
            if (speeds.Count < 4) return 0.6f;
            speeds.Sort(); return Mathf.Clamp(speeds[speeds.Count / 2], 0.15f, 2f);
        }

        private static AnimationClip LoadClip(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            throw new InvalidOperationException("缺少动画：" + path);
        }

        private static void MakeLooping(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("缺少动画FBX：" + path);
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool changed = false;
            foreach (var clip in clips) if (!clip.loopTime) { clip.loopTime = true; changed = true; }
            if (changed) { importer.clipAnimations = clips; importer.SaveAndReimport(); }
        }

        private static int NavigationAgentType()
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int id = NavMesh.GetSettingsByIndex(i).agentTypeID;
                if (NavMesh.GetSettingsNameFromID(id) == "Dragon") return id;
            }
            throw new InvalidOperationException("缺少当前关卡使用的 Dragon 导航类型，请先检查 Navigation 设置。");
        }

        private static void SetReference(UnityEngine.Object obj, string field, UnityEngine.Object value)
        { var data = new SerializedObject(obj); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetBool(UnityEngine.Object obj, string field, bool value)
        { var data = new SerializedObject(obj); data.FindProperty(field).boolValue = value; data.ApplyModifiedPropertiesWithoutUndo(); }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
