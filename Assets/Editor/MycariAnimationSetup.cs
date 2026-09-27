using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MycariAnimationSetup
{
    const string Root = "Assets/Mycari";
    const string MovingFolder = Root + "/Moving";
    const string AttackFolder = Root + "/Attack";
    const string IdlePath = Root + "/MycariIdle.anim";
    const string ControllerPath = Root + "/MycariController.controller";
    const string PrefabPath = Root + "/Mycari.prefab";
    const float FrameRate = 8f;

    [MenuItem("Eschropita/Mycari/Rebuild Animations")]
    public static void Rebuild()
    {
        var moving = BuildClip("MycariMoving", LoadSprites(MovingFolder), true);
        var attack = BuildClip("MycariAttack", LoadSprites(AttackFolder), false);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdlePath);
        if (idle == null || moving == null || attack == null)
        {
            Debug.LogError("[MycariAnimationSetup] Could not load all Mycari animation frames.");
            return;
        }

        var controller = BuildController(idle, moving, attack);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            if (root.GetComponent<MycariAnimator>() == null) root.AddComponent<MycariAnimator>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MycariAnimationSetup] Rebuilt Mycari idle, moving, and attack animations.");
    }

    static List<Sprite> LoadSprites(string folder)
    {
        return AssetDatabase.FindAssets("t:Sprite", new[] { folder })
            .Select(guid => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(sprite => sprite != null)
            .OrderBy(sprite => sprite.name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static AnimationClip BuildClip(string name, List<Sprite> frames, bool loop)
    {
        if (frames.Count == 0) return null;

        string path = Root + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.frameRate = FrameRate;
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        var keys = new List<ObjectReferenceKeyframe>(frames.Count + 1);
        float time = 0f;
        for (int i = 0; i < frames.Count; i++)
        {
            keys.Add(new ObjectReferenceKeyframe { time = time, value = frames[i] });
            time += 1f / FrameRate;
        }

        // Hold the final pose until the loop or non-loop clip ends.
        keys.Add(new ObjectReferenceKeyframe { time = time, value = frames[frames.Count - 1] });
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorController BuildController(AnimationClip idle, AnimationClip moving, AnimationClip attack)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        foreach (var state in sm.states) sm.RemoveState(state.state);
        foreach (var parameter in controller.parameters.ToArray()) controller.RemoveParameter(parameter);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

        var idleState = sm.AddState("Idle");
        idleState.motion = idle;
        var movingState = sm.AddState("Moving");
        movingState.motion = moving;
        var attackState = sm.AddState("Attack");
        attackState.motion = attack;
        sm.defaultState = idleState;

        var toMoving = idleState.AddTransition(movingState);
        toMoving.hasExitTime = false;
        toMoving.duration = 0f;
        toMoving.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");

        var toIdle = movingState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");

        var toAttack = sm.AddAnyStateTransition(attackState);
        toAttack.hasExitTime = false;
        toAttack.duration = 0f;
        toAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

        var attackToIdle = attackState.AddTransition(idleState);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = 1f;
        attackToIdle.duration = 0f;

        EditorUtility.SetDirty(controller);
        return controller;
    }
}