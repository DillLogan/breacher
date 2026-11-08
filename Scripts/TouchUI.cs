using UnityEngine;
using UnityEngine.InputSystem;

namespace BreachPoint
{
    /// <summary>
    /// On-screen controls for phones and tablets (web build). Left half: move stick. Right half: drag to look. Buttons: fire, aim, reload, jump,
    /// crouch, sprint, switch weapon, use, Dead Eye, pause. Shows automatically when a touch screen is used (Settings ▸ Touch controls).
    /// </summary>
    public class TouchUI : MonoBehaviour
    {
        public static Vector2 Move, Look; public static bool Fire, Aim, Crouch, Sprint, Jump, Reload, Switch, Use, DeadEye, AimPressed, FirePressed;
        public static bool Active { get; private set; }
        Vector2 rawLook, smooth, tapPos; float tapT, tapMoved; Door doorNear;
        static bool IsDoorBtn(string id) => id == "door" || id == "kick";
        /// <summary>A quick tap on the look side: tapping a door you're close to plants your charge on a locked door, or opens an unlocked one.</summary>
        void TapAt(Vector2 screenPos)
        {
            var G = Game.I; var cam = G.player ? G.player.cam : null; if (!cam) return;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out var h, 4f, Game.MaskEnv, QueryTriggerInteraction.Ignore)) return;
            var dr = h.collider.GetComponentInParent<DoorRef>(); if (dr == null || dr.door == null || dr.door.state != "closed") return;
            G.map.DoorActionOn(dr.door, false);
        }
        int moveId = -1, lookId = -1; Vector2 moveOrigin, movePos; bool crouchT, sprintT, aimT;
        readonly System.Collections.Generic.Dictionary<int, string> held = new System.Collections.Generic.Dictionary<int, string>();
        static float S => Mathf.Max(1, Screen.height / 720f);

        struct Btn { public string id, label; public Rect r; public bool hidden; }

        // ---------------- custom layout: position, size and visibility of each button (saved on the device) ----------------
        /// <summary>True while the layout editor is open (Settings ▸ Controls ▸ Edit touch layout).</summary>
        public static bool Editing;
        class Lay { public float x = -1, y = -1, s = 1; public bool hide; }
        static System.Collections.Generic.Dictionary<string, Lay> lay; static bool allHidden;
        const string LayKey = "bp_touch_layout", HideAllKey = "bp_touch_hideall";
        static void LoadLayout()
        {
            if (lay != null) return; lay = new System.Collections.Generic.Dictionary<string, Lay>(); allHidden = PlayerPrefs.GetInt(HideAllKey, 0) == 1;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var e in PlayerPrefs.GetString(LayKey, "").Split(';'))
            {
                var kv = e.Split(':'); if (kv.Length != 2) continue; var v = kv[1].Split(','); if (v.Length != 4) continue;
                float.TryParse(v[0], System.Globalization.NumberStyles.Float, inv, out var x); float.TryParse(v[1], System.Globalization.NumberStyles.Float, inv, out var y); float.TryParse(v[2], System.Globalization.NumberStyles.Float, inv, out var sc);
                lay[kv[0]] = new Lay { x = x, y = y, s = sc > 0 ? sc : 1, hide = v[3] == "1" };
            }
        }
        static void SaveLayout()
        {
            var sb = new System.Text.StringBuilder(); var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kv in lay) sb.Append(kv.Key).Append(':').Append(kv.Value.x.ToString("0.####", inv)).Append(',').Append(kv.Value.y.ToString("0.####", inv)).Append(',').Append(kv.Value.s.ToString("0.##", inv)).Append(',').Append(kv.Value.hide ? "1" : "0").Append(';');
            PlayerPrefs.SetString(LayKey, sb.ToString()); PlayerPrefs.SetInt(HideAllKey, allHidden ? 1 : 0); PlayerPrefs.Save();
        }
        /// <summary>Put every button back where it started, full size and visible.</summary>
        public static void ResetLayout() { LoadLayout(); lay.Clear(); allHidden = false; SaveLayout(); }
        static Lay L(string id) { LoadLayout(); if (!lay.TryGetValue(id, out var l)) { l = new Lay(); lay[id] = l; } return l; }

        Btn[] Buttons()
        {
            var d = DefaultButtons(); LoadLayout(); float W = Screen.width, H = Screen.height;
            for (int i = 0; i < d.Length; i++)
            {
                if (!lay.TryGetValue(d[i].id, out var l)) { d[i].hidden = allHidden; continue; }
                var r = d[i].r; float w = r.width * l.s, h = r.height * l.s; var c = l.x >= 0 ? new Vector2(l.x * W, l.y * H) : r.center;
                c.x = Mathf.Clamp(c.x, w / 2, W - w / 2); c.y = Mathf.Clamp(c.y, h / 2, H - h / 2); d[i].r = new Rect(c.x - w / 2, c.y - h / 2, w, h); d[i].hidden = allHidden || l.hide;
            }
            return d;
        }
        Btn[] DefaultButtons()
        {
            float W = Screen.width, H = Screen.height, s = S, b = 74 * s, g = 12 * s;
            return new[]
            {
                new Btn { id = "fire", label = "FIRE", r = new Rect(W - b * 1.35f - g, H - b * 1.35f - g, b * 1.35f, b * 1.35f) },
                new Btn { id = "aim", label = "AIM", r = new Rect(W - b * 2.45f - g * 2, H - b - g, b, b) },
                new Btn { id = "jump", label = "JUMP", r = new Rect(W - b * 1.2f - g, H - b * 2.5f - g * 2, b, b) },
                new Btn { id = "reload", label = "R", r = new Rect(W - b * 2.45f - g * 2, H - b * 2 - g * 2, b * 0.8f, b * 0.8f) },
                new Btn { id = "crouch", label = "CROUCH", r = new Rect(W - b * 3.5f - g * 3, H - b - g, b, b) },
                new Btn { id = "sprint", label = "SPRINT", r = new Rect(g, H - b * 3.4f - g, b, b * 0.6f) },
                new Btn { id = "switch", label = "SWAP", r = new Rect(W - b * 1.1f - g, g + b * 0.7f, b, b * 0.6f) },
                new Btn { id = "use", label = "USE", r = new Rect(W - b * 2.2f - g * 2, g + b * 0.7f, b, b * 0.6f) },
                new Btn { id = "deadeye", label = "DEAD EYE", r = new Rect(W - b * 3.3f - g * 3, g + b * 0.7f, b, b * 0.6f) },
                new Btn { id = "pause", label = "II", r = new Rect(W / 2 - b * 0.3f, g, b * 0.6f, b * 0.5f) },
                // door buttons: only shown (and only work) while you're facing a closed door
                new Btn { id = "door", label = "OPEN", r = new Rect(W * 0.66f, H * 0.36f, b * 1.25f, b * 0.7f) },
                new Btn { id = "kick", label = "KICK", r = new Rect(W * 0.66f, H * 0.36f + b * 0.85f, b * 1.25f, b * 0.7f) },
            };
        }

        void Update()
        {
            Jump = Reload = Switch = Use = DeadEye = AimPressed = FirePressed = false; Look = Vector2.zero;
            var ts = Touchscreen.current; var G = Game.I; int mode = G ? G.settings.touch : 2;
            if (ts != null && ts.primaryTouch.press.isPressed && mode == 0) Active = true; if (mode == 1) Active = true; if (mode == 2) Active = false;
            if (Editing || !Active || ts == null || G == null || G.state != Game.State.Playing) { Move = Vector2.zero; Fire = Aim = Crouch = Sprint = false; moveId = lookId = -1; held.Clear(); return; }
            var btns = Buttons(); bool fire = false;
            foreach (var t in ts.touches)
            {
                int id = t.touchId.ReadValue(); var ph = t.phase.ReadValue(); var pos = t.position.ReadValue(); var gp = new Vector2(pos.x, Screen.height - pos.y);
                if (ph == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    string hit = null; foreach (var b in btns) if (b.r.Contains(gp) && (!IsDoorBtn(b.id) || doorNear != null)) { hit = b.id; break; }
                    if (hit != null)
                    {
                        held[id] = hit;
                        switch (hit) { case "jump": Jump = true; break; case "reload": Reload = true; break; case "switch": Switch = true; break; case "use": Use = true; break; case "deadeye": DeadEye = true; break; case "crouch": crouchT = !crouchT; break; case "sprint": sprintT = !sprintT; break; case "aim": aimT = !aimT; AimPressed = true; break; case "fire": FirePressed = true; break; case "pause": G.OpenMenu(); break; case "door": G.map.DoorAction(false); break; case "kick": G.map.DoorAction(true); break; }
                    }
                    else if (pos.x < Screen.width * 0.4f && moveId < 0) { moveId = id; moveOrigin = movePos = pos; }
                    else if (lookId < 0) { lookId = id; tapT = Time.unscaledTime; tapPos = pos; tapMoved = 0; }
                }
                if (ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled) { held.Remove(id); if (id == moveId) { moveId = -1; } if (id == lookId) { lookId = -1; if (ph == UnityEngine.InputSystem.TouchPhase.Ended && Time.unscaledTime - tapT < 0.3f && tapMoved < 24 * S) TapAt(pos); } continue; }
                if (ph == UnityEngine.InputSystem.TouchPhase.None) continue;
                if (held.TryGetValue(id, out var hb)) { if (hb == "fire") fire = true; if (hb == "fire" || hb == "aim") rawLook += t.delta.ReadValue() * 0.8f; }
                else if (id == moveId) movePos = pos;
                else if (id == lookId) { var dd = t.delta.ReadValue(); rawLook += dd; tapMoved += dd.magnitude; }
            }
            // look: smoothed (touch deltas jitter frame to frame), slower for small precise drags and faster for quick swipes, scaled by the touch sensitivity setting
            { float dtu = Mathf.Max(1e-3f, Time.unscaledDeltaTime); float pxs = rawLook.magnitude / dtu / S; float accel = Mathf.Lerp(0.6f, 1.45f, Mathf.InverseLerp(150f, 2200f, pxs));
              var want = rawLook * (0.15f / S) * accel * (G.settings.touchSensPct / 100f); smooth = Vector2.Lerp(smooth, want, 0.55f); if (rawLook == Vector2.zero && smooth.sqrMagnitude < 1e-4f) smooth = Vector2.zero; Look = smooth; rawLook = Vector2.zero; }
            doorNear = G.player && G.player.alive ? G.map.NearDoor(G.player.transform) : null;
            Fire = fire; Aim = aimT; Crouch = crouchT; Sprint = sprintT;
            var mv = (movePos - moveOrigin) / (60 * S); Move = moveId >= 0 ? Vector2.ClampMagnitude(mv, 1) : Vector2.zero; if (Move.y < 0.5f) sprintT = sprintT && Move.y > 0.2f;
        }

        void OnGUI()
        {
            if (Editing) { GUI.depth = -100; DrawEditor(); return; }
            if (!Active || Game.I == null || Game.I.state != Game.State.Playing) return;
            var st = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(14 * S), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; st.normal.textColor = Color.white;
            var old = GUI.color;
            foreach (var b in Buttons())
            {
                bool on = (b.id == "aim" && aimT) || (b.id == "crouch" && crouchT) || (b.id == "sprint" && sprintT) || (b.id == "fire" && Fire);
                if (IsDoorBtn(b.id) && doorNear == null) continue;
                if (b.hidden) continue; // invisible, but still works
                var label = b.id == "door" && doorNear != null ? (doorNear.locked ? (doorNear.chargeT < 0 && Game.I.map.breachCharges > 0 ? "CHARGE" : "LOCKED") : "OPEN") : b.label;
                GUI.color = new Color(1, 1, 1, on ? 0.75f : IsDoorBtn(b.id) ? 0.6f : 0.4f); GUI.Box(b.r, label, st);
            }
            if (moveId >= 0) { var o = new Vector2(moveOrigin.x, Screen.height - moveOrigin.y); float R = 60 * S; GUI.color = new Color(1, 1, 1, 0.25f); GUI.Box(new Rect(o.x - R, o.y - R, R * 2, R * 2), "", st); var k = o + new Vector2(Move.x, -Move.y) * R; GUI.color = new Color(1, 1, 1, 0.6f); GUI.Box(new Rect(k.x - R * 0.35f, k.y - R * 0.35f, R * 0.7f, R * 0.7f), "", st); }
            GUI.color = old;
        }

        // ---------------- layout editor ----------------
        string sel, drag; Vector2 dragOff;
        void DrawEditor()
        {
            float W = Screen.width, H = Screen.height, s = S; var e = Event.current; var old = GUI.color;
            var box = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; box.normal.textColor = Color.white;
            var btnSt = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(13 * s), fontStyle = FontStyle.Bold }; var lab = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter, wordWrap = true }; lab.normal.textColor = new Color(1, 1, 1, 0.75f);
            GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.94f); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = old;
            // the move stick works anywhere in the left 40% that isn't a button
            GUI.color = new Color(1, 1, 1, 0.06f); GUI.DrawTexture(new Rect(0, 0, W * 0.4f, H), Texture2D.whiteTexture); GUI.color = new Color(1, 1, 1, 0.35f); GUI.Label(new Rect(0, H * 0.5f - 20 * s, W * 0.4f, 40 * s), "MOVE STICK AREA", lab); GUI.color = old;
            // toolbar in the middle of the screen (the buttons sit around the edges)
            float bw = 112 * s, bh = 40 * s, gap = 6 * s; int cols = 3; float tw = cols * bw + (cols - 1) * gap, tx = W * 0.5f - tw / 2 + W * 0.05f, ty = H * 0.5f - bh * 1.6f;
            var tool = new Rect(tx - 10 * s, ty - 40 * s, tw + 20 * s, bh * 2 + gap + 50 * s);
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(tool, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(tool.x, ty - 36 * s, tool.width, 30 * s), sel == null ? "Drag a button to move it. Tap one to select it." : "Selected: " + sel.ToUpper(), lab);
            var Ls = sel != null ? L(sel) : null; GUI.enabled = sel != null;
            if (GUI.Button(new Rect(tx, ty, bw, bh), "SIZE −", btnSt)) { Ls.s = Mathf.Max(0.5f, Ls.s - 0.1f); SaveLayout(); }
            if (GUI.Button(new Rect(tx + bw + gap, ty, bw, bh), "SIZE +", btnSt)) { Ls.s = Mathf.Min(2f, Ls.s + 0.1f); SaveLayout(); }
            if (GUI.Button(new Rect(tx + (bw + gap) * 2, ty, bw, bh), Ls != null && Ls.hide ? "SHOW" : "INVISIBLE", btnSt)) { Ls.hide = !Ls.hide; SaveLayout(); }
            GUI.enabled = true; float ty2 = ty + bh + gap;
            if (GUI.Button(new Rect(tx, ty2, bw, bh), allHidden ? "SHOW ALL" : "ALL INVISIBLE", btnSt)) { allHidden = !allHidden; if (!allHidden) foreach (var l in lay.Values) l.hide = false; SaveLayout(); }
            if (GUI.Button(new Rect(tx + bw + gap, ty2, bw, bh), "RESET", btnSt)) { ResetLayout(); sel = null; }
            if (GUI.Button(new Rect(tx + (bw + gap) * 2, ty2, bw, bh), "DONE", btnSt)) { SaveLayout(); Editing = false; sel = drag = null; return; }
            // the buttons: invisible ones are drawn faintly here so they can still be placed
            var btns = Buttons();
            foreach (var b in btns)
            {
                bool on = b.id == sel; GUI.color = b.hidden ? new Color(1, 1, 1, on ? 0.45f : 0.2f) : new Color(on ? 1f : 1, on ? 0.85f : 1, on ? 0.4f : 1, on ? 0.9f : 0.55f);
                GUI.Box(b.r, b.hidden ? b.label + "\n(invisible)" : b.label, box);
            }
            GUI.color = old;
            if (e.type == EventType.MouseDown && !tool.Contains(e.mousePosition))
            {
                drag = null; for (int i = btns.Length - 1; i >= 0; i--) if (btns[i].r.Contains(e.mousePosition)) { drag = sel = btns[i].id; dragOff = e.mousePosition - btns[i].r.center; break; }
                if (drag == null) sel = null; e.Use();
            }
            else if (e.type == EventType.MouseDrag && drag != null) { var c = e.mousePosition - dragOff; var l = L(drag); l.x = Mathf.Clamp01(c.x / W); l.y = Mathf.Clamp01(c.y / H); e.Use(); }
            else if (e.type == EventType.MouseUp && drag != null) { drag = null; SaveLayout(); e.Use(); }
        }
    }
}
