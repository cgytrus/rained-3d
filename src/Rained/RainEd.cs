using Raylib_cs;
using rlImGui_cs;
using System.Numerics;
using ImGuiNET;
using System.Text;
using System.Runtime.InteropServices;

namespace RainEd;

sealed class RainEd
{
    public static RainEd Instance = null!;

    public bool Running = true;
    private Level level;
    public readonly RlManaged.Texture2D LevelGraphicsTexture;
    private readonly RlManaged.Texture2D rainedLogo;
    public readonly Tiles.Database TileDatabase;
    public readonly EffectsDatabase EffectsDatabase;
    public readonly Light.LightBrushDatabase LightBrushDatabase;
    private readonly ChangeHistory.ChangeHistory changeHistory;
    private readonly EditorWindow editorWindow;
    private bool ShowDemoWindow = false;

    private string currentFilePath = string.Empty;

    public string CurrentFilePath { get => currentFilePath; }
    public Level Level { get => level; }
    public EditorWindow Window { get => editorWindow; }

    private string notification = "";
    private float notificationTime = 0f;
    private float notifFlash = 0f;

    private bool promptAbout = false;

    public ChangeHistory.ChangeHistory ChangeHistory { get => changeHistory; }

    private DrizzleRenderWindow? drizzleRenderWindow = null;
    private LevelResizeWindow? levelResizeWin = null;

    public LevelResizeWindow? LevelResizeWindow { get => levelResizeWin; }
    
    public RainEd(string levelPath = "") {
        if (Instance != null)
            throw new Exception("Attempt to create more than one RainEd instance");
        
        Instance = this;

        rainedLogo = RlManaged.Texture2D.Load("assets/rained-logo.png");
        TileDatabase = new Tiles.Database();
        EffectsDatabase = new EffectsDatabase();
        LightBrushDatabase = new Light.LightBrushDatabase();
        
        if (levelPath.Length > 0)
        {
            level = LevelSerialization.Load(levelPath);
        }
        else
        {
            level = Level.NewDefaultLevel();
        }

        LevelGraphicsTexture = RlManaged.Texture2D.Load("assets/level-graphics.png");
        changeHistory = new ChangeHistory.ChangeHistory();
        editorWindow = new EditorWindow();

        UpdateTitle();
        RegisterShortcuts();
    }

    public void ShowError(string msg)
    {
        notification = msg;
        notificationTime = 3f;
        notifFlash = 0f;
    }

    private void LoadLevel(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            editorWindow.UnloadView();

            try
            {
                level.LightMap.Dispose();
                level = LevelSerialization.Load(path);
                ReloadLevel();
                currentFilePath = path;
                UpdateTitle();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error loading file " + path);
                Console.WriteLine(e);
                ShowError("Could not load level");
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            editorWindow.ReloadLevel();
            editorWindow.LoadView();
        }
    }

    private void SaveLevel(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            editorWindow.FlushDirty();

            try
            {
                LevelSerialization.Save(path);
                currentFilePath = path;
                UpdateTitle();
                changeHistory.MarkUpToDate();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                ShowError("Could not write level file");
            }
            
            if (promptCallback is not null)
            {
                promptCallback();
            }
        }

        promptCallback = null;
    }

    public void ResizeLevel(int newWidth, int newHeight, int anchorX, int anchorY)
    {
        if (newWidth == level.Width && newHeight == level.Height) return;

        Window.FlushDirty();
        level.Resize(newWidth, newHeight, anchorX, anchorY);
        Window.ReloadLevel();
        changeHistory.Clear();

        Window.LevelRenderer.MarkNeedsRedraw(0);
        Window.LevelRenderer.MarkNeedsRedraw(1);
        Window.LevelRenderer.MarkNeedsRedraw(2);
    }

    private void ReloadLevel()
    {
        editorWindow.ReloadLevel();
        changeHistory.Clear();
        changeHistory.MarkUpToDate();
        Window.LevelRenderer.MarkNeedsRedraw(0);
        Window.LevelRenderer.MarkNeedsRedraw(1);
        Window.LevelRenderer.MarkNeedsRedraw(2);
    }

    private Action? promptCallback;
    private bool promptUnsavedChanges;
    private bool promptUnsavedChangesCancelable;

    private void PromptUnsavedChanges(Action callback, bool canCancel = true)
    {
        promptUnsavedChangesCancelable = canCancel;

        if (changeHistory.HasChanges || (!canCancel && string.IsNullOrEmpty(currentFilePath)))
        {
            promptUnsavedChanges = true;
            promptCallback = callback;
        }
        else
        {
            callback();
        }
    }

    private void UpdateTitle()
    {
        var levelName =
            string.IsNullOrEmpty(currentFilePath) ? "Untitled" :
            Path.GetFileNameWithoutExtension(currentFilePath);
        
        Raylib.SetWindowTitle($"Rained - {levelName}");
    }

