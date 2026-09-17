using System.IO;
using System.Text.RegularExpressions;

namespace Ets2ProfileManager
{
    internal enum ControlsEntryKind { Device, Input, Constant, Mix, Other }

    internal sealed record ControlsEntry(ControlsEntryKind Kind, string Name, string Inner);

    /// <summary>Parsed controls.sii: raw lines + classified config_lines entries.</summary>
    internal sealed class ControlsDoc
    {
        public List<string> Lines { get; } = new();
        public List<(int lineIndex, ControlsEntry entry)> Entries { get; } = new();
        public bool Valid { get; set; }
    }

    internal sealed record CfgEntry(string Key, string Value, string RawLine);

    /// <summary>Parsed uset config file: raw lines + key index. Unknown lines preserved verbatim.</summary>
    internal sealed class CfgDoc
    {
        public List<string> Lines { get; } = new();
        public Dictionary<string, (List<int> lineIndexes, CfgEntry entry)> ByKey { get; } = new(StringComparer.Ordinal);
        public bool Loaded { get; set; }
    }

    internal sealed record SettingGroup(
        string Id,
        string File,
        string Title,
        string Description,
        string? Warning,
        Func<ControlsEntry, bool>? MatchControls,
        Func<string, bool>? MatchCfgKey);

    internal sealed record SettingDiff(string File, string Group, string Key, string OldValue, string NewValue);

    internal sealed record ApplyResult(bool Ok, int Applied, List<string> Skipped, List<string> Errors);

    /// <summary>
    /// Syncs wheel/driving settings between profiles of the same game.
    /// Replace-only for controls.sii (never renumbers config_lines indices);
    /// replace-or-append for uset cfg files. All writes are atomic (temp + replace)
    /// and re-validated after writing.
    /// </summary>
    internal static class SettingsSync
    {
        public const string ControlsFile = "controls.sii";
        public const string LocalCfgFile = "config_local.cfg";
        public const string ConfigFile = "config.cfg";

        private static readonly HashSet<string> SteerConstants = new(StringComparer.Ordinal)
        {
            "c_steer_func", "c_steer_dz", "c_rsteersens", "c_asteersens", "c_relatsteer", "c_stm_simple"
        };

