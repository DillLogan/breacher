using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    /// <summary>
    /// The pause / start menu, laid out and styled like the web version: dark fade over the game, BREACH POINT title, GAMEPLAY / CHARACTER / SYSTEM groups,
    /// tab pills, a grid of fields (labels, button rows, switches, sliders, notes) and the footer with Deploy. Scales with the screen.
    /// </summary>
    public partial class HUD
    {
        // ---------------- look ----------------
        static readonly Color Text = Hex(0xe8edf1), Dim = Hex(0x93a1ae), Line = Hex(0x2f3d4b), Lab = Hex(0xd8dadc), Ink = Hex(0x0a0b0c);
        static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);
        Font fDisp, fDispB, fBody, fBodyM, fBodyS; Texture2D scrimTex; readonly Dictionary<Color, Texture2D> texCache = new Dictionary<Color, Texture2D>();
        GUIStyle sTitle, sDesc, sGroup, sTab, sLab, sVal, sNote, sSeg, sBtn, sCardH, sCardMeta, sBig, sSmallBody, sHint;
        bool measuring; string mGroup = "gameplay", mTab = "mission"; Vector2 mScroll; int wSel = 2; float uiS = 1;

        Texture2D T(Color c) { if (texCache.TryGetValue(c, out var t) && t) return t; t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); texCache[c] = t; return t; }
        GUIStyle St(Font f, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool wrap = false) { var s = new GUIStyle(GUI.skin.label) { font = f, fontSize = size, alignment = a, wordWrap = wrap, richText = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) }; s.normal.textColor = c; return s; }
        static string Sp(string s, int n = 1) { var sp = n == 1 ? " " : " "; var b = new System.Text.StringBuilder(); for (int i = 0; i < s.Length; i++) { b.Append(s[i]); if (i < s.Length - 1) b.Append(sp); } return b.ToString(); }

        void MenuStyles()
        {
            if (sTitle != null) return;
            fDisp = Resources.Load<Font>("BP/Fonts/SairaCondensed-SemiBold"); fDispB = Resources.Load<Font>("BP/Fonts/SairaCondensed-Bold"); fBody = Resources.Load<Font>("BP/Fonts/IBMPlexSans-Regular"); fBodyM = Resources.Load<Font>("BP/Fonts/IBMPlexSans-Medium"); fBodyS = Resources.Load<Font>("BP/Fonts/IBMPlexSans-SemiBold");
            var def = GUI.skin.label.font; fDisp = fDisp ? fDisp : def; fDispB = fDispB ? fDispB : fDisp; fBody = fBody ? fBody : def; fBodyM = fBodyM ? fBodyM : fBody; fBodyS = fBodyS ? fBodyS : fBodyM;
            sTitle = St(fDispB, 42, Color.white); sDesc = St(fBody, 14, Dim, TextAnchor.UpperLeft, true); sGroup = St(fDisp, 22, new Color(1, 1, 1, 0.5f)); sTab = St(fBodyS, 12, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter);
            sLab = St(fBodyS, 12, Lab); sVal = St(fBody, 12, Color.white, TextAnchor.MiddleRight); sNote = St(fBody, 12, Dim, TextAnchor.UpperLeft, true); sSeg = St(fBodyM, 13, Hex(0xe8e9ea), TextAnchor.MiddleCenter);
            sBtn = St(fDisp, 17, Color.white, TextAnchor.MiddleCenter); sCardH = St(fDisp, 19, Color.white); sCardMeta = St(fBody, 11, Dim); sBig = St(fDispB, 28, Text); sSmallBody = St(fBody, 13, Dim, TextAnchor.UpperLeft, true); sHint = St(fBodyS, 12, Dim);
            scrimTex = new Texture2D(256, 1) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++) { float t = x / 255f; float a = t < 0.42f ? Mathf.Lerp(0.95f, 0.88f, t / 0.42f) : t < 0.78f ? Mathf.Lerp(0.88f, 0.5f, (t - 0.42f) / 0.36f) : Mathf.Lerp(0.5f, 0.25f, (t - 0.78f) / 0.22f); scrimTex.SetPixel(x, 0, new Color(5 / 255f, 6 / 255f, 8 / 255f, a)); }
            scrimTex.Apply();
        }

        // ---------------- widgets ----------------
        void Box(float x, float y, float w, float h, Color c) { if (!measuring) Rect(x, y, w, h, c); }
        void Lbl(float x, float y, float w, float h, string s, GUIStyle st) { if (!measuring) GUI.Label(new UnityEngine.Rect(x, y, w, h), s, st); }
        float TextH(string s, GUIStyle st, float w) => st.CalcHeight(new GUIContent(s), w);
        float TextW(string s, GUIStyle st) => st.CalcSize(new GUIContent(s)).x;
        /// <summary>A flat button: background colour, hover colour, text colour.</summary>
        bool FlatBtn(float x, float y, float w, float h, string s, GUIStyle st, Color bg, Color hover, Color fg, bool enabled = true)
        {
            if (measuring) return false; var r = new UnityEngine.Rect(x, y, w, h); bool over = r.Contains(Event.current.mousePosition) && enabled;
            Rect(x, y, w, h, over ? hover : bg); var old = st.normal.textColor; st.normal.textColor = enabled ? fg : new Color(fg.r, fg.g, fg.b, fg.a * 0.45f); GUI.Label(r, s, st); st.normal.textColor = old;
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
        /// <summary>A row of buttons (one pressed), wrapping like the web's .seg. Returns the new index; h = height used.</summary>
        int Seg(float x, float y, float w, string[] opts, int cur, out float h)
        {
            float cx = x, cy = y, rowH = 31, gap = 4; int res = cur;
            for (int i = 0; i < opts.Length; i++)
            {
                float bw = TextW(opts[i], sSeg) + 24; if (cx > x && cx + bw > x + w) { cx = x; cy += rowH + gap; }
                bool on = i == cur; if (FlatBtn(cx, cy, bw, rowH, opts[i], sSeg, on ? Color.white : new Color(1, 1, 1, 0.06f), on ? new Color(0.86f, 0.86f, 0.86f) : new Color(1, 1, 1, 0.12f), on ? Ink : Hex(0xe8e9ea))) res = i;
                cx += bw + gap;
            }
            h = cy - y + rowH; return res;
        }
        bool Switch(float x, float y, bool v)
        {
            if (measuring) return v; Rect(x, y, 46, 24, v ? Color.white : new Color(1, 1, 1, 0.14f)); Rect(x + (v ? 25 : 3), y + 3, 16, 16, v ? Ink : Hex(0x9aa0a6));
            return GUI.Button(new UnityEngine.Rect(x, y, 46, 24), GUIContent.none, GUIStyle.none) ? !v : v;
        }
        float Slide(float x, float y, float w, float v, float a, float b)
        {
            if (measuring) return v; float k = Mathf.InverseLerp(a, b, v); Rect(x, y + 9, w, 4, new Color(1, 1, 1, 0.22f)); Rect(x, y + 9, w * k, 4, Color.white); Rect(x + w * k - 7, y + 4, 14, 14, Color.white);
            var thumb = new GUIStyle { fixedWidth = 14, fixedHeight = 22 }; return GUI.HorizontalSlider(new UnityEngine.Rect(x, y, w, 22), v, a, b, GUIStyle.none, thumb);
        }

        // ---------------- fields (label + control + note), laid out in a grid like the web's .grid ----------------
        delegate float FieldFn(float x, float y, float w);
        readonly List<FieldFn> fields = new List<FieldFn>();
        void FSeg(string label, string[] opts, int cur, System.Action<int> set, string note = null) => AddF((x, y, w) =>
        {
            Lbl(x, y, w, 18, Sp(label.ToUpper()), sLab); int n = Seg(x, y + 24, w, opts, cur, out var h); if (n != cur && !measuring) set(n); float yy = y + 24 + h;
            if (note != null) { yy += 6; float nh = TextH(note, sNote, w); Lbl(x, yy, w, nh, note, sNote); yy += nh; } return yy - y;
        });
        void FSw(string label, bool v, System.Action<bool> set, string note = null) => AddF((x, y, w) =>
        {
            Lbl(x, y, w, 18, Sp(label.ToUpper()), sLab); var nv = Switch(x, y + 24, v); if (nv != v) set(nv); float yy = y + 24 + 24;
            if (note != null) { yy += 6; float nh = TextH(note, sNote, w); Lbl(x, yy, w, nh, note, sNote); yy += nh; } return yy - y;
        });
        void FSl(string label, float v, float a, float b, System.Func<float, string> fmt, System.Action<float> set, string note = null, float step = 0) => AddF((x, y, w) =>
        {
            Lbl(x, y, w * 0.7f, 18, Sp(label.ToUpper()), sLab); Lbl(x, y, w, 18, fmt(v), sVal); var nv = Slide(x, y + 22, w, v, a, b); if (step > 0) nv = Mathf.Round(nv / step) * step; if (Mathf.Abs(nv - v) > 1e-5f) set(nv); float yy = y + 22 + 22;
            if (note != null) { yy += 6; float nh = TextH(note, sNote, w); Lbl(x, yy, w, nh, note, sNote); yy += nh; } return yy - y;
        });
        void FCustom(FieldFn f) => AddF(f);
        // ---------------- sections: full-width headers that split a tab into labelled groups; click a header to fold it ----------------
        readonly HashSet<int> fullRow = new HashSet<int>(); static readonly HashSet<string> folded = new HashSet<string>(); bool inFolded;
        void AddF(FieldFn f) { if (!inFolded) fields.Add(f); }
        void ClearFields() { fields.Clear(); fullRow.Clear(); inFolded = false; }
        /// <summary>A full-width field (spans every column).</summary>
        void FFull(FieldFn f) { if (inFolded) return; fullRow.Add(fields.Count); fields.Add(f); }
        /// <summary>Start a section: a header row with a fold toggle; fields after it (until the next header) belong to it.</summary>
        void FHead(string title, string sub = null)
        {
            string key = mTab + "/" + title; bool shut = folded.Contains(key); inFolded = false;
            FFull((x, y, w) =>
            {
                float yy = y + 4; Box(x, yy + 28, w, 1, Line);
                Lbl(x, yy, w, 24, (shut ? "+  " : "–  ") + Sp(title.ToUpper(), 2), St(fDisp, 15, shut ? Dim : Color.white));
                if (sub != null) Lbl(x, yy + 4, w, 18, sub, St(fBody, 12, Dim, TextAnchor.MiddleRight));
                if (!measuring && GUI.Button(new UnityEngine.Rect(x, yy, w, 28), GUIContent.none, GUIStyle.none)) { if (shut) folded.Remove(key); else folded.Add(key); }
                return 34;
            });
            inFolded = shut;
        }
        /// <summary>Draw the collected fields in columns (min 250 px wide, gaps 14 / 26). Returns the height used.</summary>
        float Grid(float x, float y, float w, bool draw)
        {
            int cols = Mathf.Max(1, Mathf.FloorToInt((w + 26) / (250 + 26))); float cw = (w - 26 * (cols - 1)) / cols; float yy = y; int i = 0;
            if (gridFullFirst && fields.Count > 0) fullRow.Add(0);
            while (i < fields.Count)
            {
                if (fullRow.Contains(i)) { measuring = true; float h0 = fields[i](0, 0, w); measuring = false; if (draw) fields[i](x, yy, w); yy += h0 + 14; i++; continue; }
                int n = 0; while (n < cols && i + n < fields.Count && !fullRow.Contains(i + n)) n++;
                float rowH = 0; measuring = true; for (int c = 0; c < n; c++) rowH = Mathf.Max(rowH, fields[i + c](0, 0, cw)); measuring = false;
                if (draw) for (int c = 0; c < n; c++) fields[i + c](x + c * (cw + 26), yy, cw);
                yy += rowH + 14; i += n;
            }
            return yy - y;
        }

        // ---------------- the menu ----------------
        static readonly (string id, string name, string[] tabs, string[] tabNames)[] Groups =
        {
            ("gameplay", "GAMEPLAY", new[] { "mission", "enemies", "rules" }, new[] { "Mission", "Hostiles", "Rules" }),
            ("character", "CHARACTER", new[] { "loadout", "gunsmith", "weapons", "progress" }, new[] { "Loadout", "Gunsmith", "Weapons", "Progress" }),
            ("system", "SYSTEM", new[] { "graphics", "audio", "controls" }, new[] { "Graphics", "Audio", "Controls" }),
        };

        void DrawWebMenu(Game G, float SW, float SH)
        {
            MenuStyles(); uiS = Mathf.Clamp(SH / 900f, 0.7f, 2.4f); float W = SW / uiS, H = SH / uiS; var oldM = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(uiS, uiS, 1));
            GUI.DrawTexture(new UnityEngine.Rect(0, 0, W, H), scrimTex);
            float pad = W < 760 ? 18 : 56, x = pad, y = H < 520 ? 12 : 34, cw = Mathf.Min(1240, W - pad * 2);
            // header
            var mapName = MapGen.MapNames[Mathf.Max(0, System.Array.IndexOf(MapGen.Maps, G.settings.mapId))];
            var title = ("Breach Point: " + mapName).ToUpper(); GUI.Label(new UnityEngine.Rect(x, y, W - x * 2, 46), Sp(title), sTitle); y += 46;
            if (H >= 520 && W >= 760) { var desc = "Clear a procedurally built " + mapName.ToLower() + ". Every hostile carries a layered body: clothing, skin, muscle, skeleton, organs and major vessels. Rounds strip each layer they pass through and lose energy as they go. Blades cut exactly where they pass."; float dh = TextH(desc, sDesc, 620); GUI.Label(new UnityEngine.Rect(x, y + 8, 620, dh), desc, sDesc); y += 8 + dh; }
            // groups
            y += H < 520 ? 8 : 20; float gx = x;
            foreach (var g in Groups)
            {
                var label = Sp(g.name, 2); float gw = TextW(label, sGroup); bool on = mGroup == g.id; var old = sGroup.normal.textColor; sGroup.normal.textColor = on ? Color.white : new Color(1, 1, 1, 0.5f);
                var r = new UnityEngine.Rect(gx, y, gw, 30); GUI.Label(r, label, sGroup); sGroup.normal.textColor = old; if (on) Rect(gx, y + 40, gw, 3, Color.white);
                if (GUI.Button(new UnityEngine.Rect(gx, y, gw, 42), GUIContent.none, GUIStyle.none)) { mGroup = g.id; mTab = g.tabs[0]; mScroll = Vector2.zero; }
                gx += gw + 32;
            }
            y += 42; Rect(x, y, W - x * 2 + (pad - x), 1, Line); y += 16;
            // tabs
            float tx = x; foreach (var g in Groups) if (g.id == mGroup) for (int i = 0; i < g.tabs.Length; i++)
                    {
                        var label = Sp(g.tabNames[i].ToUpper(), 2); float tw = TextW(label, sTab) + 32; bool on = mTab == g.tabs[i];
                        if (FlatBtn(tx, y, tw, 32, label, sTab, on ? Color.white : new Color(1, 1, 1, 0.05f), on ? Color.white : new Color(1, 1, 1, 0.1f), on ? Ink : new Color(1, 1, 1, 0.75f))) { mTab = g.tabs[i]; mScroll = Vector2.zero; }
                        tx += tw + 4;
                    }
            y += 32 + 24;
            // footer
            float fh = 70, fy = H - fh; Rect(x, fy, W - x * 2, 1, Line);
            if (W >= 760) { float hx = x; foreach (var (k, t) in new[] { ("Esc", "BACK"), ("P", "DEPLOY") }) { float kw = Mathf.Max(24, TextW(k, sHint) + 12); Rect(hx, fy + 25, kw, 1, new Color(1, 1, 1, 0.5f)); Rect(hx, fy + 45, kw, 1, new Color(1, 1, 1, 0.5f)); Rect(hx, fy + 25, 1, 20, new Color(1, 1, 1, 0.5f)); Rect(hx + kw, fy + 25, 1, 21, new Color(1, 1, 1, 0.5f)); GUI.Label(new UnityEngine.Rect(hx, fy + 25, kw, 20), k, St(fBody, 11, Color.white, TextAnchor.MiddleCenter)); GUI.Label(new UnityEngine.Rect(hx + kw + 8, fy + 25, 80, 20), Sp(t, 2), sHint); hx += kw + 8 + TextW(Sp(t, 2), sHint) + 24; } }
            float bx = W - x; float dw = 190; bx -= dw;
            if (FlatBtn(bx, fy + 14, dw, 42, Sp("DEPLOY", 2), sBtn, Color.white, Hex(0xdcdcdc), Ink)) G.Deploy();
            float nw = TextW(Sp("NEW MAP", 2), sBtn) + 44; bx -= nw + 8; if (FlatBtn(bx, fy + 14, nw, 42, Sp("NEW MAP", 2), sBtn, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) { G.settings.seed = Random.Range(10000, 99999); G.NewMap(); G.Deploy(); }
            float rw = TextW(Sp("RESET HOSTILES", 2), sBtn) + 44; bx -= rw + 8; if (FlatBtn(bx, fy + 14, rw, 42, Sp("RESET HOSTILES", 2), sBtn, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) { G.ResetHostiles(); G.Deploy(); }
            // tab body (scrolls when it doesn't fit)
            float bodyH = fy - y - 8; ClearFields(); BuildTab(G); float contentH;
            if (mTab == "gunsmith" && cw >= 900) { GunsmithWide(G, x, y, cw, bodyH); if (GUI.changed) G.SaveSettings(); GUI.matrix = oldM; return; }
            if (mTab == "gunsmith") contentH = GunsmithStack(G, 0, 0, cw, false); else
            if (mTab == "loadout") contentH = LoadoutBody(G, 0, 0, cw, false); else if (mTab == "progress") contentH = ProgressBody(G, 0, 0, cw, false); else if (mTab == "controls") contentH = Grid(0, 0, cw, false) + ControlsBody(G, 0, 0, cw, false); else contentH = Grid(0, 0, cw, false);
            mScroll = GUI.BeginScrollView(new UnityEngine.Rect(x, y, cw + 18, bodyH), mScroll, new UnityEngine.Rect(0, 0, cw, Mathf.Max(bodyH - 2, contentH + 10)), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (mTab == "gunsmith") GunsmithStack(G, 0, 0, cw, true); else if (mTab == "loadout") LoadoutBody(G, 0, 0, cw, true); else if (mTab == "progress") ProgressBody(G, 0, 0, cw, true); else if (mTab == "controls") { float gh = Grid(0, 0, cw, true); ControlsBody(G, 0, gh + 6, cw, true); } else Grid(0, 0, cw, true);
            GUI.EndScrollView();
            if (GUI.changed) G.SaveSettings();
            GUI.matrix = oldM;
            if (rebind != null) DrawRebindOverlay(SW, SH);
        }

        static string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
        void BuildTab(Game G)
        {
            var S = G.settings; gridFullFirst = false;
            switch (mTab)
            {
                case "mission":
                    FHead("Mode", "what you're doing this round");
                    { var modes = new[] { "clear", "duel", "zombies", "hostage", "bomb" }; int mi0 = Mathf.Max(0, System.Array.IndexOf(modes, S.mode));
                      FSeg("Mode", new[] { "Clear the area", "1v1 duel", "Zombies", "Hostage rescue", "Bomb defuse" }, mi0, i => { S.mode = modes[i]; G.NewMap(); },
                        "Hostage rescue: hostages kneel around the map, each watched by a guard. A guard who spots you starts to execute their hostage: stop them fast (flashbangs and tasers work). Free hostages with F and they run for the exit. Bomb defuse: find the ticking bomb by its beeping and hold F next to it for 6 s before the timer runs out. Zombies: endless rounds of the dead. 1v1: you against one AI duelist in a compact arena."); }
                    FSl("Hostages", S.hostages, 1, 6, v => Mathf.RoundToInt(v).ToString(), v => S.hostages = Mathf.RoundToInt(v), "Hostage rescue. Applies on reset.", 1);
                    { float[] bt = { 120, 180, 300 }; int bi = System.Array.IndexOf(bt, S.bombTime); if (bi < 0) bi = 1; FSeg("Bomb timer", new[] { "2 min", "3 min", "5 min" }, bi, i => S.bombTime = bt[i], "Bomb defuse. Applies on reset."); }
                    { int[] dv = { 5, 10, 20, 0 }; int cur = System.Array.IndexOf(dv, S.duelTo); if (cur < 0) cur = 1; FSeg("1v1 win rule", new[] { "First to 5", "First to 10", "First to 20", "Endless" }, cur, i => S.duelTo = dv[i]); }
                    FHead("Map", "where, and how it's laid out");
                    { int mi = Mathf.Max(0, System.Array.IndexOf(MapGen.Maps, S.mapId)); FSeg("Map", MapGen.MapNames, mi, i => { S.mapId = MapGen.Maps[i]; G.NewMap(); },
                        "Rebuilds the map now. Neon district: rainy night market. Fantasy keep: castle at dusk with dragons hunting mages overhead. Bosch's Hell: the hell panel of the 1500s painting, Tree-Man and all. Baba Yaga's forest: night birches, a skull fence, huts on chicken legs and the witch in her mortar (cut a hut's leg and it comes down). Yggdrasil Station: a Norse moon colony under a black hole, the World Tree in the sky, bright longhalls, delivery robots, vending machines and flying cars."); }
                    FCustom((x, y, w) =>
                    {
                        Lbl(x, y, w, 18, Sp("LAYOUT SEED"), sLab); Box(x, y + 24, 130, 31, new Color(1, 1, 1, 0.06f)); Box(x, y + 54, 130, 1, new Color(1, 1, 1, 0.3f));
                        if (!measuring) { var s = GUI.TextField(new UnityEngine.Rect(x + 6, y + 28, 120, 24), S.seed.ToString(), St(fBody, 13, Color.white)); if (int.TryParse(s, out var v) && v != S.seed) S.seed = Mathf.Abs(v); }
                        if (FlatBtn(x + 138, y + 24, 92, 31, Sp("SHUFFLE", 2), St(fDisp, 15, Color.white, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) S.seed = Random.Range(10000, 99999);
                        return 24 + 31;
                    });
                    FSeg("Cover density", new[] { "Sparse", "Normal", "Dense" }, S.dens, i => S.dens = i, "Applies with New map.");
                    FSeg("Lighting", new[] { "Dim", "Normal", "Bright" }, S.light, i => S.light = i, "Applies with New map. F toggles your weapon light.");
                    break;
                case "enemies":
                    FHead("Hostiles", "who, how many, how they act");
                    FSeg("Hostile type", new[] { "Humans", "Zombies" }, S.kind == 3 ? 1 : 0, i => S.kind = i == 1 ? 3 : 0, "Applies on reset.");
                    FSw("Teammate bot", S.bot, v => { S.bot = v; G.ResetHostiles(); }, "An AI soldier on your side: follows you, shoots hostiles it can see. Enemies shoot at it too. Not in duels.");
                    FSeg("Teammate look", new[] { "Ghost", "Operator" }, S.botLook, i => { S.botLook = i; if (S.bot) G.ResetHostiles(); }, "Ghost: translucent blue, like Wraith Form. It can still be shot.");
                    FSl("Hostile count", S.count, 1, 24, v => Mathf.RoundToInt(v).ToString(), v => S.count = Mathf.RoundToInt(v), "Applies on reset.", 1);
                    FSeg("Behaviour", new[] { "Engage", "Hold fire", "Mannequin" }, S.behavior, i => S.behavior = i, "Mannequins stand still for anatomy testing.");
                    FHead("Difficulty", "preset, or tune each skill");
                    FSeg("Difficulty", new[] { "Recruit", "Regular", "Veteran", "Elite", "Custom" }, S.difficulty, i => G.ApplyDifficulty(i), "Sets the AI sliders below. Elite hostiles also flank. Moving any slider switches to Custom.");
                    FSl("Accuracy", S.accuracy, 0, 1, Pct, v => { S.accuracy = v; S.difficulty = 4; }, null, 0.05f);
                    FSl("Reaction time", S.reaction, 0.1f, 2, v => v.ToString("0.00") + " s", v => { S.reaction = v; S.difficulty = 4; }, null, 0.05f);
                    FSl("Movement speed", S.speed, 0.4f, 1.8f, v => v.ToString("0.00") + "×", v => { S.speed = v; S.difficulty = 4; }, null, 0.05f);
                    FSl("Aggression", S.aggression, 0, 1, Pct, v => { S.aggression = v; S.difficulty = 4; }, "Chance to push your position instead of holding.", 0.05f);
                    FSl("Pain tolerance", S.painTol, 0.3f, 3, v => v.ToString("0.00") + "×", v => { S.painTol = v; S.difficulty = 4; }, "How much trauma before going down from shock.", 0.05f);
                    FSl("Nerve", S.nerve, 0.4f, 2, v => v.ToString("0.00") + "×", v => { S.nerve = v; S.difficulty = 4; }, "Applies on reset. Higher nerve means fewer surrenders.", 0.05f);
                    FSw("Flanking", S.flank, v => S.flank = v, "Pushing hostiles split up and come at you from the side.");
                    FHead("Body", "blood, armour, helmets");
                    FSl("Blood volume", S.blood, 3, 7, v => v.ToString("0.0") + " L", v => S.blood = v, "Applies on reset. Collapse at 36% loss, death at 56%.", 0.1f);
                    FSeg("Body armour", new[] { "None", "Soft vest", "Plate carrier" }, S.armor, i => S.armor = i, "Applies on reset. Soft stops pistol and SMG rounds; plates stop everything here.");
                    FSw("Helmets", S.helmet, v => S.helmet = v, "Applies on reset.");
                    break;
                case "rules":
                    FHead("Player", "time, damage, ammo");
                    FSl("Slow motion speed", S.slowmo, 0.05f, 0.8f, Pct, v => S.slowmo = v, "Press T in game to toggle.", 0.05f);
                    FSw("Invulnerable", S.god, v => S.god = v); FSw("Infinite reserve ammo", S.infinite, v => S.infinite = v);
                    FHead("Screen", "what the HUD shows");
                    FSw("Fullscreen", Screen.fullScreen, v => Screen.fullScreen = v, "Fills the whole screen. On the web, F11 also works.");
                    FSw("Tracers", S.tracers, v => S.tracers = v);
                    FSw("Hit location readout", S.hitText, v => S.hitText = v, "Shows what each shot hit (bone, organ, limb) next to the crosshair.");
                    FSw("Kill feed", S.killFeed, v => S.killFeed = v, "Text messages for kills, take-downs and events in the corner of the screen.");
                    FSw("Hostile condition panel", S.inspect, v => S.inspect = v, "Aim at a hostile within 8 m to see their condition and wound list.");
                    FSw("Final takedown camera", S.killCam, v => S.killCam = v, "Cuts to the last hostile going down, in slow motion.");
                    FHead("Blades", "katana, sickle, slash projector");
                    FSw("Blades cut the environment", S.cutEnv, v => S.cutEnv = v, "Katana and sickle cuts also split furniture, crates, shelves, pillars and other objects up to 4.5 m across. The pieces fall with physics and can be cut again. Floors, outer walls and doors stay.");
                    FSeg("Slice angle", new[] { "Random", "Blade Mode" }, S.sliceMode, i => S.sliceMode = i, "Random: every swing cuts at a random angle. Blade Mode: hold aim with the katana, sickle or Slash Projector to slow time, move the mouse or right stick to angle the cut, and click to cut exactly along the line.");
                    break;
                case "weapons": BuildWeapons(G); break;
                case "graphics":
                    FSeg("Preset", new[] { "Low", "Medium", "High", "Ultra" }, S.quality, i => { S.quality = i; G.ApplyQuality(); }, "Render resolution, shadow distance and anti-aliasing. Fantasy keep: more dragons and mages on higher presets.");
                    FSl("Field of view", S.fov, 60, 100, v => Mathf.RoundToInt(v) + "°", v => S.fov = v, null, 1);
                    break;
                case "audio":
                    FHead("Volume");
                    FSl("Master volume", S.master, 0, 1, Pct, v => { S.master = v; AudioListener.volume = v; }, null, 0.05f);
                    FHead("Music");
                    FSw("Music", S.music, v => S.music = v, "Procedural EDM. It builds up while hostiles are hunting you.");
                    FSl("Music volume", S.musicVol, 0, 1, Pct, v => S.musicVol = v, null, 0.05f);
                    break;
                case "controls":
                    FHead("Aiming");
                    FSw("Weapon laser instead of crosshair", S.aimLaser, v => S.aimLaser = v, "Hides the crosshair; a red dot marks where your shots land.");
                    FSl("Mouse sensitivity", S.sens, 0.2f, 3, v => v.ToString("0.00") + "×", v => S.sens = v, null, 0.05f);
                    FHead("Input");
                    FSeg("Touch controls", new[] { "Auto", "On", "Off" }, S.touch, i => S.touch = i, "Auto shows them once you touch the screen. On a phone, play in landscape.");
                    FSl("Touch look sensitivity", S.touchSensPct, 40, 250, v => Mathf.RoundToInt(v) + "%", v => S.touchSensPct = Mathf.RoundToInt(v), "How far a drag turns the view. Small drags are slowed for precise aim, quick swipes are sped up.", 5);
                    FCustom((x, y, w) =>
                    {
                        Lbl(x, y, w, 18, Sp("TOUCH BUTTON LAYOUT"), sLab); var bs = St(fDisp, 13, Color.white, TextAnchor.MiddleCenter);
                        if (FlatBtn(x, y + 24, 230, 34, Sp("EDIT LAYOUT", 2), bs, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) TouchUI.Editing = true;
                        float yy = y + 24 + 34 + 6; var note = "Drag buttons where you want them, resize them, or make them invisible (they still work). Reset puts everything back."; float nh = TextH(note, sNote, w); Lbl(x, yy, w, nh, note, sNote); return yy + nh - y;
                    });
                    break;
            }
        }

        // ---------------- weapons tab: tune any weapon, like the web ----------------
        static readonly Dictionary<string, float[]> wDefaults = new Dictionary<string, float[]>();
        void BuildWeapons(Game G)
        {
            var list = WeaponDef.All.FindAll(Offered); wSel = Mathf.Clamp(wSel, 0, list.Count - 1); var w = WeaponDef.Get(list[wSel].id);
            if (!wDefaults.ContainsKey(w.id)) wDefaults[w.id] = new[] { w.energy, w.rpm, w.mag, w.spread, w.adsSpread, w.wound, w.recoil, w.pellets, w.auto ? 1 : 0 };
            var D = wDefaults[w.id]; var names = new string[list.Count]; for (int i = 0; i < list.Count; i++) names[i] = list[i].name;
            FFull((x, y, ww) => { float h = GroupedPicker(x, y, ww, list, wSel, out int n); if (n != wSel && !measuring) wSel = n; float yy = y + h + 10; var am = w.Ammo; var spec = (w.desc.Length > 0 ? w.desc + " " : "") + (am != null ? am.name + " " + am.type + (w.pellets > 1 ? " ×" + w.pellets : "") + ": " + (am.mass * 1000).ToString("0.0") + " g at " + Mathf.RoundToInt(am.V0(w.energy)) + " m/s, " + Mathf.RoundToInt(w.energy) + " J, about " + Mathf.RoundToInt(am.Budget(am.V0(w.energy))) + " cm of tissue penetration" + (am.type == Bullet.JHP ? "; expands, so it usually stays in a torso" : am.fragV < 9999 ? "; tumbles and fragments while fast" : am.type == Bullet.Buck ? " per pellet" : "") + "." : (w.desc.Length > 0 ? "" : w.cal + ". Muzzle energy " + Mathf.RoundToInt(w.energy) + " J.")); float sh = TextH(spec, sSmallBody, ww); Lbl(x, yy, ww, sh, spec, sSmallBody); return yy + sh + 8 - y; });
            int start = fields.Count; var wc = G.player.weapons; bool gun = w.kind == "gun" || w.kind == "eldritch" || w.kind == "bow" || w.kind == "melee";
            if (gun)
            {
                FHead("Tune " + w.name, "changes apply right away");
                FSl("Muzzle energy", w.energy / Mathf.Max(1, D[0]), 0.25f, 3, v => v.ToString("0.00") + "×", v => w.energy = D[0] * v, "Drives penetration and wound size.", 0.05f);
                if (w.kind != "melee" && w.kind != "bow") { FSl("Rate of fire", w.rpm, 30, 1200, v => Mathf.RoundToInt(v) + " rpm", v => w.rpm = v, null, 10); FSl("Magazine size", w.mag, 1, 100, v => Mathf.RoundToInt(v).ToString(), v => { w.mag = Mathf.RoundToInt(v); wc.Refill(); }, null, 1); }
                if (w.kind != "melee") { FSl("Hip-fire spread", w.spread, 0, 0.08f, v => (v * 57.2958f).ToString("0.00") + "°", v => w.spread = v, null, 0.001f); FSl("Aimed spread", w.adsSpread, 0, 0.04f, v => (v * 57.2958f).ToString("0.00") + "°", v => w.adsSpread = v, null, 0.0005f); }
                FSl("Wound size", w.wound / Mathf.Max(1e-4f, D[5]), 0.5f, 2.5f, v => v.ToString("0.00") + "×", v => w.wound = D[5] * v, "Size of each torn patch per layer.", 0.05f);
                if (w.kind == "gun") FSl("Recoil", w.recoil, 0, 0.15f, v => v.ToString("0.000"), v => w.recoil = v, null, 0.002f);
                if (w.id == "shotgun") FSl("Pellets per shell", w.pellets, 1, 20, v => Mathf.RoundToInt(v).ToString(), v => w.pellets = Mathf.RoundToInt(v), null, 1);
                if (w.kind == "gun") FSw("Full auto", w.auto, v => w.auto = v);
                if (w.kind == "bow") FSw("Super shot", wc.superShot, v => wc.superShot = v, "Arrows hurl whoever they hit along the flight path and pin them to the first surface behind. Press J to toggle in game.");
            }
            FCustom((x, y, ww) => { if (FlatBtn(x, y + 6, TextW(Sp("RESTORE DEFAULTS", 2), St(fDisp, 16, Color.white)) + 36, 38, Sp("RESTORE DEFAULTS", 2), St(fDisp, 16, Color.white, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) { w.energy = D[0]; w.rpm = D[1]; w.mag = (int)D[2]; w.spread = D[3]; w.adsSpread = D[4]; w.wound = D[5]; w.recoil = D[6]; w.pellets = (int)D[7]; w.auto = D[8] > 0; wc.Refill(); } return 48; });
            gridFullFirst = false;
        }
        bool gridFullFirst;
        /// <summary>Weapon categories, in display order.</summary>
        public static readonly string[] Cats = { "Pistols", "SMGs", "Rifles", "Shotguns", "Bows & thrown", "Less-lethal", "Special", "Melee", "Eldritch", "Psychic", "Wands" };
        /// <summary>Weapon classes, in display order; each category belongs to one.</summary>
        public static readonly string[] Classes = { "Gunner", "Melee", "Magic" };
        public static string ClassOf(string cat) => cat == "Melee" ? "Melee" : cat == "Eldritch" || cat == "Psychic" || cat == "Wands" ? "Magic" : "Gunner";
        public static string Cat(WeaponDef d)
        {
            switch (d.id) { case "pistol": case "revolver": return "Pistols"; case "smg": return "SMGs"; case "rifle": case "dmr": return "Rifles"; case "shotgun": return "Shotguns"; }
            switch (d.kind) { case "melee": return "Melee"; case "bow": case "throw": return "Bows & thrown"; case "taser": case "beanbag": return "Less-lethal"; case "eldritch": return "Eldritch"; case "psi": return "Psychic"; case "wand": return "Wands"; default: return "Special"; }
        }
        /// <summary>Weapon chooser grouped by category: a label column on the left, the weapons as chips beside it. Returns the height; sel = the clicked index in list.</summary>
        float GroupedPicker(float x, float y, float w, List<WeaponDef> list, int cur, out int sel)
        {
            sel = cur; float yy = y; float lw = w < 520 ? 0 : 118; var cs = St(fBodyM, 13, Color.white, TextAnchor.MiddleCenter); string cls = null;
            foreach (var cat in Cats)
            {
                var idx = new List<int>(); for (int i = 0; i < list.Count; i++) if (Cat(list[i]) == cat) idx.Add(i); if (idx.Count == 0) continue;
                if (ClassOf(cat) != cls) { cls = ClassOf(cat); if (yy > y) yy += 6; Lbl(x, yy, w, 22, Sp(cls.ToUpper(), 2), St(fDisp, 16, Color.white)); Box(x, yy + 24, w, 2, new Color(1, 1, 1, 0.22f)); yy += 32; }
                if (lw > 0) Lbl(x, yy + 6, lw, 18, Sp(cat.ToUpper()), St(fBodyS, 11, Dim)); else { Lbl(x, yy, w, 16, Sp(cat.ToUpper()), St(fBodyS, 11, Dim)); yy += 20; }
                float cx = x + lw, rowY = yy;
                foreach (var i in idx)
                {
                    var name = WeaponDef.Get(list[i].id).name; float bw = TextW(name, cs) + 22; if (cx > x + lw && cx + bw > x + w) { cx = x + lw; rowY += 32; }
                    bool on = i == cur; if (FlatBtn(cx, rowY, bw, 28, name, cs, on ? Color.white : new Color(1, 1, 1, 0.06f), on ? Color.white : new Color(1, 1, 1, 0.13f), on ? Ink : Hex(0xcfd2d5))) sel = i;
                    cx += bw + 4;
                }
                yy = rowY + 28 + 8; if (lw > 0) Box(x, yy - 4, w, 1, new Color(1, 1, 1, 0.05f));
            }
            return yy - y;
        }

        // ---------------- loadout: slots and weapon cards ----------------
        float LoadoutBody(Game G, float x, float y, float w, bool draw)
        {
            measuring = !draw; var S = G.settings; var wc = G.player.weapons; float y0 = y;
            var intro = "Carry any 4 weapons. Kills earn 1 token each; spend tokens to unlock more. You have " + Progress.tokens + " token" + (Progress.tokens == 1 ? "" : "s") + ".";
            Lbl(x, y, w, 20, intro, St(fBody, 14, Dim)); y += 30;
            float sx = x; for (int i = 0; i < 4; i++)
            {
                string id = i < S.loadout.Length ? S.loadout[i] : null; var d = id != null ? WeaponDef.Get(id) : null; float sw = Mathf.Max(120, TextW(d != null ? d.name.ToUpper() : "EMPTY", St(fDisp, 17, Color.white)) + 24);
                Box(sx, y, sw, 46, new Color(1, 1, 1, 0.05f)); Box(sx, y + 44, sw, 2, id != null ? Color.white : new Color(1, 1, 1, 0.18f));
                Lbl(sx + 12, y + 4, sw, 14, Sp("SLOT " + (i + 1), 2), St(fBody, 10, Dim)); Lbl(sx + 12, y + 18, sw, 24, d != null ? d.name.ToUpper() : "EMPTY", St(fDisp, 17, Color.white)); sx += sw + 8;
            }
            y += 46 + 14;
            // body armor: four tiers, heavier = more protection and slower
            Lbl(x, y, w, 18, Sp("BODY ARMOR", 2), St(fBody, 11, Dim)); y += 22;
            { float ax = x, aw = Mathf.Max(110, (w - 8 * 3) / 4); if (aw * 4 + 24 > w) aw = (w - 24) / 4; var ast = St(fDisp, 13, Color.white, TextAnchor.MiddleCenter);
              for (int i = 0; i < 4; i++) { bool on = S.playerArmor == i; if (FlatBtn(ax, y, aw, 36, Player.ArmorNames[i].ToUpper(), ast, on ? new Color(1, 1, 1, 0.22f) : new Color(1, 1, 1, 0.05f), new Color(1, 1, 1, 0.14f), Color.white)) { S.playerArmor = i; G.SaveSettings(); } if (on) Box(ax, y + 34, aw, 2, Color.white); ax += aw + 8; } }
            y += 42; { var info = Player.ArmorInfo[Mathf.Clamp(S.playerArmor, 0, 3)]; float ih = TextH(info, St(fBody, 13, Dim), w); Lbl(x, y, w, ih, info, St(fBody, 13, Dim)); y += ih + 18; }
            var all0 = WeaponDef.All.FindAll(Offered); int cols = Mathf.Max(1, Mathf.FloorToInt((w + 10) / 200f)); float cw = (w - 10 * (cols - 1)) / cols;
            // weapons grouped by type, each group under its own header
            string cls = null;
            foreach (var cat in Cats)
            {
            var all = all0.FindAll(q => Cat(WeaponDef.Get(q.id)) == cat); if (all.Count == 0) continue; float ch = 0;
            // class header (Gunner / Melee / Magic) above its categories
            if (ClassOf(cat) != cls) { cls = ClassOf(cat); var c0 = cls; int n = all0.FindAll(q => ClassOf(Cat(WeaponDef.Get(q.id))) == c0).Count; Lbl(x, y + 6, w, 30, Sp(cls.ToUpper(), 3), St(fDisp, 22, Color.white)); Lbl(x, y + 10, w, 20, n + (n == 1 ? " weapon" : " weapons"), St(fBody, 13, Dim, TextAnchor.MiddleRight)); Box(x, y + 40, w, 2, new Color(1, 1, 1, 0.3f)); y += 52; }
            Lbl(x, y + 4, w, 22, Sp(cat.ToUpper(), 2), St(fDisp, 15, Color.white)); Lbl(x, y + 6, w, 18, all.Count + (all.Count == 1 ? " weapon" : " weapons"), St(fBody, 12, Dim, TextAnchor.MiddleRight)); Box(x, y + 30, w, 1, Line); y += 40;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i]; var d = WeaponDef.Get(b.id); int col = i % cols; if (col == 0 && i > 0) y += ch + 10; if (col == 0) ch = 0;
                float cx = x + col * (cw + 10); bool own = Progress.Owned(b.id); int eq = System.Array.IndexOf(S.loadout, b.id); bool hasV = WeaponDef.Variants.TryGetValue(b.id, out var vs) && own;
                float h = 12 + 22 + 16 + (hasV ? 30 : 0) + 38 + 12; ch = Mathf.Max(ch, h);
                Box(cx, y, cw, h, eq >= 0 ? new Color(1, 1, 1, 0.1f) : new Color(1, 1, 1, 0.05f)); if (eq >= 0) Box(cx, y, 3, h, Color.white);
                var a0 = GUI.color; if (!own && draw) GUI.color = new Color(1, 1, 1, 0.6f);
                Lbl(cx + 14, y + 10, cw - 20, 24, d.name.ToUpper(), sCardH); Lbl(cx + 14, y + 34, cw - 20, 16, Sp(((d.kind == "gun" ? "Firearm" : d.kind == "melee" ? "Melee" : d.kind == "psi" ? "Psychic" : d.kind == "eldritch" ? "Eldritch" : d.kind == "bow" ? "Bow" : d.kind == "flame" ? "Flamethrower" : d.kind == "throw" ? "Grenade" : d.kind == "wand" ? "Wand" : d.kind == "taser" || d.kind == "beanbag" ? "Less-lethal" : "Experimental") + " · " + d.cal).ToUpper()), sCardMeta);
                float by = y + 54;
                if (hasV)
                {
                    float vx = cx + 14; foreach (var v in vs)
                    {
                        float vw = TextW(v.key, St(fBodyM, 12, Color.white)) + 16; if (vx + vw > cx + cw - 10) break; bool on = v.vid == d.vid;
                        if (FlatBtn(vx, by, vw, 24, v.key, St(fBodyM, 12, Color.white, TextAnchor.MiddleCenter), on ? Color.white : new Color(1, 1, 1, 0.07f), on ? Color.white : new Color(1, 1, 1, 0.14f), on ? Ink : Hex(0xcfd2d5))) { S.SetVariant(b.id, v.vid); WeaponDef.SetVariant(b.id, v.vid); wc.Rebuild(b.id); wc.Refill(); G.SaveSettings(); }
                        vx += vw + 4;
                    }
                    by += 30;
                }
                string act; bool en = true; int cost = Progress.Cost.TryGetValue(b.id, out var cv) ? cv : 0;
                if (eq >= 0) act = "REMOVE (SLOT " + (eq + 1) + ")"; else if (own) { act = S.loadout.Length >= 4 ? "LOADOUT FULL" : "EQUIP"; en = S.loadout.Length < 4; } else if (b.id == "wand") { act = "LEARN POLYMORPH (ANIMANCER)"; en = false; } else { act = "UNLOCK: " + cost + " TOKEN" + (cost == 1 ? "" : "S"); en = Progress.tokens >= cost; }
                var bs = St(fDisp, 13, Color.white, TextAnchor.MiddleCenter); float bw = TextW(Sp(act, 2), bs) + 24;
                if (FlatBtn(cx + 14, by, bw, 30, Sp(act, 2), bs, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white, en))
                {
                    var L = new List<string>(S.loadout);
                    if (eq >= 0) { if (L.Count > 1) L.RemoveAt(eq); else G.Feed("Keep at least one weapon"); }
                    else if (own) { if (L.Count < 4) L.Add(b.id); }
                    else Progress.Unlock(b.id);
                    S.loadout = L.ToArray(); wc.LoadLoadout(); wc.Select(wc.loadout[0], true); G.SaveSettings();
                }
                if (draw) GUI.color = a0;
            }
            y += ch + 22;
            }
            measuring = false; return y - y0;
        }

        // ---------------- progress: level, tokens, skill trees ----------------
        float ProgressBody(Game G, float x, float y, float w, bool draw)
        {
            measuring = !draw; float y0 = y; int need = Progress.XpNeed(Progress.level);
            float hx = x;
            void Head(string big, string small, bool bar = false) { Lbl(hx, y, 220, 30, big, sBig); Lbl(hx, y + 30, 220, 16, small, St(fBody, 13, Dim)); if (bar) { Box(hx, y + 50, 190, 6, new Color(1, 1, 1, 0.14f)); Box(hx, y + 50, 190 * Progress.xp / (float)need, 6, Color.white); } hx += Mathf.Max(TextW(big, sBig), TextW(small, St(fBody, 13, Dim))) + 26; }
            Head("Level " + Progress.level, Progress.xp.ToString("N0") + " / " + need.ToString("N0") + " points", true); Head(Progress.sp.ToString(), "skill point" + (Progress.sp == 1 ? "" : "s")); Head(Progress.tokens.ToString(), "token" + (Progress.tokens == 1 ? "" : "s")); Head(Progress.mission.ToString("N0"), "points this mission");
            y += 66;
            var info = "Kill 100 · headshot +50 · dismember +25 · Dead Eye +50 · pinned +75 · cut in half +100 · over 25 m +50 · each extra kill within 3 s +100 · arrest 150. Each level needs 500 more points and gives 1 skill point. Progress and settings are saved on this device.";
            float ih = TextH(info, sSmallBody, w - 190); Lbl(x, y, w - 190, ih, info, sSmallBody);
            bool armed = Time.unscaledTime - resetArm < 3; var rs = St(fDisp, 12, Color.white, TextAnchor.MiddleCenter);
            if (FlatBtn(x + w - 180, y, 180, 28, Sp(armed ? "TAP AGAIN TO ERASE" : "RESET PROGRESS", 2), rs, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) { if (armed) { Progress.ResetAll(); resetArm = -9; } else resetArm = Time.unscaledTime; }
            y += Mathf.Max(ih, 28) + 14;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w + 14) / (240 + 14))); float cw = (w - 14 * (cols - 1)) / cols; float rowH = 0;
            for (int ti = 0; ti < Progress.Trees.Length; ti++)
            {
                var t = Progress.Trees[ti]; int col = ti % cols; if (col == 0 && ti > 0) { y += rowH + 14; rowH = 0; } float cx = x + col * (cw + 14);
                int spent = Progress.Spent(t); float h = 10 + 26 + 18 + 6; foreach (var s in t.skills) h += 8 + 22 + TextH(s.d, sNote, cw - 24) + 10; h += 4; rowH = Mathf.Max(rowH, h);
                Box(cx, y, cw, h, new Color(1, 1, 1, 0.05f)); Lbl(cx + 12, y + 10, cw - 24, 26, Sp(t.name.ToUpper()), St(fDisp, 21, Color.white)); Lbl(cx + 12, y + 36, cw - 24, 18, t.tag + " · " + spent + " point" + (spent == 1 ? "" : "s") + " spent", St(fBody, 12, Dim));
                float sy = y + 60;
                for (int i = 0; i < t.skills.Length; i++)
                {
                    var s = t.skills[i]; int r = Progress.Skill(s.id); int gate = Progress.TreeGate[i]; bool open = spent >= gate; bool can = Progress.CanLearn(t, i);
                    Box(cx + 12, sy, cw - 24, 1, Line); sy += 8; Lbl(cx + 12, sy, cw - 120, 18, s.name, St(fBodyS, 14, Color.white));
                    for (int k = 0; k < 3; k++) { Box(cx + 12 + k * 13, sy + 20, 10, 10, k < r ? Color.white : new Color(1, 1, 1, 0.4f)); if (k >= r) Box(cx + 13 + k * 13, sy + 21, 8, 8, new Color(0.09f, 0.11f, 0.13f)); }
                    var lb = r >= 3 ? "MAXED" : open ? "LEARN" : "NEEDS " + gate + " IN TREE"; var bs = St(fDisp, 13, Color.white, TextAnchor.MiddleCenter); float bw = TextW(Sp(lb, 2), bs) + 20;
                    if (FlatBtn(cx + cw - 12 - bw, sy, bw, 28, Sp(lb, 2), bs, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white, can)) Progress.Learn(t, i);
                    sy += 32; float dh = TextH(s.d, sNote, cw - 24); Lbl(cx + 12, sy, cw - 24, dh, s.d, sNote); sy += dh + 10;
                }
            }
            y += rowH + 10; measuring = false; return y - y0;
        }

        // ---------------- controls: rebinding, inside the Controls tab ----------------
        float ControlsBody(Game G, float x, float y, float w, bool draw)
        {
            measuring = !draw; var S = G.settings; float y0 = y; string group = ctab == 0 ? BPInput.KB : BPInput.PAD;
            int nt = Seg(x, y, w, new[] { "Keyboard & mouse", "Controller" }, ctab, out var th); if (nt != ctab && draw) ctab = nt;
            var rb = St(fDisp, 14, Color.white, TextAnchor.MiddleCenter); float rw = TextW(Sp("RESET TO DEFAULTS", 2), rb) + 28; if (FlatBtn(x + w - rw, y, rw, 31, Sp("RESET TO DEFAULTS", 2), rb, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) BPInput.ResetGroup(group);
            y += th + 14;
            if (ctab == 1)
            {
                float cw2 = (w - 26) / 2;
                void Pad(float px, string label, float v, float a, float b, string val, System.Action<float> set) { Lbl(px, y, cw2 * 0.7f, 18, Sp(label.ToUpper()), sLab); Lbl(px, y, cw2, 18, val, sVal); var nv = Slide(px, y + 22, cw2, v, a, b); if (draw && Mathf.Abs(nv - v) > 1e-5f) set(nv); }
                Pad(x, "Look sensitivity", S.padSens, 0.2f, 3, S.padSens.ToString("0.00"), v => S.padSens = v); Pad(x + cw2 + 26, "Aiming sensitivity", S.padAdsMul, 0.2f, 1, Pct(S.padAdsMul), v => S.padAdsMul = v); y += 56;
                Pad(x, "Stick dead zone", S.padDeadzone, 0.03f, 0.4f, Pct(S.padDeadzone), v => S.padDeadzone = v); Pad(x + cw2 + 26, "Response curve", S.padCurve, 1, 3, S.padCurve.ToString("0.0") + (S.padCurve < 1.3f ? " linear" : S.padCurve < 2.3f ? " classic" : " precise"), v => S.padCurve = v); y += 56;
                Lbl(x, y, 200, 18, Sp("INVERT Y"), sLab); var ni = Switch(x, y + 22, S.padInvert); if (draw) S.padInvert = ni; y += 58;
            }
            else { Lbl(x, y, 200, 18, Sp("INVERT Y"), sLab); var ni = Switch(x, y + 22, S.mouseInvert); if (draw) S.mouseInvert = ni; y += 58; }
            Lbl(x, y, w, 18, "Click a binding, then press the new key or button (Esc cancels). × clears it.", St(fBody, 13, Dim)); y += 26;
            string last = null;
            foreach (var (a, label) in BPInput.Listed)
                foreach (var i in BPInput.Indices(a, group))
                {
                    var b = a.bindings[i]; string l = b.isPartOfComposite ? label + " " + b.name.ToLower() : label;
                    Box(x, y, w, 32, new Color(1, 1, 1, 0.03f)); Box(x, y + 31, w, 1, new Color(1, 1, 1, 0.06f)); Lbl(x + 10, y, w * 0.42f, 32, l == last ? "" : l, St(fBodyM, 13, Text)); last = l;
                    var disp = BPInput.Display(a, i); float kx = x + w * 0.44f;
                    if (BPInput.Rebindable(a, i))
                    {
                        if (FlatBtn(kx, y + 4, 220, 24, disp, St(fBodyM, 12, Color.white, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.16f), Color.white)) { rebindLabel = l; rebind = BPInput.Rebind(a, i, group, () => rebind = null); }
                        if (FlatBtn(kx + 226, y + 4, 26, 24, "×", St(fBodyM, 13, Color.white, TextAnchor.MiddleCenter), new Color(1, 1, 1, 0.05f), new Color(1, 1, 1, 0.14f), Color.white)) BPInput.Clear(a, i);
                        var c = BPInput.Conflict(a, i); if (c != null) Lbl(kx + 262, y, 200, 32, "also " + c, St(fBody, 12, new Color(1f, 0.7f, 0.3f)));
                    }
                    else Lbl(kx + 6, y, 260, 32, disp + " (fixed)", St(fBody, 12, Dim));
                    y += 32;
                }
            measuring = false; return y - y0 + 10;
        }
        void DrawRebindOverlay(float W, float H)
        {
            Rect(0, 0, W, H, new Color(0, 0, 0, 0.7f)); var st = St(fDisp, Mathf.RoundToInt(26 * uiS), Color.white, TextAnchor.MiddleCenter);
            GUI.Label(new UnityEngine.Rect(0, H / 2 - 30 * uiS, W, 40 * uiS), Sp(("Press a " + (ctab == 0 ? "key or mouse button" : "controller button") + " for " + rebindLabel).ToUpper()), st);
            GUI.Label(new UnityEngine.Rect(0, H / 2 + 14 * uiS, W, 24 * uiS), "Esc to cancel", St(fBody, Mathf.RoundToInt(13 * uiS), Dim, TextAnchor.MiddleCenter));
        }

        // ---------------- end screen: small panel, white top edge ----------------
        void DrawWebEnd(Game G, float SW, float SH)
        {
            MenuStyles(); uiS = Mathf.Clamp(SH / 900f, 0.7f, 2.4f); float W = SW / uiS, H = SH / uiS; var oldM = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(uiS, uiS, 1));
            Rect(0, 0, W, H, new Color(4 / 255f, 5 / 255f, 6 / 255f, 0.55f)); float pw = Mathf.Min(520, W - 32), ph = 360, px = (W - pw) / 2, py = (H - ph) / 2;
            Rect(px, py, pw, ph, new Color(10 / 255f, 12 / 255f, 14 / 255f, 0.95f)); Rect(px, py, pw, 3, Color.white);
            string t = G.Duel ? (G.Won ? "Duel won " : "Duel lost ") + G.duelMe + " – " + G.duelThem : G.settings.mode == "zombies" ? "You died on round " + G.zm.round
                : G.settings.mode == "bomb" ? (G.bm.defused ? "Bomb defused" : G.bm.exploded ? "The bomb went off" : "You were killed")
                : G.settings.mode == "hostage" ? (G.Won ? "Hostages saved " + G.hm.Saved + " / " + G.hm.total : G.player.alive ? "Hostages lost" : "You were killed")
                : G.Won ? "Area secure" : "You were killed";
            GUI.Label(new UnityEngine.Rect(px + 28, py + 24, pw - 56, 40), Sp(t.ToUpper()), St(fDispB, 34, Color.white));
            GUI.Label(new UnityEngine.Rect(px + 28, py + 66, pw - 56, 20), "+" + Progress.mission.ToString("N0") + " points · +" + Progress.missionTokens + " tokens · Level " + Progress.level, St(fBody, 13, Dim));
            var s = G.stats; float acc = s.shots > 0 ? s.hits * 100f / s.shots : 0; float sy = py + 100;
            foreach (var (k, v) in new[] { ("Killed", s.kills.ToString()), ("Down, alive", s.downs.ToString()), ("Arrests", s.arrests.ToString()), ("Shots / hits", s.shots + " / " + s.hits + " (" + acc.ToString("0") + "%)"), ("Organ and vessel hits", s.organs.ToString()), ("Brain hits", s.heads.ToString()) })
            { GUI.Label(new UnityEngine.Rect(px + 28, sy, 220, 24), k, St(fBody, 14, Dim)); GUI.Label(new UnityEngine.Rect(px + 260, sy, pw - 288, 24), v, St(fDisp, 17, Color.white)); sy += 26; }
            Rect(px, py + ph - 70, pw, 1, Line); float bx = px + pw - 28;
            float nw = TextW(Sp("NEW MAP", 2), sBtn) + 44; bx -= nw; if (FlatBtn(bx, py + ph - 56, nw, 42, Sp("NEW MAP", 2), sBtn, Color.white, Hex(0xdcdcdc), Ink)) { G.settings.seed = Random.Range(10000, 99999); G.NewMap(); G.Deploy(); }
            float rw = TextW(Sp("RESET HOSTILES", 2), sBtn) + 44; bx -= rw + 8; if (FlatBtn(bx, py + ph - 56, rw, 42, Sp("RESET HOSTILES", 2), sBtn, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.16f), Color.white)) { G.ResetHostiles(); G.Deploy(); }
            GUI.matrix = oldM;
        }
    }
}
