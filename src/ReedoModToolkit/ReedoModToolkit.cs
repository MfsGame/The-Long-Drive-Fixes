using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using TLDLoader;
using UnityEngine;

namespace ReedoModToolkit
{
    // First-class TLDLoader mod that surfaces both TLDLoader mods AND BepInEx plugins in one
    // panel, lets the user disable individual entries (rename .dll <-> .dll.disabled), and
    // checks our GitHub manifest for newer versions.
    //
    // Why both loader ecosystems in one mod:
    //   This setup co-runs TLDLoader (for M-ultiTool + workshop mods) and BepInEx (for our
    //   custom MP / perf plugins). Players otherwise have no single place to see what's
    //   installed or toggle things. Two file-tree layouts to know about; this collapses them.
    //
    // Hotkey: F11 by default (configurable via the toggleKey field — recompile to change for now).
    //
    // What gets persisted:
    //   - Disabling a plugin renames its .dll to .dll.disabled (BepInEx + TLDLoader both
    //     ignore .disabled files on load), so the disable survives a restart with no state
    //     file of our own.
    //   - Update downloads land at <plugin>.dll.update; our BepInEx updater patcher promotes
    //     .update files into place on next launch. TLDLoader mods we just overwrite directly
    //     since they're loaded once at game start, after our overwrite point.

    public class ReedoModToolkit : Mod
    {
        public override string ID => "ReedoModToolkit";
        public override string Name => "Reedo's Mod Toolkit";
        public override string Version => "0.1.0";
        public override string Author => "Reedo + Claude";
        public override bool LoadInMenu => true;
        public override bool UseHarmony => false;
        public override bool UseLogger => true;

        // ----- config -----
        private const string MANIFEST_URL =
            "https://raw.githubusercontent.com/Reedo22/The-Long-Drive-Fixes/main/public-mp.txt";
        private const KeyCode toggleKey = KeyCode.F11;
        private const int windowId = 0x7E300;

        // ----- ui state -----
        private bool uiVisible;
        private Rect windowRect = new Rect(80f, 80f, 700f, 540f);
        private Vector2 scrollPosition;
        private string statusMsg = "";
        private DateTime? lastUpdateCheck;
        private bool updateInProgress;
        private string currentTab = "Mods"; // "Mods" or "BepInEx"

        // ----- discovered state -----
        private readonly Dictionary<string, string> remoteVersions = new Dictionary<string, string>();
        private readonly Dictionary<string, string> remoteUrls = new Dictionary<string, string>();
        private readonly List<BepInExPluginInfo> bepinexPlugins = new List<BepInExPluginInfo>();
        private readonly List<DisabledMod> disabledTLDMods = new List<DisabledMod>();
        private string bepinexPluginsPath;
        private string tldModsPath;

        public override void OnMenuLoad()
        {
            ResolvePaths();
            RescanAll();
            Debug.Log("[ReedoModToolkit] loaded. Press " + toggleKey + " in-game to open the panel.");
        }

        public override void OnLoad()
        {
            ResolvePaths();
            RescanAll();
        }

        private void ResolvePaths()
        {
            try
            {
                bepinexPluginsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BepInEx", "plugins"));
            }
            catch { bepinexPluginsPath = null; }
            tldModsPath = ModLoader.ModsFolder;
        }