        public static IReadOnlyList<SettingGroup> Groups { get; } = new List<SettingGroup>
        {
            new("ffb", ControlsFile, "Force feedback",
                "c_ff_* / c_vff_* gains, friction, bumps, collisions",
                null,
                e => e.Kind == ControlsEntryKind.Constant &&
                     (e.Name.StartsWith("c_ff_", StringComparison.Ordinal) || e.Name.StartsWith("c_vff_", StringComparison.Ordinal)),
                null),
            new("steer", ControlsFile, "Steering tuning",
                "Steering function, linearity, sensitivity, auto-centering feel",
                null,
                e => e.Kind == ControlsEntryKind.Constant && SteerConstants.Contains(e.Name),
                null),
            new("deadzone", ControlsFile, "Deadzones & invert",
                "Pedal/steering deadzones, axis invert and response flags",
                null,
                e => e.Kind == ControlsEntryKind.Constant &&
                     (e.Name.Contains("_dz", StringComparison.Ordinal) ||
                      (e.Name.StartsWith("c_j", StringComparison.Ordinal) &&
                       (e.Name.StartsWith("c_jz", StringComparison.Ordinal) || e.Name.StartsWith("c_ji", StringComparison.Ordinal)))),
                null),
            new("axes", ControlsFile, "Axes & devices",
                "device/input lines: which wheel/pedal axis drives steer, throttle, brake, clutch",
                "Hardware-specific. Only sync between profiles on this same PC/wheel.",
                e => e.Kind is ControlsEntryKind.Device or ControlsEntryKind.Input,
                null),
            new("bindings", ControlsFile, "Button & key bindings",
                "All mix lines: every button/assignment in the game",
                "Large set. Overwrites all button assignments on targets.",
                e => e.Kind == ControlsEntryKind.Mix,
                null),
            new("miscctl", ControlsFile, "Other input constants",
                "Head tracking, ATR, speeds and remaining c_* constants",
                null,
                e => e.Kind == ControlsEntryKind.Constant,
                null),
            new("shifting", LocalCfgFile, "Shifting & transmission",
                "H-shifter layout/split/sync, transmission mode",
                null, null,
                k => k.StartsWith("g_hshifter", StringComparison.Ordinal) || k == "g_trans"),
            new("pedals", LocalCfgFile, "Pedals & brake",
                "Clutch range, brake intensity, clutch+brake combo",
                null, null,
                k => k is "g_pedal_clutch_range" or "g_brake_intensity" or "g_clutch_brake"),
            new("steercam", LocalCfgFile, "Wheel range & camera",
                "Steering animation range, auto-centering, cam steering",
                null, null,
                k => k is "g_steer_anim_range" or "g_steer_autocenter" or "g_cam_steering"
                    or "g_cam_steering_value" or "g_cam_steering_reverse"),
            new("aids", ConfigFile, "Driving aids & stability",
                "Truck/trailer stability, retarder, emergency brake, lane assist, cruise, crawl",
                null, null,
                k => k is "g_truck_stability" or "g_trailer_stability"
                    or "g_retarder_auto" or "g_motor_brake_auto"
                    or "g_emergency_brake" or "g_emergency_brake_mode"
                    or "g_lane_assistant" or "g_cruise_control_smart" or "g_cruise_control_grid"
                    or "g_transmission_crawl_ignore" or "g_hill_brake_assist"
                    or "g_engine_start_auto" or "g_axle_drop_auto"),
            new("gameplay", ConfigFile, "Gameplay & simulation",
                "Fuel/hardcore sim, parking difficulty, coupling, cargo rules, economy, physics feel",
                null, null,
                k => k is "g_fuel_simulation" or "g_hardcore_simulation"
                    or "g_parking_difficulty" or "g_simple_parking_doubles"
                    or "g_trailer_advanced_coupling"
                    or "g_cargo_load_require_park_brake" or "g_cargo_load_require_engine_off"
                    or "g_relocate_truck_and_driver_tog"
                    or "g_speed_warning" or "g_use_speed_limiter"
                    or "g_road_events" or "g_detours" or "g_bad_weather_factor"
                    or "g_income_factor" or "g_currency"
                    or "g_start_in_truck" or "g_park_brake_init"
                    or "g_adaptive_shift" or "g_auto_diff_lock"
                    or "g_automatic_high_beams" or "g_automatic_headlights"
                    or "g_throttle_double_tap" or "g_acc"
                    or "g_suspension_stiffness" or "g_cabin_suspension_stiffness" or "g_driveshaft_torque"),
            new("units", ConfigFile, "Units",
                "mph, Fahrenheit, psi, pounds, gallons, mpg",
                null, null,
                k => k is "g_mph" or "g_fahrenheit" or "g_psi" or "g_pounds" or "g_gallon" or "g_mpg"),
            new("camera", ConfigFile, "Camera",
                "Cabin physics, horizon lock, FOV speed, blinker cam, blind spot, mirror physics",
                null, null,
                k => k is "g_cam_physics" or "g_cam_physics_value" or "g_camera_horizon_lock"
                    or "g_cam_fov_speed" or "g_cam_blinker" or "g_blind_spot" or "g_phys_mirrors"),
            new("hud", ConfigFile, "HUD & navigation",
                "Speed warnings, map/GPS, voice nav, job list, markers",
                null, null,
                k => k is "g_hud_speed_warning" or "g_hud_speed_limit"
                    or "g_show_game_elements" or "g_show_game_blockers"
                    or "g_mp_name_tags" or "g_disable_beacons"
                    or "g_ui_map_align" or "g_gps_routing_mode" or "g_gps_navigation"
                    or "g_voice_navigation" or "g_voice_navigation_pack"
                    or "g_job_distance" or "g_job_distance_limit" or "g_world_map_zoom"),
        };

        private static readonly Regex ConfigLineRe = new(
            @"^\s*config_lines\[(\d+)\]:\s*""((?:[^""\\]|\\.)*)""\s*$", RegexOptions.Compiled);

