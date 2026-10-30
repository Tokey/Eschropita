using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eschropita.Overhaul
{
    /// <summary>
    /// Marks a MonoBehaviour as an overhaul module. <see cref="OverhaulBootstrap"/> adds one instance of every
    /// marked type to the "Overhaul (runtime)" object when a game scene loads, in ascending <see cref="Order"/>.
    /// A module that already exists anywhere in the scene (placed by hand) is not added again.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class OverhaulModuleAttribute : Attribute
    {
        public int Order { get; }
        public OverhaulModuleAttribute(int order = 0) { Order = order; }
    }

    /// <summary>
    /// Installs the overhaul layers (signals, audio, VFX, atmosphere, story extras) into any scene that looks like
    /// the game (it has a <see cref="PlayerController"/>). Disable with the command-line flag <c>-noOverhaul</c>
    /// or by setting <see cref="Enabled"/> to false before the scene loads (useful for before/after captures).
    /// </summary>
    public static class OverhaulBootstrap
    {
        public static bool Enabled = true;
        public static GameObject Root { get; private set; }

        static List<Type> _moduleTypes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Environment.GetCommandLineArgs().Contains("-noOverhaul")) Enabled = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            // Listeners unsubscribe themselves in OnDisable/OnDestroy; GameSignals are not cleared here because
            // sceneLoaded fires after the new scene's OnEnable (that would drop baked objects' fresh subscriptions).
            if (mode == LoadSceneMode.Single) OverhaulHooks.Clear();
            Install();
        }

        public static void Install()
        {
            if (!Enabled) return;
            if (Root != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<PlayerController>() == null) return;

            Root = new GameObject("Overhaul (runtime)");
            foreach (var t in ModuleTypes())
            {
                if (UnityEngine.Object.FindAnyObjectByType(t) != null) continue;
                try { Root.AddComponent(t); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static IEnumerable<Type> ModuleTypes()
        {
            if (_moduleTypes != null) return _moduleTypes;
            var list = new List<(int order, string name, Type type)>();
            Assembly asm = typeof(OverhaulBootstrap).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                var a = t.GetCustomAttribute<OverhaulModuleAttribute>();
                if (a != null) list.Add((a.Order, t.FullName, t));
            }
            _moduleTypes = list.OrderBy(x => x.order).ThenBy(x => x.name, StringComparer.Ordinal).Select(x => x.type).ToList();
            return _moduleTypes;
        }

        /// <summary>Finds a module instance (or null) without a scene search when the bootstrap created it.</summary>
        public static T Get<T>() where T : Component
        {
            if (Root != null)
            {
                var c = Root.GetComponent<T>();
                if (c != null) return c;
            }
            return UnityEngine.Object.FindAnyObjectByType<T>();
        }
    }
}