#region Shortcuts
    private class KeyShortcut
    {
        public string Name;
        public string ShortcutString;
        public ImGuiKey Key;
        public ImGuiModFlags Mods;
        public bool IsActivated = false;
        public bool AllowRepeat = false;

        public KeyShortcut(string name, ImGuiKey key, ImGuiModFlags mods, bool allowRepeat = false)
        {
            // build shortcut string
            var str = new List<string>();

            if (mods.HasFlag(ImGuiModFlags.Ctrl))
                str.Add("Ctrl");
            
            if (mods.HasFlag(ImGuiModFlags.Shift))
                str.Add("Shift");
            
            if (mods.HasFlag(ImGuiModFlags.Alt))
                str.Add("Alt");
            
            if (mods.HasFlag(ImGuiModFlags.Super))
                str.Add("Super");
            
            str.Add(ImGui.GetKeyName(key));

            ShortcutString = string.Join('+', str);

            // initialize other properties
            Name = name;
            Key = key;
            Mods = mods;
            AllowRepeat = allowRepeat;
        }

        public bool IsKeyPressed()
            =>
                ImGui.IsKeyPressed(Key, AllowRepeat) &&
                (Mods.HasFlag(ImGuiModFlags.Ctrl) == ImGui.IsKeyDown(ImGuiKey.ModCtrl)) &&
                (Mods.HasFlag(ImGuiModFlags.Shift) == ImGui.IsKeyDown(ImGuiKey.ModShift)) &&
                (Mods.HasFlag(ImGuiModFlags.Alt) == ImGui.IsKeyDown(ImGuiKey.ModAlt)) &&
                (Mods.HasFlag(ImGuiModFlags.Super) == ImGui.IsKeyDown(ImGuiKey.ModSuper));
    }
    private readonly Dictionary<string, KeyShortcut> keyShortcuts = new();

    public void RegisterKeyShortcut(string name, ImGuiKey key, ImGuiModFlags mods, bool allowRepeat = false)
    {
        keyShortcuts.Add(name, new KeyShortcut(name, key, mods, allowRepeat));
    }

    public bool IsShortcutActivated(string id)
        => keyShortcuts[id].IsActivated;

    public void ImGuiMenuItemShortcut(string shortcutId, string name)
    {
        var shortcutData = keyShortcuts[shortcutId];
        if (ImGui.MenuItem(name, shortcutData.ShortcutString))
            shortcutData.IsActivated = true;
    }

    private void RegisterShortcuts()
    {
        RegisterKeyShortcut("NavUp", ImGuiKey.W, ImGuiModFlags.None, true);
        RegisterKeyShortcut("NavLeft", ImGuiKey.A, ImGuiModFlags.None, true);
        RegisterKeyShortcut("NavDown", ImGuiKey.S, ImGuiModFlags.None, true);
        RegisterKeyShortcut("NavRight", ImGuiKey.D, ImGuiModFlags.None, true);
        RegisterKeyShortcut("NewObject", ImGuiKey.N, ImGuiModFlags.None, true);

        RegisterKeyShortcut("New", ImGuiKey.N, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("Open", ImGuiKey.O, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("Save", ImGuiKey.S, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("SaveAs", ImGuiKey.S, ImGuiModFlags.Ctrl | ImGuiModFlags.Shift);

        RegisterKeyShortcut("Cut", ImGuiKey.X, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("Copy", ImGuiKey.C, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("Paste", ImGuiKey.V, ImGuiModFlags.Ctrl);
        RegisterKeyShortcut("Undo", ImGuiKey.Z, ImGuiModFlags.Ctrl);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RegisterKeyShortcut("Redo", ImGuiKey.Y, ImGuiModFlags.Ctrl);
        else
            RegisterKeyShortcut("Redo", ImGuiKey.Z, ImGuiModFlags.Ctrl | ImGuiModFlags.Shift);
    }

    private void HandleShortcuts()
    {
        // activate shortcuts on key press
        if (!ImGui.GetIO().WantTextInput)
        {
            foreach (var shortcut in keyShortcuts.Values)
            {
                if (shortcut.IsKeyPressed())
                    shortcut.IsActivated = true;
            }
        }

        if (IsShortcutActivated("New"))
        {
            PromptUnsavedChanges(() =>
            {
                editorWindow.UnloadView();
                level.LightMap.Dispose();
                level = Level.NewDefaultLevel();
                ReloadLevel();
                editorWindow.LoadView();

                currentFilePath = string.Empty;
                UpdateTitle();
            });
        }

        if (IsShortcutActivated("Open"))
        {
            PromptUnsavedChanges(() =>
                LevelBrowser.Open(LevelBrowser.OpenMode.Read, LoadLevel, currentFilePath)
            );
        }

        if (IsShortcutActivated("Save"))
        {
            if (string.IsNullOrEmpty(currentFilePath))
                LevelBrowser.Open(LevelBrowser.OpenMode.Write, SaveLevel, currentFilePath);
            else
                SaveLevel(currentFilePath);
        }

        if (IsShortcutActivated("SaveAs"))
        {
            LevelBrowser.Open(LevelBrowser.OpenMode.Write, SaveLevel, currentFilePath);
        }

        if (IsShortcutActivated("Undo"))
        {
            Undo();
        }

        if (IsShortcutActivated("Redo"))
        {
            Redo();
        }
    }
#endregion

    public void Draw(float dt)
    {
        if (Raylib.WindowShouldClose())
            PromptUnsavedChanges(() => Running = false);
        
        Raylib.ClearBackground(Color.DarkGray);
        
        foreach (var shortcut in keyShortcuts.Values)
            shortcut.IsActivated = false;

        rlImGui.Begin();
        ImGui.DockSpaceOverViewport();

        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                ImGuiMenuItemShortcut("New", "New");
                ImGuiMenuItemShortcut("Open", "Open");
                ImGuiMenuItemShortcut("Save", "Save");
                ImGuiMenuItemShortcut("SaveAs", "Save As...");

                ImGui.Separator();
                
                if (ImGui.MenuItem("Render"))
                {
                    PromptUnsavedChanges(() =>
                    {
                        drizzleRenderWindow = new DrizzleRenderWindow();
                    }, false);
                }

                ImGui.MenuItem("Mass Render", false);

                ImGui.Separator();
                if (ImGui.MenuItem("Quit", "Alt+F4"))
                {
                    PromptUnsavedChanges(() => Running = false);
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                ImGuiMenuItemShortcut("Undo", "Undo");
                ImGuiMenuItemShortcut("Redo", "Redo");
                ImGui.Separator();
                ImGuiMenuItemShortcut("Cut", "Cut");
                ImGuiMenuItemShortcut("Copy", "Copy");
                ImGuiMenuItemShortcut("Paste", "Paste");
                ImGui.Separator();

                if (ImGui.MenuItem("Resize Level..."))
                {
                    levelResizeWin = new LevelResizeWindow();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Grid", null, editorWindow.LevelRenderer.ViewGrid))
                {
                    editorWindow.LevelRenderer.ViewGrid = !editorWindow.LevelRenderer.ViewGrid;
                }

                if (ImGui.MenuItem("Obscured Beams", null, editorWindow.LevelRenderer.ViewObscuredBeams))
                {
                    editorWindow.LevelRenderer.ViewObscuredBeams = !editorWindow.LevelRenderer.ViewObscuredBeams;
                }
                
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About..."))
                    promptAbout = true;
                
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }

        HandleShortcuts();

        editorWindow.Render(dt);

        if (ImGui.IsKeyPressed(ImGuiKey.F1))
            ShowDemoWindow = !ShowDemoWindow;
        
        // this is how imgui is documented
        // you see what it can do and when i want to know how it does that,
        // i ctrl+f imgui_demo.cpp.
        // I guess it works?
        if (ShowDemoWindow)
        {
            ImGui.ShowDemoWindow(ref ShowDemoWindow);
        }

        // render level browser
        LevelBrowser.Singleton?.Render();

        // render drizzle render, if in progress
        if (drizzleRenderWindow is not null)
        {
            // if this returns true, the render window had closed
            if (drizzleRenderWindow.DrawWindow())
            {
                drizzleRenderWindow.Dispose();
                drizzleRenderWindow = null;

                // the whole render process allocates ~1 gb of memory
                // so, try to free all that
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForFullGCComplete();
            }
        }

        // render level resize window
        if (levelResizeWin is not null)
        {
            levelResizeWin.DrawWindow();
            if (!levelResizeWin.IsWindowOpen) levelResizeWin = null;
        }
        
        // notification window
        if (notificationTime > 0f) {
            ImGuiWindowFlags windowFlags =
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;
            
            ImGuiViewportPtr viewport = ImGui.GetMainViewport();
            const float pad = 10f;

            Vector2 windowPos = new(
                viewport.WorkPos.X + pad,
                viewport.WorkPos.Y + viewport.WorkSize.Y - pad
            );
            Vector2 windowPosPivot = new(0f, 1f);
            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Always, windowPosPivot);

            var flashValue = (float) (Math.Sin(Math.Min(notifFlash, 0.25f) * 16 * Math.PI) + 1f) / 2f;
            var windowBg = ImGui.GetStyle().Colors[(int) ImGuiCol.WindowBg];

            if (flashValue > 0.5f)
                ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(flashValue, flashValue, flashValue, windowBg.W));
            else
                ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
            
            if (ImGui.Begin("Notification", windowFlags))
                ImGui.TextUnformatted(notification);
            ImGui.End();

            ImGui.PopStyleColor();

            notificationTime -= dt;
            notifFlash += dt;
        }

        // about screen
        if (promptAbout)
        {
            promptAbout = false;
            ImGui.OpenPopup("About Rained");

            // center popup modal
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        }

        bool unused = true;
        if (ImGui.BeginPopupModal("About Rained", ref unused, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            // TODO: version number, build date, os/runtime information, library licenses
            rlImGui.Image(rainedLogo);
            ImGui.Text("A Rain World level editor - Beta 1.0.0");
            ImGui.NewLine();
            ImGui.Text("(c) 2024 pkhead - MIT License");
            ImGui.Text("Rain World by Videocult/Adult Swim Games/Akapura Games");
        }

        // prompt unsaved changes
        if (promptUnsavedChanges)
        {
            promptUnsavedChanges = false;
            ImGui.OpenPopup("Unsaved Changes");

            // center popup modal
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        }

        unused = true;
        if (ImGui.BeginPopupModal("Unsaved Changes", ref unused, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (promptUnsavedChangesCancelable)
            {
                ImGui.Text("Do you want to save your changes before proceeding?");
            }
            else
            {
                ImGui.Text("You must save before proceeding.\nDo you want to save now?");
            }

            if (ImGui.Button("Yes"))
            {
                ImGui.CloseCurrentPopup();

                // unsaved change callback is run in SaveLevel
                if (string.IsNullOrEmpty(currentFilePath))
                    LevelBrowser.Open(LevelBrowser.OpenMode.Write, SaveLevel, currentFilePath);
                else
                    SaveLevel(currentFilePath);
            }

            ImGui.SameLine();
            if (ImGui.Button("No") || (!promptUnsavedChangesCancelable && ImGui.IsKeyPressed(ImGuiKey.Escape)))
            {
                ImGui.CloseCurrentPopup();

                if (promptUnsavedChangesCancelable)
                {
                    if (promptCallback is not null)
                    {
                        promptCallback();
                        promptCallback = null;
                    }
                }
            }

            if (promptUnsavedChangesCancelable)
            {
                ImGui.SameLine();
                if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    ImGui.CloseCurrentPopup();
                    promptCallback = null;
                }
            }

            ImGui.EndPopup();
        }

        rlImGui.End();
    }
    
    public void Undo() => changeHistory.Undo();
    public void Redo() => changeHistory.Redo();
}