        private static readonly Regex UsetRe = new(
            @"^\s*uset\s+(\S+)\s+""?(.*?)""?\s*$", RegexOptions.Compiled);

        public static ControlsDoc LoadControls(string profileDir)
        {
            ControlsDoc doc = new();
            string path = Path.Combine(profileDir, ControlsFile);
            if (!File.Exists(path))
            {
                return doc;
            }
            try
            {
                string[] lines = File.ReadAllLines(path);
                doc.Lines.AddRange(lines);
                doc.Valid = lines.Length > 0 && lines[0].Trim() == "SiiNunit";
                if (!doc.Valid)
                {
                    return doc;
                }
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = ConfigLineRe.Match(lines[i]);
                    if (!m.Success)
                    {
                        continue;
                    }
                    string inner = m.Groups[2].Value;
                    doc.Entries.Add((i, Classify(inner)));
                }
            }
            catch { doc.Valid = false; }
            return doc;
        }

        private static ControlsEntry Classify(string inner)
        {
            string t = inner.TrimStart();
            if (t.StartsWith("device ", StringComparison.Ordinal))
            {
                // Key by device slot name (joy, joy2, keyboard...), NOT a shared
                // literal: every line must have a distinct key or syncs collide.
                string name = t.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p ? p[1] : "device";
                return new(ControlsEntryKind.Device, name, inner);
            }
            if (t.StartsWith("input ", StringComparison.Ordinal))
            {
                string name = t.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p ? p[1] : "input";
                return new(ControlsEntryKind.Input, name, inner);
            }
            if (t.StartsWith("constant ", StringComparison.Ordinal))
            {
                string name = t.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p ? p[1] : "constant";
                return new(ControlsEntryKind.Constant, name, inner);
            }
            if (t.StartsWith("mix ", StringComparison.Ordinal))
            {
                string name = t.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } p ? p[1] : "mix";
                return new(ControlsEntryKind.Mix, name, inner);
            }
            return new(ControlsEntryKind.Other, t.Length > 24 ? t[..24] : t, inner);
        }

        public static CfgDoc LoadCfg(string profileDir, string file)
        {
            CfgDoc doc = new();
            string path = Path.Combine(profileDir, file);
            if (!File.Exists(path))
            {
                return doc;
            }
            try
            {
                string[] lines = File.ReadAllLines(path);
                doc.Lines.AddRange(lines);
                doc.Loaded = true;
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = UsetRe.Match(lines[i]);
                    if (!m.Success)
                    {
                        continue;
                    }
                    string key = m.Groups[1].Value;
                    // Keep every occurrence: the game may read any of them,
                    // so all must be updated on write.
                    if (!doc.ByKey.TryGetValue(key, out var existing))
                    {
                        doc.ByKey[key] = (new List<int> { i }, new CfgEntry(key, m.Groups[2].Value, lines[i]));
                    }
                    else
                    {
                        existing.lineIndexes.Add(i);
                    }
                }
            }
            catch { doc.Loaded = false; }
            return doc;
        }

        private static string DisplayValue(string file, ControlsEntry e)
        {
            if (e.Kind == ControlsEntryKind.Constant)
            {
                string[] parts = e.Inner.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 3 ? parts[2] : e.Inner;
            }
            if (e.Inner.Length > 80)
            {
                return e.Inner[..80] + "…";
            }
            return e.Inner;
        }

        private static string EntryKey(ControlsEntry e) => $"{e.Kind}:{e.Name}";

        /// <summary>Keys selected by group ids, resolved against the source docs.</summary>
        public static Dictionary<string, List<(SettingGroup group, string key, string newValue)>> SelectedKeys(
            ControlsDoc srcCtl, CfgDoc srcLocal, CfgDoc srcCfg, IEnumerable<string> groupIds)
        {
            var ids = new HashSet<string>(groupIds);
            var result = new Dictionary<string, List<(SettingGroup, string, string)>>(StringComparer.Ordinal);
            foreach (SettingGroup g in Groups)
            {
                if (!ids.Contains(g.Id))
                {
                    continue;
                }
                if (!result.TryGetValue(g.File, out var list))
                {
                    list = new();
                    result[g.File] = list;
                }
                if (g.File == ControlsFile && g.MatchControls != null && srcCtl.Valid)
                {
                    var match = g.MatchControls;
                    foreach ((_, ControlsEntry e) in srcCtl.Entries)
                    {
                        if (match(e))
                        {
                            list.Add((g, EntryKey(e), DisplayValue(g.File, e)));
                        }
                    }
                }
                else if (g.MatchCfgKey != null)
                {
                    CfgDoc src = g.File == LocalCfgFile ? srcLocal : srcCfg;
                    if (src.Loaded)
                    {
                        var keyMatch = g.MatchCfgKey;
                        foreach (var kv in src.ByKey)
                        {
                            if (keyMatch(kv.Key))
                            {
                                list.Add((g, kv.Key, kv.Value.entry.Value));
                            }
                        }
                    }
                }
            }
            return result;
        }

        public static List<SettingDiff> ComputeDiff(
            ControlsDoc srcCtl, CfgDoc srcLocal, CfgDoc srcCfg,
            ControlsDoc tgtCtl, CfgDoc tgtLocal, CfgDoc tgtCfg,
            IEnumerable<string> groupIds)
        {
            var diffs = new List<SettingDiff>();
            var selected = SelectedKeys(srcCtl, srcLocal, srcCfg, groupIds);

            if (selected.TryGetValue(ControlsFile, out var ctlKeys) && tgtCtl.Valid)
            {
                var tgtByKey = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (SettingGroup g in Groups.Where(g => groupIds.Contains(g.Id) && g.File == ControlsFile && g.MatchControls != null))
                {
                    var match = g.MatchControls;
                    foreach ((_, ControlsEntry e) in tgtCtl.Entries)
                    {
                        if (match != null && match(e))
                        {
                            tgtByKey[EntryKey(e)] = DisplayValue(g.File, e);
                        }
                    }
                }
                foreach ((SettingGroup g, string key, string newVal) in ctlKeys)
                {
                    if (!tgtByKey.TryGetValue(key, out string? oldVal))
                    {
                        diffs.Add(new SettingDiff(ControlsFile, g.Title, PrettyKey(key), "(missing in target)", newVal));
                    }
                    else if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                    {
                        diffs.Add(new SettingDiff(ControlsFile, g.Title, PrettyKey(key), oldVal, newVal));
                    }
                }
            }

            foreach (string file in new[] { LocalCfgFile, ConfigFile })
            {
                if (!selected.TryGetValue(file, out var keys))
                {
                    continue;
                }
                CfgDoc tgt = file == LocalCfgFile ? tgtLocal : tgtCfg;
                if (!tgt.Loaded)
                {
                    continue;
                }
                foreach ((SettingGroup g, string key, string newVal) in keys)
                {
                    if (!tgt.ByKey.TryGetValue(key, out var t))
                    {
                        diffs.Add(new SettingDiff(file, g.Title, key, "(missing — will append)", newVal));
                    }
                    else if (!string.Equals(t.entry.Value, newVal, StringComparison.Ordinal))
                    {
                        diffs.Add(new SettingDiff(file, g.Title, key, t.entry.Value, newVal));
                    }
                }
            }
            return diffs;
        }

        private static string PrettyKey(string entryKey)
        {
            int i = entryKey.IndexOf(':');
            return i >= 0 ? entryKey[(i + 1)..] : entryKey;
        }

        /// <summary>
        /// Applies selected groups from source profile dir to target profile dir.
        /// Refuses cross-game sync and identical dirs even if the caller forgot.
        /// Caller must take backups first.
        /// </summary>
        public static ApplyResult Apply(string sourceDir, string targetDir, IEnumerable<string> groupIds,
            string? sourceGame = null, string? targetGame = null)
        {
            var ids = new HashSet<string>(groupIds);
            var skipped = new List<string>();
            var errors = new List<string>();
            int applied = 0;

            if (string.Equals(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, 0, skipped, new() { "Source and target are the same directory. Aborted." });
            }

            if (!string.IsNullOrEmpty(sourceGame) && !string.IsNullOrEmpty(targetGame) &&
                !string.Equals(sourceGame, targetGame, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, 0, skipped, new() { $"Cross-game sync refused ({sourceGame} → {targetGame})." });
            }

            ControlsDoc srcCtl = LoadControls(sourceDir);
            CfgDoc srcLocal = LoadCfg(sourceDir, LocalCfgFile);
            CfgDoc srcCfg = LoadCfg(sourceDir, ConfigFile);

            // controls.sii — replace existing entries only, keep target indices.
            var ctlGroups = Groups.Where(g => ids.Contains(g.Id) && g.File == ControlsFile && g.MatchControls != null).ToList();
            if (ctlGroups.Count > 0)
            {
                string tgtPath = Path.Combine(targetDir, ControlsFile);
                if (!File.Exists(tgtPath))
                {
                    skipped.Add($"{ControlsFile}: target has no {ControlsFile}, skipped whole file.");
                }
                else if (!srcCtl.Valid)
                {
                    errors.Add($"{ControlsFile}: source file missing or invalid, skipped.");
                }
                else
                {
                    ControlsDoc tgtCtl = LoadControls(targetDir);
                    if (!tgtCtl.Valid)
                    {
                        errors.Add($"{ControlsFile}: target file invalid (not SiiNunit), skipped.");
                    }
                    else
                    {
                        var srcByKey = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (SettingGroup g in ctlGroups)
                        {
                            var match = g.MatchControls;
                            if (match == null)
                            {
                                continue;
                            }
                            foreach ((_, ControlsEntry e) in srcCtl.Entries)
                            {
                                if (match(e))
                                {
                                    srcByKey[EntryKey(e)] = e.Inner;
                                }
                            }
                        }
                        var newLines = new List<string>(tgtCtl.Lines);
                        var matchedGroups = new Dictionary<string, SettingGroup>();
                        var appliedLines = new HashSet<int>();
                        foreach (SettingGroup g in ctlGroups)
                        {
                            var match = g.MatchControls;
                            if (match == null)
                            {
                                continue;
                            }
                            foreach ((int li, ControlsEntry e) in tgtCtl.Entries)
                            {
                                // Groups overlap (e.g. c_steer_dz is both steer+deadzone):
                                // count each target line once.
                                if (appliedLines.Contains(li))
                                {
                                    matchedGroups.TryAdd(EntryKey(e), g);
                                    continue;
                                }
                                if (match(e) && srcByKey.TryGetValue(EntryKey(e), out string? srcInner))
                                {
                                    Match m = ConfigLineRe.Match(newLines[li]);
                                    if (m.Success)
                                    {
                                        newLines[li] = $"config_lines[{m.Groups[1].Value}]: \"{srcInner}\"";
                                        applied++;
                                        appliedLines.Add(li);
                                    }
                                    matchedGroups.TryAdd(EntryKey(e), g);
                                }
                            }
                        }
                        foreach (string key in srcByKey.Keys)
                        {
                            if (!matchedGroups.ContainsKey(key))
                            {
                                skipped.Add($"{ControlsFile}: {PrettyKey(key)} missing in target, not appended (indices must stay stable).");
                            }
                        }
                        string? err = WriteAndValidateControls(tgtPath, newLines);
                        if (err != null)
                        {
                            errors.Add(err);
                        }
                    }
                }
            }

            // uset cfg files — replace or append.
            foreach (string file in new[] { LocalCfgFile, ConfigFile })
            {
                var fileGroups = Groups.Where(g => ids.Contains(g.Id) && g.File == file && g.MatchCfgKey != null).ToList();
                if (fileGroups.Count == 0)
                {
                    continue;
                }
                string tgtPath = Path.Combine(targetDir, file);
                if (!File.Exists(tgtPath))
                {
                    skipped.Add($"{file}: target has no {file}, skipped whole file.");
                    continue;
                }
                CfgDoc src = file == LocalCfgFile ? srcLocal : srcCfg;
                if (!src.Loaded)
                {
                    errors.Add($"{file}: source file missing or unreadable, skipped.");
                    continue;
                }
                CfgDoc tgt = LoadCfg(targetDir, file);
                if (!tgt.Loaded)
                {
                    errors.Add($"{file}: target file unreadable, skipped.");
                    continue;
                }
                var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (SettingGroup g in fileGroups)
                {
                    var keyMatch = g.MatchCfgKey;
                    if (keyMatch == null)
                    {
                        continue;
                    }
                    foreach (var kv in src.ByKey)
                    {
                        if (keyMatch(kv.Key))
                        {
                            wanted[kv.Key] = kv.Value.entry.Value;
                        }
                    }
                }
                var newLines = new List<string>(tgt.Lines);
                foreach (var kv in wanted)
                {
                    if (tgt.ByKey.TryGetValue(kv.Key, out var t))
                    {
                        foreach (int li in t.lineIndexes)
                        {
                            newLines[li] = $"uset {kv.Key} \"{kv.Value}\"";
                        }
                        applied++;
                    }
                    else
                    {
                        newLines.Add($"uset {kv.Key} \"{kv.Value}\"");
                        applied++;
                    }
                }
                string? err = WriteAndValidateCfg(tgtPath, newLines, wanted);
                if (err != null)
                {
                    errors.Add(err);
                }
            }

            return new(errors.Count == 0, applied, skipped, errors);
        }

        private static string? WriteAndValidateControls(string path, List<string> newLines)
        {
            try
            {
                string tmp = path + ".ets2pm-tmp";
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                }
                File.WriteAllLines(tmp, newLines);
                // Validate before replacing: header + entry count intact.
                string[] check = File.ReadAllLines(tmp);
                if (check.Length == 0 || check[0].Trim() != "SiiNunit")
                {
                    File.Delete(tmp);
                    return $"{ControlsFile}: validation failed after write (header lost). Target untouched.";
                }
                int before = newLines.Count(l => ConfigLineRe.IsMatch(l));
                int after = check.Count(l => ConfigLineRe.IsMatch(l));
                if (before != after)
                {
                    File.Delete(tmp);
                    return $"{ControlsFile}: validation failed after write (entry count {before} → {after}). Target untouched.";
                }
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
                return null;
            }
            catch (Exception ex)
            {
                return $"{ControlsFile}: write failed: {ex.Message}";
            }
        }

        private static string? WriteAndValidateCfg(string path, List<string> newLines, Dictionary<string, string> wanted)
        {
            try
            {
                string tmp = path + ".ets2pm-tmp";
                File.WriteAllLines(tmp, newLines);
                CfgDoc check = new();
                string[] lines = File.ReadAllLines(tmp);
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = UsetRe.Match(lines[i]);
                    if (m.Success)
                    {
                        if (!check.ByKey.TryGetValue(m.Groups[1].Value, out var existing))
                        {
                            check.ByKey[m.Groups[1].Value] = (new List<int> { i }, new CfgEntry(m.Groups[1].Value, m.Groups[2].Value, lines[i]));
                        }
                        else
                        {
                            existing.lineIndexes.Add(i);
                        }
                    }
                }
                foreach (var kv in wanted)
                {
                    if (!check.ByKey.TryGetValue(kv.Key, out var got) || got.entry.Value != kv.Value)
                    {
                        File.Delete(tmp);
                        return $"{Path.GetFileName(path)}: validation failed for {kv.Key}. Target untouched.";
                    }
                    // Every duplicate occurrence must hold the new value too.
                    foreach (int li in got.lineIndexes)
                    {
                        Match m = UsetRe.Match(lines[li]);
                        if (!m.Success || m.Groups[2].Value != kv.Value)
                        {
                            File.Delete(tmp);
                            return $"{Path.GetFileName(path)}: validation failed for {kv.Key} (duplicate). Target untouched.";
                        }
                    }
                }
                File.Copy(tmp, path, overwrite: true);
                File.Delete(tmp);
                return null;
            }
            catch (Exception ex)
            {
                return $"{Path.GetFileName(path)}: write failed: {ex.Message}";
            }
        }
    }
}
