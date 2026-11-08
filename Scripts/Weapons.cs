using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    public struct HitInfo
    {
        public BodyPart part; public Vector3 point, exit, end, dir, normal; public float energy, deposited, wound; public bool boneHit, through, armorStop, byPlayer, cartridge; public float wk; public List<Organ> organs;
    }
    public struct ShotResult { public Vector3 end; public string txt; public int sev; public bool hit, stopped; public Hostile e; public BodyPart part; public Collider env; public Vector3 normal; }

    /// <summary>Rounds travel through bodies until their energy is spent (soft tissue, bone core, organs, armour). Walls stop them; doors are punched through above 230 J; the rail slug ignores walls.</summary>
    public static partial class Ballistics
    {
        /// <summary>A shot from one of your guns: real cartridge physics for firearms, the energy model for everything else.</summary>
        public static ShotResult Shoot(WeaponDef w, Vector3 o, Vector3 d, float maxD) { var c = w.Ammo; return c != null ? ShotC(o, d, w.energy, c, maxD, null, true, 0, w.wound) : Shot(o, d, w.energy, w.Pen, maxD, null, false, true, 0, w.wound); }
        /// <summary>A hostile's shot with a named cartridge.</summary>
        public static Vector3 FireC(Vector3 o, Vector3 d, float energy, string cal, float maxD, Hostile shooter) => ShotC(o, d, energy, Cartridge.Named(cal), maxD, shooter).end;
        static readonly List<Organ> scratch = new List<Organ>();

        public static Vector3 Fire(Vector3 o, Vector3 dir, float energy, float pen, float maxD, Hostile shooter, out string summary) { var r = Shot(o, dir, energy, pen, maxD, shooter); summary = r.txt; return r.end; }

        public static ShotResult Shot(Vector3 o, Vector3 dir, float energy, float pen, float maxD, Hostile shooter, bool throughWalls = false, bool holes = true, int depth = 0, float wound = 0.018f)
        {
            var res = new ShotResult { end = o + dir * Mathf.Min(60, maxD), sev = -1 }; var hits = Physics.RaycastAll(o, dir, maxD, Game.MaskShots, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); float E = energy; var seen = new HashSet<Collider>();
            foreach (var h in hits)
            {
                if (E <= 0) break; if (seen.Contains(h.collider)) continue; seen.Add(h.collider);
                var portal = h.collider.GetComponent<Portal>();
                if (portal != null) { if (Specials.I && Specials.I.PortalPair(portal, out var other)) { Specials.I.MapThrough(portal, h.point, dir, out var np, out var nd); if (depth < 3) { var sub = Shot(np + other.n * 0.05f, nd, E, pen, Mathf.Max(0.5f, maxD - h.distance), shooter, throughWalls, holes, depth + 1, wound); if (sub.hit) { res.hit = true; if (sub.sev > res.sev) { res.sev = sub.sev; res.txt = sub.txt; } res.e = res.e ?? sub.e; } if (Game.I.settings.tracers) FX.I.Tracer(np, sub.end); } res.end = h.point; return res; } continue; }
                if (h.collider.isTrigger) continue;
                { var an = h.collider.GetComponent<Animal>(); if (an) { if (shooter == null) { an.Pop(dir); res.hit = true; Sev(ref res, 2, "The " + an.kind + " popped"); } res.end = h.point; return res; } }
                if (h.collider.gameObject.layer == Game.LayerPlayer) { if (shooter != null && !shooter.IsThrall && !shooter.ally) { Game.I.player.Shot(h.point, energy, null, Random.Range(9f, 18f) * shooter.dmgMul, shooter.tag2 + " shot you"); res.end = h.point; return res; } continue; }
                var bp = h.collider.GetComponent<BodyPart>();
                if (bp != null)
                {
                    var e = bp.owner; if (shooter != null && e == shooter) continue; if (e.Gone) continue;
                    if (e.frozen) { if (!e.shattered) { e.Shatter(dir); res.hit = true; Sev(ref res, 3, e.tag2 + " shattered"); } E -= 300; res.end = h.point; continue; }
                    if (e.shrunk && e.state != HState.Dead) { e.lastHitDirPub = dir; e.Die("Squashed while tiny"); res.hit = true; Sev(ref res, 3, e.tag2 + " popped"); res.end = h.point; E -= 200; continue; }
                    Vector3 exit = h.point + dir * 0.08f; if (h.collider.Raycast(new Ray(h.point + dir * 1.2f, -dir), out var ex, 1.2f)) exit = ex.point;
                    float L = Vector3.Distance(h.point, exit);
                    bool helm = bp.zone == Zone.Head && e.helmet && Random.value < 0.6f;
                    if ((bp.armored && e.armor > 0 || helm) && Random.value < 0.85f)
                    {
                        float stop = helm ? 650f : e.armor == 2 ? 2600f : 650f;
                        if (E * pen < stop * e.armorIntegrity) { e.armorIntegrity *= 0.93f; e.TakeHit(new HitInfo { byPlayer = shooter == null, part = bp, point = h.point, exit = h.point, dir = dir, normal = h.normal, energy = E, deposited = E * 0.08f, armorStop = true, organs = new List<Organ>() }); FX.I.Sparks(h.point, h.normal, 4); res.hit = true; Sev(ref res, 1, e.tag2 + ": stopped by armour"); res.end = h.point; res.e = e; return res; }
                        E -= stop * 0.5f; e.armorIntegrity *= 0.8f;
                    }
                    Vector3 A = bp.transform.InverseTransformPoint(h.point), B = bp.transform.InverseTransformPoint(exit);
                    float cost = L * bp.tissueResist; bool bone = BodyBuilder.SegSegDist(A, B, bp.boneA, bp.boneB) < bp.boneR + 0.004f; if (bone) cost += bp.zone == Zone.Head ? 900f : 450f;
                    float frac = E >= cost ? 1f : Mathf.Clamp01(E / Mathf.Max(1f, cost)); Vector3 Bend = Vector3.Lerp(A, B, frac);
                    scratch.Clear(); foreach (var org in bp.organs) if (BodyBuilder.SegSphere(A, Bend, org.local, org.radius)) scratch.Add(org);
                    bool wasDead = e.state == HState.Dead;
                    var info = new HitInfo { byPlayer = shooter == null, part = bp, point = h.point, exit = exit, end = bp.transform.TransformPoint(Bend), dir = dir, normal = h.normal, energy = E, wound = wound, deposited = Mathf.Min(E, cost), boneHit = bone && frac > 0.5f, through = frac >= 1f, organs = new List<Organ>(scratch) };
                    string t = e.TakeHit(info); res.hit = true; if (res.e == null) { res.e = e; res.part = bp; }
                    if (t != null) Sev(ref res, !wasDead && e.state == HState.Dead ? 3 : scratch.Count > 0 ? 2 : 1, t);
                    e.BloodSpray(h.point, dir, false, E, Hostile.Region(bp.zone)); if (!e.bloodless) FX.I.Decal(h.point, h.normal, 0.025f + Mathf.Min(0.03f, E / 60000f), true, bp.transform);
                    E -= cost; if (E <= 0) { res.end = Vector3.Lerp(h.point, exit, frac); return res; }
                    e.BloodSpray(exit, dir, true, E, Hostile.Region(bp.zone)); if (!e.bloodless) FX.I.Decal(exit, dir, 0.04f + Mathf.Min(0.05f, E / 40000f), true, bp.transform);
                    E *= 0.92f; res.end = exit; continue;
                }
                // loose objects: pushed with the round's real momentum (still ones wake up); wood can splinter
                var sbot = h.collider.GetComponent<SpaceBot>(); if (sbot) sbot.Hit(E, dir, h.point);
                var brk = h.collider.GetComponent<Breakable>(); if (brk) { brk.Shatter(dir, h.point); continue; }
                var lampH = h.collider.GetComponentInParent<LampSwing>(); if (lampH) { lampH.Shot(dir, E, h.point); continue; }
                var lpz = h.collider.GetComponent<LooseProp>(); if (lpz) { lpz.Shot(h.point, dir, E); if (holes) FX.I.Decal(h.point, h.normal, 0.025f, false, h.collider.transform); FX.I.Chips(h.point, h.normal * 2, 6, new Color(0.5f, 0.4f, 0.3f)); E *= lpz.wood ? 0.6f : 0.3f; continue; }
                if (h.collider.gameObject.layer == Game.LayerProp) { var rb = h.collider.attachedRigidbody; if (rb) rb.AddForceAtPosition(dir * PhysicsTuning.ShotImpulse(E), h.point, ForceMode.Impulse); FX.I.Chips(h.point, h.normal * 2, 6, new Color(0.5f, 0.4f, 0.3f)); E *= 0.6f; continue; }
                // the world: doors are punched through, the rail ignores everything
                var dr = h.collider.GetComponent<DoorRef>();
                if (holes) { FX.I.Sparks(h.point, h.normal, 5); FX.I.Decal(h.point, h.normal, 0.025f, false, h.collider.transform); }
                res.env = h.collider; res.normal = h.normal; res.end = h.point;
                if (dr != null && dr.door.state != "blown")
                {
                    Game.I.map.DamageDoor(dr.door, E, dir, h.point);
                    if (E > 230 && dr.door.state != "blown") { E -= 200; FX.I.Chips(h.point + dir * 0.06f, dir * 1.5f, 6, new Color(0.5f, 0.36f, 0.2f)); continue; }
                    if (dr.door.state == "blown") continue;
                }
                if (throughWalls) { FX.I.Emit(h.point, Random.insideUnitSphere, new Color(0.6f, 0.95f, 1), 0.5f, 1); continue; }
                return res;
            }
            return res;
        }
        static void Sev(ref ShotResult r, int sev, string txt) { if (sev > r.sev) { r.sev = sev; r.txt = txt; } }
    }

    /// <summary>Loose, physics-driven objects the Grav Lifter can grab: crates, boxes, barrels, door planks, severed parts.</summary>
    public class Prop : MonoBehaviour
    {
        public string label = "object"; public static readonly List<Prop> All = new List<Prop>(); Rigidbody rb;
        public Rigidbody Body { get { if (!rb) rb = GetComponent<Rigidbody>(); return rb; } }
        void OnEnable() => All.Add(this); void OnDisable() => All.Remove(this);
    }

    /// <summary>The player's weapons: loadout of four, quick keys, every weapon kind, view models, recoil, reload, aim-down-sights.</summary>
    public class WeaponController : MonoBehaviour
    {
        public Player player; public Camera cam; public bool reloading; float reloadT, fireT, switchT, kick, bloom, shake, flashT; public float ads, draw, drawHeld;
        public readonly Dictionary<string, Vector2Int> ammo = new Dictionary<string, Vector2Int>(); public readonly List<string> loadout = new List<string>();
        readonly Dictionary<string, VM> models = new Dictionary<string, VM>(); Transform holder; Light muzzleLight; AudioSource src; bool held, rHeld; float swingT, stabT; bool swingDone, stabDone; int swingSide = 1, katI;
        public string cur = "pistol"; public WeaponDef W => WeaponDef.Get(cur);
        public int SlashDir = 0; public bool wheelOpen; public Vector2 wheelV; public int wheelPick = -1; public static readonly string[] SlashArrows = { "→", "↗", "↑", "↖", "←", "↙", "↓", "↘" };
        public float railCharge = -1;

        public void Init(Player p, Camera c)
        {
            player = p; cam = c; holder = new GameObject("ViewModels").transform; holder.SetParent(cam.transform, false);
            muzzleLight = new GameObject("MuzzleLight").AddComponent<Light>(); muzzleLight.type = LightType.Point; muzzleLight.range = 8; muzzleLight.color = new Color(1f, 0.7f, 0.4f); muzzleLight.intensity = 0; muzzleLight.transform.SetParent(holder, false); muzzleLight.transform.localPosition = new Vector3(0.15f, -0.1f, 0.7f);
            src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0;
            foreach (var kv in WeaponDef.Variants) WeaponDef.SetVariant(kv.Key, Game.I.settings.Variant(kv.Key));
            LoadLoadout(); Refill(); Select(loadout[0], true);
        }

        public void LoadLoadout() { loadout.Clear(); foreach (var id in Game.I.settings.loadout) if (WeaponDef.Get(id) != null && Progress.Owned(id) && !loadout.Contains(id)) loadout.Add(id); if (loadout.Count == 0) loadout.AddRange(new[] { "pistol", "rifle", "shotgun", "knife" }); if (!loadout.Contains(cur)) { if (models.Count > 0) Select(loadout[0], true); else cur = loadout[0]; } }
        public VM Model(string id)
        {
            if (models.TryGetValue(id, out var vm) && vm.root) return vm;
            var w = WeaponDef.Get(id); vm = id == "boxgun" && Specials.boxReceiver != null ? ViewModels.BuildMash(w, Specials.boxReceiver, Specials.boxBarrel, holder) : ViewModels.Build(w, holder); models[id] = vm; return vm;
        }
        /// <summary>Rebuild a model (after a variant change or a new Mystery Box roll).</summary>
        public void Rebuild(string id) { if (models.TryGetValue(id, out var vm) && vm.root) Destroy(vm.root.gameObject); models.Remove(id); if (cur == id) { Model(id).root.gameObject.SetActive(true); } }

        public int Reserve(WeaponDef w)
        {
            float r = w.reserve; if (w.kind == "gun" || w.kind == "eldritch" || w.kind == "bow") r *= 1 + 0.25f * Progress.Skill("m_pockets");
            if (w.id == "flamer") r *= 1 + 0.33f * Progress.Skill("b_ord"); if (w.id == "melt") r += Progress.Skill("b_ord"); return Mathf.RoundToInt(r);
        }
        public bool superShot; bool bladeHeld;
        /// <summary>Fire the current weapon now (Dead Eye uses this after turning the camera to each mark).</summary>
        public void ForceFire() { var w = W; if (switchT > 0 || reloading) return; if (ammo[w.id].x <= 0) return; Fire(w); fireT = 60f / Mathf.Max(1, w.rpm); }
        public void Shake(float k) { shake = Mathf.Max(shake, k); kick = Mathf.Max(kick, k); }
        public void RefillOne(string id) { var w = WeaponDef.Get(id); if (w != null) ammo[id] = new Vector2Int(w.mag, Reserve(w)); if (id == cur) reloading = false; }
        public void DropModel(string id) { if (models.TryGetValue(id, out var vm)) { if (vm.root) Destroy(vm.root.gameObject); models.Remove(id); } }
        public void Refill() { ammo.Clear(); foreach (var b in WeaponDef.All) { var w = WeaponDef.Get(b.id); ammo[w.id] = new Vector2Int(w.mag, Reserve(w)); } reloading = false; }
        public int Mag => ammo.TryGetValue(cur, out var a) ? a.x : 0; public int Res => ammo.TryGetValue(cur, out var a) ? a.y : 0;
        void SetAmmo(string id, int mag, int res) => ammo[id] = new Vector2Int(mag, res);

        public void Select(string id, bool force = false)
        {
            if (!force && (id == cur || !loadout.Contains(id) && !(Specials.I && Specials.I.TempOwned(id)))) return;
            if (models.TryGetValue(cur, out var old) && old.root) old.root.gameObject.SetActive(false);
            cur = id; Model(id).root.gameObject.SetActive(true); reloading = false; draw = 0; drawHeld = 0; switchT = 0.35f * (1 - 0.2f * Progress.Skill("b_hands")); bloom = 0; railCharge = -1;
            if (Specials.I) Specials.I.OnSwitch();
        }
        public void SelectSlot(int i) { if (i < loadout.Count) Select(loadout[i]); }
        public void Cycle(int d) { if (loadout.Count == 0) return; int k = Mathf.Max(0, loadout.IndexOf(cur)); Select(loadout[(k + d + loadout.Count) % loadout.Count]); }
        public void CycleMelee() { var ids = new List<string>(); foreach (var x in new[] { "knife", "sickle", "katana" }) if (loadout.Contains(x)) ids.Add(x); if (ids.Count == 0) { Game.I.Feed("No melee weapon in your loadout"); return; } Select(ids[(ids.IndexOf(cur) + 1) % ids.Count]); }
        public void QuickSelect(string id) { if (!loadout.Contains(id)) { Game.I.Feed(WeaponDef.Get(id).name + " is not in your loadout"); return; } Select(id); }
        public void QuickNade()
        {
            if (!player.alive) return; if (!loadout.Contains("melt")) { Game.I.Feed("Melt grenades are not in your loadout"); return; }
            var a = ammo["melt"]; bool inf = Game.I.settings.infinite; if (a.x > 0) a.x--; else if (a.y > 0 || inf) { if (!inf) a.y--; } else { Game.I.Feed("No melt grenades left"); return; }
            ammo["melt"] = a; Specials.I.ThrowNade("melt", cam);
        }

        public void StartReload()
        {
            var w = W; var a = ammo[w.id]; if (reloading || a.x >= w.mag || w.kind == "melee" || w.kind == "grav" || w.kind == "rift" || w.kind == "psi") return;
            if (w.kind == "wand") { reloading = true; reloadT = w.reload * Rf(); return; }
            if (a.y <= 0 && !Game.I.settings.infinite) return; reloading = true; reloadT = (w.PerShell ? w.reload + 0.25f : w.reload) * Rf(); SFX.Play("reload", null, 0.5f);
            if (!w.PerShell) DropMag(Model(w.id));
        }
        static float Rf() => 1 - 0.15f * Progress.Skill("m_reload");
        /// <summary>The empty magazine falls out of the gun and lands on the floor.</summary>
        void DropMag(VM vm)
        {
            if (vm == null || !vm.mag || !vm.mag.gameObject.activeInHierarchy) return; var drop = new GameObject("DroppedMag"); drop.layer = Game.LayerProp; drop.transform.SetPositionAndRotation(vm.mag.position, vm.mag.rotation);
            Bounds bb = new Bounds(); bool first = true;
            foreach (var r in vm.mag.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf) continue; var c = new GameObject("m"); c.layer = Game.LayerProp; c.transform.SetParent(drop.transform, false);
                c.transform.localPosition = vm.mag.InverseTransformPoint(r.transform.position); c.transform.localRotation = Quaternion.Inverse(vm.mag.rotation) * r.transform.rotation; c.transform.localScale = r.transform.localScale;
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh; var nr = c.AddComponent<MeshRenderer>(); nr.sharedMaterials = r.sharedMaterials;
                var lb = new Bounds(c.transform.localPosition + Vector3.Scale(mf.sharedMesh.bounds.center, c.transform.localScale), Vector3.Scale(mf.sharedMesh.bounds.size, c.transform.localScale)); if (first) { bb = lb; first = false; } else bb.Encapsulate(lb);
            }
            if (first) { Destroy(drop); return; }
            var bc = drop.AddComponent<BoxCollider>(); bc.center = bb.center; bc.size = Vector3.Max(bb.size, Vector3.one * 0.01f);
            var rb = drop.AddComponent<Rigidbody>(); rb.mass = 0.3f; PhysicsTuning.Loose(rb, PhysicsTuning.Metal); rb.linearVelocity = cam.transform.up * -0.8f + cam.transform.forward * 0.3f; rb.angularVelocity = Random.insideUnitSphere * 3;
            FX.I.Track(drop); Destroy(drop, 10);
        }
        void FinishReload()
        {
            var w = W; var a = ammo[w.id]; bool inf = Game.I.settings.infinite;
            if (w.kind == "wand") { SetAmmo(w.id, w.mag, a.y); reloading = false; return; }
            if (w.PerShell) { a.x++; if (!inf) a.y--; SetAmmo(w.id, a.x, a.y); if (a.x < w.mag && (a.y > 0 || inf)) { reloadT = w.reload * Rf(); SFX.Play("click", null, 0.4f); return; } reloading = false; return; }
            int need = w.mag - a.x, take = inf ? need : Mathf.Min(need, a.y); SetAmmo(w.id, a.x + take, inf ? a.y : a.y - take); reloading = false;
        }

        public float CurrentSpread()
        {
            var w = W; if (Specials.I && Specials.I.deadEyeAim) return w.adsSpread * 0.15f;
            float s = Mathf.Lerp(w.spread, w.adsSpread * 0.45f, ads); if (w.fan && ads < 0.5f) s *= 2.6f; s += w.spread * 0.7f * Mathf.Clamp(player.speed / 4f, 0, 1.4f) * (1 - ads); if (player.crouched) s *= 0.78f;
            return (s + bloom) * (1 - 0.12f * Progress.Skill("m_steady"));
        }
        public Vector3 SpreadDir(float sp) { float an = Random.value * Mathf.PI * 2, rr = Mathf.Sqrt(Random.value) * sp; var t = cam.transform; return (t.forward + t.right * Mathf.Cos(an) * rr + t.up * Mathf.Sin(an) * rr).normalized; }
        public Vector3 MuzzleWorld => cam.transform.TransformPoint(new Vector3(0.1f, -0.08f, 0.6f));

        public void Tick(float dt)
        {
            var w = W; var vm = Model(cur); fireT -= dt; if (switchT > 0) switchT -= dt; bloom = Mathf.Max(0, bloom - dt * 0.12f); shake = Mathf.Max(0, shake - dt * 4);
            bool trig = BPInput.Fire.IsPressed() || TouchUI.Fire, aimIn = BPInput.Aim.IsPressed() || TouchUI.Aim, aimDown = BPInput.Aim.WasPressedThisFrame() || TouchUI.AimPressed;
            if (Game.I.hud.Blocking) { trig = false; aimIn = false; aimDown = false; }
            bool canAds = !w.NoAds && w.kind != "psi" && w.kind != "grav" && w.kind != "rift";
            ads = Mathf.MoveTowards(ads, canAds && aimIn && switchT <= 0 && !player.sprinting ? 1 : 0, dt * 6f * w.adsMul);
            // slash direction wheel
            if (w.id == "slash" && BPInput.Wheel.IsPressed()) { if (!wheelOpen) { wheelOpen = true; wheelV = Vector2.zero; wheelPick = -1; } }
            else if (wheelOpen) { wheelOpen = false; if (wheelPick >= 0) SlashDir = wheelPick; }
            if (wheelOpen) { trig = false; }
            if (reloading) { reloadT -= dt; if (reloadT <= 0) FinishReload(); }
            if (!player.alive) trig = false;
            // Blade Mode (Settings ▸ Slice angle): hold aim with a blade or the Slash Projector to slow time and steer the cut; each click cuts
            bool bladeOk = Game.I.settings.sliceMode == 1 && ((w.kind == "melee" && w.id != "knife") || w.id == "slash") && player.alive && switchT <= 0 && !Game.I.hud.Blocking && Game.I.state == Game.State.Playing && !Specials.I.deadEyeActive;
            if (bladeOk && aimIn) { Specials.I.BladeEnter(); if (trig && !bladeHeld) { Specials.I.BladeCut(w, cam); swingT = 0.2f; swingDone = true; swingSide = -swingSide; } bladeHeld = trig; trig = false; aimDown = false; }
            else Specials.I.BladeExit();
            // right-button actions
            if (switchT <= 0 && player.alive && aimDown && !Game.I.hud.Blocking)
            {
                if (w.kind == "melee") { if (!(bladeOk && Game.I.settings.sliceMode == 1)) StartStab(w); } else if (w.kind == "grav") Specials.I.GravToggle(cam); else if (w.kind == "rift") Specials.I.PlacePortal(1, cam); else if (w.kind == "psi") Specials.I.PsiCycle();
            }
            if (w.kind != "grav" && w.kind != "psi") Specials.I.GravDropIfHeld();
            // melee swings
            if (swingT > 0) { swingT -= dt; if (!swingDone && swingT < 0.24f) { swingDone = true; Specials.I.Melee(w, cam, swingSide, katI, false); } }
            if (stabT > 0) { stabT -= dt; if (!stabDone && stabT < 0.18f) { stabDone = true; Specials.I.Melee(w, cam, swingSide, katI, true); } }
            // rail charge
            if (railCharge >= 0) { railCharge -= dt; if (railCharge < 0) { railCharge = -1; if (player.alive) Specials.I.RailShot(w, cam); kick = 1.4f; shake = 1.3f; player.AddRecoil(7.5f, 0); Flash(4); } }
            // the trigger
            if (Specials.I.deadEyeActive) { held = trig; }
            else if (w.kind == "bow")
            {
                var a = ammo["bow"];
                if (trig && player.alive && switchT <= 0 && !reloading && a.x > 0) { if (draw == 0) SFX.Play("string", null, 0.3f, 0.6f); draw = Mathf.Min(1, draw + dt / 0.75f); if (draw >= 1) drawHeld += dt; }
                else { if (draw > 0.15f && player.alive) LooseArrow(w); draw = 0; drawHeld = 0; }
            }
            else if (trig && switchT <= 0)
            {
                bool fan = w.fan && ads < 0.5f;
                if ((w.auto || fan || !held) && fireT <= 0) { Fire(w); fireT = fan ? 0.16f : 60f / w.rpm; }
                held = true;
            }
            else if (!trig) held = false;
            if (flashT > 0) { flashT -= dt; if (flashT <= 0) muzzleLight.intensity = 0; }
            kick = Mathf.Lerp(kick, 0, dt * 14f);
            Pose(w, vm, dt);
        }

        void Pose(WeaponDef w, VM vm, float dt)
        {
            var g = vm.root; float a = ads; float bobA = Mathf.Min(1, player.speed / 4f) * (1 - a * 0.85f); float bob = player.bob;
            Vector3 hip = new Vector3(0.16f, -0.15f, 0.34f), adsP = new Vector3(0, -vm.sightY, w.scoped ? 0.12f : vm.adsZ);
            var pos = Vector3.Lerp(hip, adsP, a) + new Vector3(Mathf.Cos(bob) * 0.012f * bobA, -Mathf.Abs(Mathf.Sin(bob)) * 0.01f * bobA, -kick * 0.045f);
            if (player.sprinting) pos += new Vector3(-0.04f, -0.05f, 0); if (reloading && !w.PerShell) pos.y -= 0.06f; if (switchT > 0) pos.y -= switchT * 0.6f;
            float rx = -kick * 4f * (1 - a * 0.6f) + (reloading ? 20 : 0) + (player.sprinting ? 18 : 0), ry = player.sprinting ? -28 : 0, rz = reloading ? 22 : 0;
            // melee swing and stab arcs
            if (swingT > 0) { float k = 1 - swingT / 0.38f; float arc = Mathf.Sin(k * Mathf.PI); pos += new Vector3(-swingSide * (k - 0.5f) * 0.4f, 0.05f * arc, 0.12f * arc); rz += -swingSide * 70 * (k - 0.5f); ry += swingSide * 50 * (k - 0.5f); rx += -30 * arc; }
            if (stabT > 0) { float k = 1 - stabT / 0.32f; float arc = Mathf.Sin(k * Mathf.PI); pos += new Vector3(-0.08f * arc, 0.02f, 0.22f * arc); rx += 10 * arc; }
            if (w.kind == "bow") { pos += new Vector3(-0.1f, 0.03f, 0); rz += 12; if (vm.bowString) vm.bowString.localPosition = new Vector3(0, 0, 0.24f - draw * 0.2f); if (vm.arrow) { vm.arrow.gameObject.SetActive(ammo["bow"].x > 0 && !reloading); vm.arrow.localPosition = new Vector3(0.004f, 0.01f, 0.24f - draw * 0.2f + 0.37f); } if (drawHeld > 2.5f) pos += Random.insideUnitSphere * 0.004f * (drawHeld - 2.5f); }
            if (vm.disc) vm.disc.localRotation = Quaternion.Euler(0, Time.time * 900, 0);
            if (vm.indicator) vm.indicator.localRotation = Quaternion.Euler(0, 0, SlashDir * 45);
            if (vm.coil) vm.coil.localScale = Vector3.one * (1 + 0.15f * Mathf.Sin(Time.time * 12) + (railCharge >= 0 ? (0.3f - railCharge) * 4 : 0));
            if (vm.pilot) vm.pilot.localScale = Vector3.one * (0.8f + Random.value * 0.4f);
            // pistol slide cycles back on each shot; the magazine is out for the first part of a reload
            if (vm.slide) vm.slide.localPosition = new Vector3(0, 0, -Mathf.Clamp01(kick) * 0.032f);
            if (vm.mag) vm.mag.gameObject.SetActive(!reloading || w.PerShell || reloadT < w.reload * 0.45f);
            if (shake > 0) pos += Random.insideUnitSphere * 0.003f * shake;
            g.localPosition = pos; g.localRotation = Quaternion.Euler(rx, ry, rz);
            g.gameObject.SetActive(!(w.scoped && ads > 0.85f) && !player.thirdPerson && !Game.I.KillCamActive);
            if (vm.flash) { vm.flash.gameObject.SetActive(flashT > 0); if (flashT > 0) vm.flash.localRotation = Quaternion.Euler(0, 0, Random.Range(0, 360)); }
        }

        void Flash(float k) { muzzleLight.intensity = k; flashT = 0.05f; }
        public void Kick(float k) { kick = Mathf.Max(kick, k); }
        void StartStab(WeaponDef w) { if (swingT > 0 || stabT > 0) return; stabT = 0.32f; stabDone = false; SFX.Play("whoosh", null, 0.3f, 1.3f); }

        bool UseAmmo(WeaponDef w) { var a = ammo[w.id]; if (a.x <= 0) { SFX.Play("click", null, 0.5f); StartReload(); return false; } if (reloading) { if (w.PerShell) reloading = false; else return false; } SetAmmo(w.id, a.x - 1, a.y); return true; }

        void Fire(WeaponDef w)
        {
            var S = Specials.I;
            switch (w.kind)
            {
                case "psi": S.PsiFire(cam); return;
                case "melee": if (swingT > 0 || stabT > 0) return; swingT = 0.38f; swingDone = false; swingSide = -swingSide; katI = (katI + 1) % 8; Game.I.stats.shots++; SFX.Play("whoosh", null, 0.35f); if (w.id == "sickle") SFX.Play("eerie", null, 0.25f, 2.2f); return;
                case "flame": if (!UseAmmo(w)) return; S.FlameTick(w, cam); Flash(1.2f); return;
                case "grav": S.GravPrimary(cam); kick = 1.2f; return;
                case "rift": S.PlacePortal(0, cam); kick = 0.6f; return;
                case "taser": case "beanbag": if (!UseAmmo(w)) return; Game.I.stats.shots++; S.LessLethal(w, cam, CurrentSpread()); player.AddRecoil(w.recoil * 48, 0); kick = 1; if (w.kind == "beanbag") { Flash(2); src.PlayOneShot(SFX.Shot(w.sf, w.sd, w.sv), 0.6f); } return;
                case "wand": if (ammo[w.id].x <= 0 || reloading) { if (!reloading) StartReload(); return; } SetAmmo(w.id, ammo[w.id].x - 1, ammo[w.id].y); S.CastWand(cam); return;
                case "throw": { var a = ammo[w.id]; if (a.x > 0 && !reloading) { SetAmmo(w.id, a.x - 1, a.y); S.ThrowNade(w.id == "flash" ? "flash" : w.id == "singnade" ? "sing" : w.id == "impball" ? "imp" : "melt", cam); kick = 1; if (a.y > 0 || Game.I.settings.infinite) { reloading = true; reloadT = w.reload; } } return; }
            }
            if (!UseAmmo(w)) return; Game.I.stats.shots++;
            var o = cam.transform.position; float sp = CurrentSpread(); if (S.secretsShot != null) S.secretsShot(o, cam.transform.forward);
            if (w.kind == "wild" || w.kind == "eldritch") { if (!S.Exotic(w, cam, sp)) { SetAmmo(w.id, ammo[w.id].x + 1, ammo[w.id].y); return; } if (w.id == "rail") { railCharge = 0.3f; SFX.Play("charge", null, 0.5f); return; } }
            else
            {
                string best = null; int bs = -1; bool any = false; var muzzle = MuzzleWorld;
                if (Specials.I.timeStop)
                {
                    // time is stopped: the rounds hang in the air and land when it starts again
                    for (int i = 0; i < w.pellets; i++) { var d = SpreadDir(sp); var mz = muzzle; Specials.I.Hang(o, d, () => { var r = Ballistics.Shoot(w, o, d, 200f); if (r.hit) { Game.I.stats.hits++; Game.I.zm.Hit(); Game.I.hud.HitMarker(); } if (r.txt != null) Game.I.hud.HitText(r.txt); FX.I.Tracer(mz, r.end); }); }
                }
                else
                for (int i = 0; i < w.pellets; i++)
                {
                    var d = SpreadDir(sp); var r = Ballistics.Shoot(w, o, d, 200f);
                    if (r.hit) any = true; if (r.e && w.fx == "inc" && Random.value < 0.35f) r.e.Ignite(); if (r.txt != null && r.sev > bs) { bs = r.sev; best = r.txt; }
                    if (Game.I.settings.tracers && i % 3 == 0) FX.I.Tracer(muzzle, r.end);
                }
                if (any) { Game.I.stats.hits++; Game.I.zm.Hit(); SFX.Play("flesh", null, 0.4f); Game.I.hud.HitMarker(); }
                if (best != null) Game.I.hud.HitText(best);
            }
            float rk = w.recoil * Random.Range(0.8f, 1.2f) * (1 - ads * 0.35f) * (player.crouched ? 0.8f : 1f);
            player.AddRecoil(rk * 60f, Random.Range(-0.5f, 0.5f) * w.recoil * 30f); kick = 1; shake = Mathf.Min(1.2f, shake + w.recoil * 7 + 0.06f); bloom = Mathf.Min(bloom + w.recoil * 0.5f, 0.06f);
            if (w.kind == "eldritch") { if (w.id != "lantern" || Random.value < 0.2f) SFX.Play("eerie", null, 0.35f, Mathf.Clamp(w.sf / 140f, 0.3f, 3f)); }
            else if (w.kind == "wild") { if (w.id != "tesla" && w.id != "cryo" && w.id != "disray" && w.id != "meltray" || Random.value < 0.25f) SFX.Play(w.id == "tesla" ? "zap" : w.id == "cryo" || w.id == "meltray" ? "hiss" : "portal", null, 0.35f, Mathf.Clamp(w.sf / 1500f, 0.4f, 3f)); }
            else { Flash(3); src.pitch = Random.Range(0.94f, 1.06f); src.PlayOneShot(SFX.Shot(w.sf, w.sd, w.sv), 0.7f); }
            if (w.kind == "gun") Game.I.Noise(player.transform.position, 32 * w.noise);
        }

        void LooseArrow(WeaponDef w)
        {
            var a = ammo["bow"]; if (a.x <= 0) return; SetAmmo("bow", a.x - 1, a.y); Game.I.stats.shots++;
            var d = SpreadDir(CurrentSpread() + Mathf.Max(0, drawHeld - 2.5f) * 0.01f); Specials.I.Arrow(cam.transform.position + d * 0.4f, d * (22 + 48 * draw), w.energy * draw * draw, superShot);
            SFX.Play("string", null, 0.5f); Game.I.Noise(player.transform.position, 8);
            if (a.y > 0 || Game.I.settings.infinite) { reloading = true; reloadT = w.reload; }
        }
    }
}
