using System;
using HutongGames.PlayMaker;
using Satchel.BetterMenus;

namespace Modding
{
    public interface IMod
    {
        string GetVersion();
    }

    public abstract class Mod : IMod
    {
        protected Mod(string name) { }

        public virtual string GetVersion() => "0.0.0";
        public virtual void Initialize() { }
    }

    public interface ICustomMenuMod
    {
        bool ToggleButtonInsideMenu { get; }
        MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates);
    }

    public interface IGlobalSettings<T>
    {
        void OnLoadGlobal(T settings);
        T OnSaveGlobal();
    }

    public struct ModToggleDelegates
    {
    }

    public static class Logger
    {
        public static void Log(string message) { }
        public static void LogWarn(string message) { }
        public static void LogError(string message) { }
    }

    public static class ModHooks
    {
#pragma warning disable 0067
        public static event Func<Fsm, HitInstance, HitInstance> HitInstanceHook;
        public static event Func<int, int, int> AfterTakeDamageHook;
        public static event Action BeforePlayerDeadHook;
        public static event Action ApplicationQuitHook;
        public static event Action<SaveGameData> AfterSavegameLoadHook;
#pragma warning restore 0067

        public static string ModVersion => "Unavailable";

        public static object GetMod(string modName) => null;
    }
}