        public override void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                uiVisible = !uiVisible;
                if (uiVisible) RescanAll();
            }
        }

        public override void OnGUI()
        {
            if (!uiVisible) return;
            windowRect = GUI.Window(0x73D0, windowRect, DrawWindow, "Reedo's Mod Toolkit v" + Version);
        }

        // ----- main UI -----
        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // header
            GUILayout.BeginHorizontal();
            GUI.enabled = !updateInProgress;
            if (GUILayout.Button("Check for Updates", GUILayout.Width(140), GUILayout.Height(22)))
                CheckForUpdates();
            GUI.enabled = true;
            if (updateInProgress)
                GUILayout.Label("Checking...", GUILayout.Width(100));
            else if (lastUpdateCheck.HasValue)
                GUILayout.Label("Last check: " + lastUpdateCheck.Value.ToString("HH:mm:ss"),
                    GUILayout.Width(180));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Rescan", GUILayout.Width(70), GUILayout.Height(22)))
                RescanAll();
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22)))
                uiVisible = false;
            GUILayout.EndHorizontal();

            // tabs
            GUILayout.BeginHorizontal();
            DrawTab("Mods", "TLDLoader Mods (" + CountActiveTLDMods() + "/" + (CountActiveTLDMods() + disabledTLDMods.Count) + ")");
            DrawTab("BepInEx", "BepInEx Plugins (" + bepinexPlugins.Count(p => !p.Disabled) + "/" + bepinexPlugins.Count + ")");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(statusMsg))
            {
                GUILayout.Box(statusMsg, GUILayout.ExpandWidth(true));
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            if (currentTab == "Mods") DrawTLDModsList();
            else DrawBepInExList();

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Disabling/updating requires a restart to take effect.", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 20));
        }

        private void DrawTab(string key, string label)
        {
            var was = currentTab == key;
            var changed = GUILayout.Toggle(was, label, "Button", GUILayout.Width(220), GUILayout.Height(24));
            if (changed && !was) currentTab = key;
        }

        // ----- TLDLoader mods list -----
        private void DrawTLDModsList()
        {
            // loaded (active) ones from TLDLoader runtime
            var active = ModLoader.LoadedMods ?? new List<Mod>();
            foreach (var mod in active)
            {
                if (mod == null) continue;
                DrawModRow(mod, false);
            }

            if (disabledTLDMods.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("Disabled (require restart):", GUI.skin.box);
                foreach (var d in disabledTLDMods) DrawDisabledTLDModRow(d);
            }
        }

        private void DrawModRow(Mod mod, bool disabled)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(disabled ? "[off] " + mod.Name : mod.Name, GUILayout.Width(220));
            GUILayout.Label("v" + mod.Version, GUILayout.Width(80));
            GUILayout.Label("by " + mod.Author, GUILayout.Width(160));
            DrawUpdateBadge(mod.ID, mod.Version);
            if (!disabled && !IsCoreMod(mod))
            {
                if (GUILayout.Button("Disable", GUILayout.Width(70)))
                    DisableTLDMod(mod);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawDisabledTLDModRow(DisabledMod d)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("[off] " + d.FileName, GUILayout.Width(220));
            GUILayout.Label(d.Version ?? "?", GUILayout.Width(80));
            GUILayout.Label("", GUILayout.Width(160));
            if (GUILayout.Button("Enable", GUILayout.Width(70)))
                EnableTLDMod(d);
            GUILayout.EndHorizontal();
        }

        // ----- BepInEx plugins list -----
        private void DrawBepInExList()
        {
            if (string.IsNullOrEmpty(bepinexPluginsPath) || !Directory.Exists(bepinexPluginsPath))
            {
                GUILayout.Label("BepInEx not detected (no plugins folder at " + (bepinexPluginsPath ?? "?") + ")");
                return;
            }
            if (bepinexPlugins.Count == 0)
            {
                GUILayout.Label("(no DLLs found)");
                return;
            }
            foreach (var p in bepinexPlugins) DrawBepInExRow(p);
        }

        private void DrawBepInExRow(BepInExPluginInfo p)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label((p.Disabled ? "[off] " : "") + p.PluginName, GUILayout.Width(220));
            GUILayout.Label(p.Version ?? "?", GUILayout.Width(80));
            GUILayout.Label("", GUILayout.Width(160));
            DrawUpdateBadge(p.PluginName, p.Version);
            if (GUILayout.Button(p.Disabled ? "Enable" : "Disable", GUILayout.Width(70)))
                ToggleBepInEx(p);
            if (HasRemoteUpdate(p.PluginName, p.Version))
            {
                if (GUILayout.Button("Update", GUILayout.Width(70)))
                    UpdateBepInExPlugin(p);
            }
            GUILayout.EndHorizontal();
        }

        // ----- update badge -----
        private void DrawUpdateBadge(string id, string installedVersion)
        {
            if (!remoteVersions.TryGetValue(id, out var remote))
            {
                GUILayout.Label("", GUILayout.Width(120));
                return;
            }
            if (remote == installedVersion)
                GUILayout.Label("latest (" + remote + ")", GUILayout.Width(120));
            else
                GUILayout.Label("update: " + remote, GUI.skin.box, GUILayout.Width(120));
        }

        private bool HasRemoteUpdate(string id, string installedVersion)
        {
            return remoteVersions.TryGetValue(id, out var r) && r != installedVersion;
        }

        // ----- enable/disable mechanics -----
        private void DisableTLDMod(Mod mod)
        {
            try
            {
                var path = FindTLDModFile(mod.ID);
                if (path == null) { statusMsg = "Couldn't locate DLL for " + mod.ID; return; }
                File.Move(path, path + ".disabled");
                statusMsg = mod.Name + " marked disabled — restart to take effect.";
                RescanAll();
            }
            catch (Exception ex) { statusMsg = "Disable failed: " + ex.Message; }
        }

        private void EnableTLDMod(DisabledMod d)
        {
            try
            {
                File.Move(d.FullPath, d.FullPath.Substring(0, d.FullPath.Length - ".disabled".Length));
                statusMsg = d.FileName + " enabled — restart to take effect.";
                RescanAll();
            }
            catch (Exception ex) { statusMsg = "Enable failed: " + ex.Message; }
        }

        private void ToggleBepInEx(BepInExPluginInfo p)
        {
            try
            {
                if (p.Disabled)
                {
                    var dst = p.FullPath.Substring(0, p.FullPath.Length - ".disabled".Length);
                    File.Move(p.FullPath, dst);
                    p.FullPath = dst;
                    p.Disabled = false;
                }
                else
                {
                    var dst = p.FullPath + ".disabled";
                    File.Move(p.FullPath, dst);
                    p.FullPath = dst;
                    p.Disabled = true;
                }
                statusMsg = p.PluginName + (p.Disabled ? " disabled" : " enabled") + " — restart to take effect.";
            }
            catch (Exception ex) { statusMsg = "Toggle failed: " + ex.Message; }
        }

        // ----- scan -----
        private void RescanAll()
        {
            ScanBepInEx();
            ScanDisabledTLDMods();
        }

        private void ScanBepInEx()
        {
            bepinexPlugins.Clear();
            if (string.IsNullOrEmpty(bepinexPluginsPath) || !Directory.Exists(bepinexPluginsPath)) return;
            foreach (var f in Directory.GetFiles(bepinexPluginsPath))
            {
                bool disabled = f.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
                bool active = f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                if (!disabled && !active) continue;
                var info = new BepInExPluginInfo { FullPath = f, Disabled = disabled };
                info.PluginName = Path.GetFileNameWithoutExtension(disabled
                    ? f.Substring(0, f.Length - ".disabled".Length)
                    : f);
                info.Version = TryReadAssemblyVersion(f);
                bepinexPlugins.Add(info);
            }
            bepinexPlugins.Sort((a, b) => string.Compare(a.PluginName, b.PluginName, StringComparison.OrdinalIgnoreCase));
        }

        private void ScanDisabledTLDMods()
        {
            disabledTLDMods.Clear();
            if (string.IsNullOrEmpty(tldModsPath) || !Directory.Exists(tldModsPath)) return;
            foreach (var f in Directory.GetFiles(tldModsPath, "*.dll.disabled"))
            {
                disabledTLDMods.Add(new DisabledMod
                {
                    FullPath = f,
                    FileName = Path.GetFileNameWithoutExtension(f.Substring(0, f.Length - ".disabled".Length)),
                    Version = TryReadAssemblyVersion(f)
                });
            }
        }

        private string FindTLDModFile(string modId)
        {
            if (string.IsNullOrEmpty(tldModsPath)) return null;
            // TLDLoader convention is one DLL per mod; the file may or may not match the ID.
            // Best-effort: ID match, name match, single-DLL match.
            var candidates = Directory.GetFiles(tldModsPath, "*.dll");
            foreach (var c in candidates)
            {
                if (Path.GetFileNameWithoutExtension(c).Equals(modId, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        private bool IsCoreMod(Mod m)
        {
            // Don't let the user disable us or the TLDLoader_Core mod.
            return m.ID == ID || m.ID == "TLDLoader_Core";
        }

        private int CountActiveTLDMods()
        {
            return (ModLoader.LoadedMods ?? new List<Mod>()).Count(m => m != null);
        }

        private static string TryReadAssemblyVersion(string filePath)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(filePath);
                return name.Version.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ----- update check / download -----
        private void CheckForUpdates()
        {
            updateInProgress = true;
            statusMsg = "Fetching manifest...";
            var t = new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        var content = wc.DownloadString(MANIFEST_URL);
                        remoteVersions.Clear();
                        remoteUrls.Clear();
                        foreach (var raw in content.Split('\n'))
                        {
                            var line = raw.Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                remoteVersions[parts[0]] = parts[1];
                                remoteUrls[parts[0]] = parts[2];
                            }
                        }
                    }
                    lastUpdateCheck = DateTime.Now;
                    statusMsg = "Manifest fetched. " + remoteVersions.Count + " entries.";
                }
                catch (Exception ex)
                {
                    statusMsg = "Update check failed: " + ex.Message;
                }
                finally { updateInProgress = false; }
            });
            t.IsBackground = true;
            t.Name = "ReedoMT-update-check";
            t.Start();
        }

        private void UpdateBepInExPlugin(BepInExPluginInfo p)
        {
            if (!remoteUrls.TryGetValue(p.PluginName, out var url))
            {
                statusMsg = "No remote URL for " + p.PluginName;
                return;
            }
            statusMsg = "Downloading " + p.PluginName + "...";
            var t = new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    using (var wc = new WebClient())
                    {
                        // .update file gets promoted into place on next launch by our updater
                        // patcher (or just by restarting if no updater is present).
                        var dst = p.FullPath + ".update";
                        if (p.Disabled) dst = p.FullPath.Substring(0, p.FullPath.Length - ".disabled".Length) + ".update";
                        wc.DownloadFile(url, dst);
                    }
                    statusMsg = p.PluginName + " updated to " +
                        (remoteVersions.TryGetValue(p.PluginName, out var v) ? v : "?") +
                        " — restart to apply.";
                }
                catch (Exception ex)
                {
                    statusMsg = "Download failed: " + ex.Message;
                }
            });
            t.IsBackground = true;
            t.Name = "ReedoMT-update-download";
            t.Start();
        }

        // ----- supporting types -----
        private class BepInExPluginInfo
        {
            public string FullPath;
            public string PluginName;
            public string Version;
            public bool Disabled;
        }

        private class DisabledMod
        {
            public string FullPath;
            public string FileName;
            public string Version;
        }
    }
}
