using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BreachPoint
{
    [System.Serializable]
    public class Settings
    {
        public int playerArmor = 0, touchSensPct = 100; public bool bot; public int botLook = 0;
        public int count = 8, seed = 4242, difficulty = 1, armor = 0, behavior = 0, quality = 2; public float density = 1f, slowmo = 0.3f, sens = 1f, fov = 75f, painTol = 1f, nerve = 1f;
        public bool god, infinite, tracers = true, flank = true, hitText = true;
        public float padSens = 1f, padAdsMul = 0.55f, padDeadzone = 0.15f, padCurve = 2f; public bool mouseInvert, padInvert;
        public string mapId = "warehouse"; public int dens = 1, light = 1;
        public string mode = "clear"; public int kind = 0, duelTo = 10;   // mode: clear / duel / zombies; kind: 0 humans, 1 mixed, 2 myth, 3 zombies
        // enemy AI (difficulty 4 = custom)
        public float accuracy = 0.5f, reaction = 0.7f, speed = 0.95f, aggression = 0.45f, blood = 5f; public bool helmet;
        // player options
        public bool aimLaser, killFeed = true, inspect = true, killCam = true, music = true; public float master = 1f, musicVol = 0.5f; public int touch = 0;   // touch: 0 auto, 1 on, 2 off
        public int hostages = 3; public float bombTime = 180;   // hostage rescue and bomb defuse
        public int sliceMode = 1;   // 0: each cut at a random angle; 1: Blade Mode (hold aim with a blade to steer the cut)
        public bool cutEnv;   // katana and sickle cuts also split the environment (furniture, crates, shelves, pillars...)
        public string[] loadout = { "pistol", "rifle", "shotgun", "knife" }; public string[] variants = new string[0];
        public string Variant(string id) { foreach (var v in variants) { var p = v.Split('='); if (p.Length == 2 && p[0] == id) return p[1]; } return null; }
        public void SetVariant(string id, string vid) { var l = new List<string>(); foreach (var v in variants) if (!v.StartsWith(id + "=")) l.Add(v); l.Add(id + "=" + vid); variants = l.ToArray(); }
    }
    public class Stats { public int shots, hits, organs, heads, kills, downs, arrests; public float t0; }

    /// <summary>Bootstrap and game state: map, player, hostiles, post-processing quality, HUD and menus (IMGUI, so no UI setup is needed).</summary>
    public class Game : MonoBehaviour
    {
        public static Game I;
        public const int LayerEnv = 0, LayerBody = 8, LayerPlayer = 9, LayerViewModel = 10, LayerProp = 12;
        public static int MaskShots => (1 << LayerEnv) | (1 << LayerBody) | (1 << LayerPlayer) | (1 << LayerProp) | (1 << MapGen.LayerPortal);
        public static int MaskEnv => 1 << LayerEnv;
        public enum State { Menu, Playing, End }
        public State state = State.Menu; public Settings settings = new Settings(); [System.NonSerialized] public Stats stats = new Stats();
        public MapGen map; public Player player; public ZombieMode zm; public HostageMode hm; public BombMode bm; public readonly List<Hostile> hostages = new List<Hostile>(); public Secrets secrets; public bool timeStop; public Hostile lastDown; public readonly List<Hostile> hostiles = new List<Hostile>(); public HUD hud;
        Volume volume; ColorAdjustments grade; DepthOfField dof; MotionBlur mblur; Bloom bloom; FilmGrain grain; bool started, won; float endT = -1; AudioClip enemyShot; int ids;
        readonly List<string> feed = new List<string>(); readonly List<float> feedT = new List<float>();

        void Awake()
        {
            I = this; BPInput.Init(); Progress.Load(); Physics.autoSyncTransforms = true; PhysicsTuning.Global(); Application.targetFrameRate = 120;
            var js = PlayerPrefs.GetString("bp_settings", ""); if (!string.IsNullOrEmpty(js)) { try { JsonUtility.FromJsonOverwrite(js, settings); } catch { } }
            new GameObject("FX").AddComponent<FX>().transform.SetParent(transform, false);
            new GameObject("Specials").AddComponent<Specials>().transform.SetParent(transform, false);
            zm = gameObject.AddComponent<ZombieMode>(); hm = gameObject.AddComponent<HostageMode>(); bm = gameObject.AddComponent<BombMode>(); secrets = gameObject.AddComponent<Secrets>(); gameObject.AddComponent<Music>(); gameObject.AddComponent<TouchUI>();
            Specials.I.tempOwned = id => zm.TempOwned(id);
            map = new GameObject("Map").AddComponent<MapGen>(); map.transform.SetParent(transform, false);
            // camera: main + an overlay that only draws the view model, so guns never clip into walls
            var main = Camera.main; if (main == null) { main = new GameObject("Main Camera").AddComponent<Camera>(); main.tag = "MainCamera"; main.gameObject.AddComponent<AudioListener>(); }
            main.cullingMask &= ~(1 << LayerViewModel); var md = main.GetUniversalAdditionalCameraData(); md.renderPostProcessing = true; md.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var vmCam = new GameObject("ViewModelCamera").AddComponent<Camera>(); vmCam.transform.SetParent(main.transform, false); vmCam.cullingMask = 1 << LayerViewModel; vmCam.fieldOfView = 50; vmCam.nearClipPlane = 0.005f; vmCam.farClipPlane = 5;
            var vd = vmCam.GetUniversalAdditionalCameraData(); vd.renderType = CameraRenderType.Overlay; md.cameraStack.Add(vmCam);
            var vmLight = new GameObject("VMLight").AddComponent<Light>(); vmLight.type = LightType.Directional; vmLight.intensity = 0.6f; vmLight.cullingMask = 1 << LayerViewModel; vmLight.transform.SetParent(main.transform, false); vmLight.transform.localRotation = Quaternion.Euler(30, -30, 0);
            var pgo = new GameObject("Player"); player = pgo.AddComponent<Player>(); player.Init(main);
            hud = gameObject.AddComponent<HUD>();
            SetupVolume(); enemyShot = SynthDistantShot();
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(0.32f, 0.34f, 0.37f); RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.21f); RenderSettings.ambientGroundColor = new Color(0.1f, 0.09f, 0.08f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.07f); RenderSettings.fogStartDistance = 25; RenderSettings.fogEndDistance = 90;
            foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional && l != vmLight) l.enabled = false;
            NewMap(); ApplyQuality();
        }

        void SetupVolume()
        {
            var go = new GameObject("PostFX"); go.transform.SetParent(transform, false); volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>(); volume.profile = p;
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.6f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.6f);
            var ca = p.Add<ColorAdjustments>(true); grade = ca; ca.postExposure.Override(0.35f); ca.contrast.Override(12f); ca.saturation.Override(8f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.28f); vg.smoothness.Override(0.45f);
            grain = p.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.18f);
            mblur = p.Add<MotionBlur>(true); mblur.intensity.Override(0.25f); mblur.quality.Override(MotionBlurQuality.Medium);
            dof = p.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Off); dof.focalLength.Override(60f); dof.aperture.Override(4f);
        }

        public void ApplyQuality()
        {
            int q = settings.quality; var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null) { asset.renderScale = q == 0 ? 0.75f : q == 1 ? 0.85f : 1f; asset.shadowDistance = q == 0 ? 0 : q == 1 ? 25 : q == 2 ? 45 : 70; asset.msaaSampleCount = q == 3 ? 4 : 1; }
            bloom.active = q >= 1; grain.active = q >= 1; mblur.active = q >= 2; QualitySettings.shadows = q == 0 ? UnityEngine.ShadowQuality.Disable : UnityEngine.ShadowQuality.All;
            // phones and tablets: a lighter version whatever the preset (lower render resolution, no shadows, no post effects, one reflection probe)
            if (Mobile)
            {
                if (asset != null) { asset.renderScale = q >= 2 ? 0.7f : 0.6f; asset.shadowDistance = 0; asset.msaaSampleCount = 1; }
                bloom.active = false; grain.active = false; mblur.active = false; QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            }
            SaveSettings();
        }
        /// <summary>Running on a phone or tablet (the web build reports mobile browsers).</summary>
        public static bool Mobile => Application.isMobilePlatform;
        public void SaveSettings() { PlayerPrefs.SetString("bp_settings", JsonUtility.ToJson(settings)); PlayerPrefs.Save(); }

        public void NewMap() { map.Build(settings.seed, settings.mapId, settings.dens, settings.light, settings.mode == "duel"); ResetHostiles(); }

        public void ResetHostiles()
        {
            foreach (var h in hostiles) if (h) Destroy(h.gameObject); hostiles.Clear(); zm.Clear(); hm.Clear(); bm.Clear(); FX.I.Clear(); Specials.I.Clear(); kc = null; lastDown = null; AudioListener.volume = settings.master; map.ResetDoors(); feed.Clear(); feedT.Clear(); won = false; endT = -1; ids = 0;
            player.weapons.LoadLoadout(); Progress.NewMission(); duelMe = duelThem = 0; duelRespawnT = -1; playerRespawnT = -1; saintK = 0;
            stats = new Stats { t0 = Time.time }; Time.timeScale = 1;
            if ((settings.mode == "duel") != map.arena) map.Build(settings.seed, settings.mapId, settings.dens, settings.light, settings.mode == "duel");
            player.Spawn(map.PlayerSpawn, 90f); SpawnAlly();
            if (map.breachDoor != null) Feed("Breach the door: kick it (K), shoot the lock or both hinges, plant your charge (G), or cut it");
            secrets.Build();
            if (settings.mode == "duel") { SpawnDuelist(); return; }
            if (settings.mode == "zombies") { zm.Begin(); return; }
            float[] acc = { 0.3f, 0.5f, 0.7f, 0.9f }, rea = { 1.1f, 0.7f, 0.45f, 0.25f };
            Color[] skins = { new Color(0.96f, 0.8f, 0.69f), new Color(0.87f, 0.67f, 0.5f), new Color(0.68f, 0.48f, 0.33f), new Color(0.45f, 0.3f, 0.2f), new Color(0.3f, 0.2f, 0.14f) };
            Color[] hairs = { new Color(0.1f, 0.08f, 0.06f), new Color(0.3f, 0.2f, 0.1f), new Color(0.55f, 0.4f, 0.2f), new Color(0.8f, 0.65f, 0.4f), new Color(0.45f, 0.15f, 0.08f) };
            for (int i = 0; i < settings.count; i++)
            {
                bool z = settings.kind == 3; var h = MakeHostile(map.RandomFree(map.PlayerSpawn, 14f), z); h.yaw = Random.value * Mathf.PI * 2; if (z) { h.Zombify(0.25f, false); continue; }
                // myth creatures: Mixed is 65% people / 35% myth (the web's yokai share goes to myth here)
                // myth creatures removed: old saved "Mixed"/"Myth" settings spawn people
            }
            if (settings.mode == "hostage") hm.Begin(); else if (settings.mode == "bomb") bm.Begin();
        }
        static readonly Color[] Skins = { new Color(0.96f, 0.8f, 0.69f), new Color(0.87f, 0.67f, 0.5f), new Color(0.68f, 0.48f, 0.33f), new Color(0.45f, 0.3f, 0.2f), new Color(0.3f, 0.2f, 0.14f) };
        static readonly Color[] Hairs = { new Color(0.1f, 0.08f, 0.06f), new Color(0.3f, 0.2f, 0.1f), new Color(0.55f, 0.4f, 0.2f), new Color(0.8f, 0.65f, 0.4f), new Color(0.45f, 0.15f, 0.08f) };
        /// <summary>Difficulty presets for the AI sliders (index 4 = custom keeps the sliders as they are).</summary>
        public static readonly float[,] DiffPresets = { { 0.3f, 1.1f, 0.85f, 0.3f }, { 0.5f, 0.7f, 0.95f, 0.45f }, { 0.7f, 0.45f, 1.05f, 0.6f }, { 0.9f, 0.25f, 1.15f, 0.75f } };
        public void ApplyDifficulty(int d) { settings.difficulty = d; if (d > 3) return; settings.accuracy = DiffPresets[d, 0]; settings.reaction = DiffPresets[d, 1]; settings.speed = DiffPresets[d, 2]; settings.aggression = DiffPresets[d, 3]; settings.flank = d >= 3 || settings.flank; }
        /// <summary>The teammate bot, if on (Settings ▸ Game ▸ Teammate bot). Not in the hostiles list, so it never counts toward winning or stats.</summary>
        public Hostile ally;
        void SpawnAlly()
        {
            if (ally) Destroy(ally.gameObject); ally = null; if (!settings.bot || Duel) return;
            var h = MakeHostile(map.NearFree(map.PlayerSpawn, 1.2f)); hostiles.Remove(h); h.armor = 2; h.AddHelmet(); h.MakeAlly(settings.botLook == 0); h.yaw = 90f * Mathf.Deg2Rad; ally = h;
        }
        public Hostile MakeHostile(Vector3 pos, bool zombie = false)
        {
            var go = new GameObject("Hostile"); go.transform.position = pos; var skin = zombie ? BPUtil.Pick(Hostile.ZombieSkins) : BPUtil.Pick(Skins); var hair = BPUtil.Pick(Hairs); if (zombie) hair = Color.Lerp(hair, new Color(0.47f, 0.45f, 0.42f), 0.45f);
            var model = CharacterBuilder.Create(Random.value < 0.4f, skin, hair);
            var h = go.AddComponent<Hostile>(); h.skin = skin; h.armor = zombie ? 0 : settings.armor; h.accuracy = settings.accuracy; h.reaction = settings.reaction; h.aggression = settings.aggression; h.speedMul = settings.speed;
            h.bloodMax = h.blood = settings.blood * 1000f;
            h.Init(model.transform, ++ids); h.anim = CharAnim.Attach(model); if (settings.helmet && !zombie) h.AddHelmet(); hostiles.Add(h); return h;
        }

        // ---------------- duel: one opponent at a time, first to duelTo ----------------
        public int duelMe, duelThem; float duelRespawnT = -1, playerRespawnT = -1; public float protectT; Hostile duelist; float huntT;
        public bool Duel => settings.mode == "duel";
        void SpawnDuelist()
        {
            var h = MakeHostile(map.DuelSpawn); h.yaw = -Mathf.PI / 2; h.duel = true; h.prof = BPUtil.Pick(new[] { "rifle", "shotgun", "dmr" });
            h.accuracy = Mathf.Min(1, h.accuracy * (h.prof == "dmr" ? 1.35f : 1.15f)); if (h.prof == "dmr") h.dmgMul = 2.3f; h.painTol *= 1.5f; h.aggression = Mathf.Max(h.aggression, 0.6f); h.morale = 99;
            h.Hear(player.transform.position); duelist = h; huntT = Random.Range(2.5f, 4f); Feed("Duelist: " + (h.prof == "dmr" ? "marksman" : h.prof));
        }
        void TickDuel(float dt)
        {
            if (duelist && duelist.Down && !duelist.duelScored) { duelist.duelScored = true; duelMe++; Feed("Duel " + duelMe + " – " + duelThem); if (settings.duelTo > 0 && duelMe >= settings.duelTo) { won = true; endT = 1.2f; return; } duelRespawnT = 2f; }
            if (duelRespawnT > 0) { duelRespawnT -= dt; if (duelRespawnT <= 0) SpawnDuelist(); }
            if (duelist && !duelist.Down) { huntT -= dt; if (huntT <= 0) { huntT = Random.Range(2.5f, 4f); duelist.Hear(player.transform.position); } }
            if (playerRespawnT > 0) { playerRespawnT -= dt; if (playerRespawnT <= 0) { player.Spawn(map.PlayerSpawn, 90f); protectT = 1.5f; } }
            protectT = Mathf.Max(0, protectT - dt);
            // keep at most 3 bodies, none older than 15 s
            var bodies = hostiles.FindAll(x => x && x.Down && x != duelist); bodies.Sort((a, b) => a.downAt.CompareTo(b.downAt));
            for (int i = 0; i < bodies.Count; i++) if (bodies.Count - i > 3 || Time.time - bodies[i].downAt > 15) { hostiles.Remove(bodies[i]); Destroy(bodies[i].gameObject); }
        }

        /// <summary>After every scored kill: skill effects (Soul Siphon now; Death Rattle arrives with the powers update).</summary>
        public void OnKill(Hostile e)
        {
            zm.Award(e);
            int de = Progress.Skill("m_deadeye"); if (de > 0) Specials.I.DeadEyeGain(0.1f * de);
            int s = Progress.Skill("w_siphon"); if (s > 0 && player.alive) player.hp = Mathf.Min(player.MaxHp, player.hp + 8 * s);
            if (Progress.Skill("w_rattle") > 0) Specials.I.DeathRattle(e);
        }

        // ---------------- saint gaze: looking at a saint pulls your aim and gilds the view ----------------
        public float saintK;
        void TickSaints(float dt)
        {
            float k = 0; var pe = player.EyePos; var f = player.cam.transform.forward;
            foreach (var h in hostiles) { if (!h || h.Down || h.myth != "saint") continue; var d = h.Center + Vector3.up * 0.4f - pe; float m = d.magnitude; if (m > 22) continue; float dot = Vector3.Dot(f, d / m); if (dot < 0.9f || !map.LOS(pe, h.Center + Vector3.up * 0.4f)) continue; k = Mathf.Max(k, (dot - 0.9f) / 0.1f * (1 - m / 22)); }
            saintK = Mathf.MoveTowards(saintK, k, dt * 2);
            if (saintK > 0.01f && player.alive) { float t = Time.time; player.yaw += Mathf.Sin(t * 1.7f) * saintK * 9f * dt; player.pitch += Mathf.Cos(t * 1.3f) * saintK * 5f * dt; }
        }

        public void Deploy() { state = State.Playing; started = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        public void OpenMenu() { state = State.Menu; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; Time.timeScale = 1; }

        public void Feed(string s) { if (s == null) return; feed.Add(s); feedT.Add(Time.unscaledTime); if (feed.Count > 6) { feed.RemoveAt(0); feedT.RemoveAt(0); } }
        public IEnumerable<string> FeedLines() { for (int i = 0; i < feed.Count; i++) if (Time.unscaledTime - feedT[i] < 7f) yield return feed[i]; }
        public void Noise(Vector3 at, float r) { foreach (var h in hostiles) if (h && !h.Down && Vector3.Distance(h.transform.position, at) < r) h.Hear(at); }
        public bool TryArrest()
        {
            if (zm.Use()) return true; if (hm.TryFree()) return true; if (bm.Near) return true;
            Hostile best = null; float bd = 2.3f;
            foreach (var h in hostiles) { if (!h || h.state != HState.Surrender) continue; var d = h.transform.position - player.transform.position; d.y = 0; if (d.magnitude < bd && Vector3.Dot(player.transform.forward, d.normalized) > 0.35f) { bd = d.magnitude; best = h; } }
            if (best == null) return false; best.Arrest(); return true;
        }
        public void OnPlayerDied() { Specials.I.DeStop(); if (zm.PlayerDown()) return; if (Duel) { duelThem++; Feed("Duel " + duelMe + " – " + duelThem); if (settings.duelTo > 0 && duelThem >= settings.duelTo) { endT = 1.5f; won = false; } else playerRespawnT = 2.2f; return; } endT = 1f; won = false; }
        public void EnemyShotSound(Vector3 at) { AudioSource.PlayClipAtPoint(enemyShot, at, 0.6f); }
        static AudioClip SynthDistantShot() { int sr = 22050, n = sr / 3; var d = new float[n]; float lp = 0; for (int i = 0; i < n; i++) { float t = i / (float)sr; lp += (Random.Range(-1f, 1f) - lp) * 0.2f; d[i] = lp * Mathf.Exp(-t * 14f) * 2f; } var c = AudioClip.Create("eshot", n, 1, sr, false); c.SetData(d, 0); return c; }

        void Update()
        {
            if (hud.Rebinding) return;
            if (BPInput.Pause.WasPressedThisFrame()) { if (state == State.Playing) OpenMenu(); else if (state == State.Menu && !hud.ControlsOpen && started) Deploy(); else if (hud.ControlsOpen) hud.ControlsOpen = false; }
            // controller in menus: A deploys / resumes, Y starts a new map on the end screen
            var pad = Gamepad.current;
            if (pad != null && state != State.Playing && !hud.ControlsOpen)
            {
                if (pad.buttonSouth.wasPressedThisFrame) { if (state == State.End) ResetHostiles(); Deploy(); }
                else if (pad.buttonNorth.wasPressedThisFrame && state == State.End) { settings.seed = Random.Range(10000, 99999); NewMap(); Deploy(); }
            }
            if (state == State.Playing && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            Time.fixedDeltaTime = PhysicsTuning.Step * Mathf.Max(0.05f, Time.timeScale);   // keeps slow motion and Dead Eye smooth
            if (state == State.Playing)
            {
                if (Duel) { if (endT < 0) TickDuel(Time.deltaTime); }
                else if (settings.mode == "bomb") bm.Tick(Time.deltaTime);
                else if (settings.mode == "zombies") { zm.Tick(Time.deltaTime); if (!player.alive && zm.downT < 0 && endT < 0) { won = false; endT = 1.5f; } }
                else if (settings.mode == "hostage") { hm.Tick(Time.deltaTime); if (!won && endT < 0 && hostiles.Count > 0 && hostiles.TrueForAll(h => h == null || h.Down)) { won = hm.Alive + hm.rescued > 0; endT = 1.2f; if (won && settings.killCam && lastDown && player.alive) { StartKillCam(lastDown); endT = 3.4f; } } }
                else if (!won && endT < 0 && hostiles.Count > 0 && hostiles.TrueForAll(h => h == null || h.Down)) { won = true; endT = 1.2f; if (settings.killCam && lastDown && player.alive) { StartKillCam(lastDown); endT = 3.4f; } }
                TickKillCam(); TickGrade();
                TickSaints(Time.deltaTime);
                if (endT > 0) { endT -= Time.unscaledDeltaTime; if (endT <= 0) { state = State.End; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; Time.timeScale = 1; } }
            }
            // aim depth of field: focus on what the sights are on (High and Ultra)
            if (dof != null)
            {
                bool on = settings.quality >= 2 && state == State.Playing && player.weapons.ads > 0.4f;
                if (on && Physics.Raycast(player.cam.transform.position, player.cam.transform.forward, out var hit, 120f, MaskShots & ~(1 << LayerPlayer))) dof.focusDistance.Override(Mathf.Lerp(dof.focusDistance.value, hit.distance, Time.unscaledDeltaTime * 8));
                dof.mode.Override(on ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off);
            }
        }

        public bool Won => won;
        public bool EndPending => endT >= 0;
        /// <summary>A mission objective decided the round (bomb defused / exploded, all hostages lost).</summary>
        public void EndMission(bool win) { if (endT >= 0) return; won = win; endT = win ? 1.6f : 2.2f; }

        // ---------------- final takedown cam: cut to the last hostile to go down and swing round them in slow motion ----------------
        class KC { public Hostile e; public float t; public Vector3 d; public float sd; public Vector3 pos; public bool hasPos; }
        KC kc; public bool KillCamActive => kc != null;
        void StartKillCam(Hostile e)
        {
            Specials.I.DeStop(); var c = e.Center; var d = Vector3.zero; var ld = e.LastHitDir; d = new Vector3(ld.x, 0, ld.z);
            if (d.sqrMagnitude < 1e-4f) d = new Vector3(c.x - player.transform.position.x, 0, c.z - player.transform.position.z); if (d.sqrMagnitude < 1e-4f) d = Vector3.right; d.Normalize();
            kc = new KC { e = e, d = d, sd = Random.value < 0.5f ? 1 : -1 }; if (!map.LOS(CamAt(kc, kc.sd, 1.2f, 2.6f), c) && map.LOS(CamAt(kc, -kc.sd, 1.2f, 2.6f), c)) kc.sd = -kc.sd;
        }
        Vector3 CamAt(KC K, float sd, float a, float dist) { var c = K.e.Center; float ca = Mathf.Cos(a), sa = Mathf.Sin(a); float x = -K.d.x * ca + (-K.d.z) * sd * sa, z = -K.d.z * ca + K.d.x * sd * sa; return new Vector3(c.x + x * dist, c.y + 0.35f, c.z + z * dist); }
        void TickKillCam() { if (kc == null) return; kc.t += Time.unscaledDeltaTime; Time.timeScale = kc.t < 1.7f ? 0.12f : 0.3f; if (kc.t > 3.2f || !kc.e) { kc = null; Time.timeScale = 1; } }
        /// <summary>Called by the player after its own camera update.</summary>
        public void ApplyKillCam(Camera cam)
        {
            if (kc == null || !kc.e) return; var c = kc.e.Center; float k = Mathf.Min(1, kc.t / 2.8f), sm = k * k * (3 - 2 * k); float a = 0.25f + 1.05f * sm, dist = 1.7f + 1.0f * sm, hy = Mathf.Max(c.y + 0.35f, 0.9f + 0.5f * sm);
            Vector3 best = Vector3.zero; bool found = false;
            foreach (var (sd, da) in new[] { (kc.sd, 0f), (kc.sd, 0.5f), (kc.sd, -0.5f), (-kc.sd, 0f), (kc.sd, 1.1f), (-kc.sd, 0.6f), (kc.sd, -1.0f) }) { var q = CamAt(kc, sd, a + da, dist); q.y = hy; if (map.LOS(q, c)) { best = q; found = true; break; } }
            if (!found) { float dd = dist; best = CamAt(kc, kc.sd, a, dd); best.y = hy; for (int i = 0; i < 8 && dd > 0.6f && !map.LOS(best, c); i++) { dd -= 0.25f; best = CamAt(kc, kc.sd, a, dd); best.y = hy + 0.15f * i; } }
            if (!kc.hasPos) { kc.pos = best; kc.hasPos = true; } else kc.pos = Vector3.Lerp(kc.pos, best, 0.12f);
            cam.transform.position = kc.pos; cam.transform.rotation = Quaternion.LookRotation(c - kc.pos);
        }

        // ---------------- colour grade for time stop, Dead Eye and Wraith Form ----------------
        void TickGrade()
        {
            if (grade == null) return; float sat = 8, con = 12; Color filt = Color.white;
            if (timeStop) { sat = -90; con = 18; }
            if (Specials.I.dePhase != "off") { sat = 40; con = 18; filt = new Color(1f, 0.78f, 0.52f); }
            if (player.Ghost) { sat = -60; filt = new Color(0.72f, 0.95f, 1.1f); }
            grade.saturation.Override(Mathf.Lerp(grade.saturation.value, sat, 0.2f)); grade.contrast.Override(con); grade.colorFilter.Override(Color.Lerp(grade.colorFilter.value, filt, 0.2f));
        }
    }
}
