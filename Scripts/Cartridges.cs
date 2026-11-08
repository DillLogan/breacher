using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    public enum Bullet { FMJ, JHP, SoftPoint, AP, Buck, Lead }

    /// <summary>
    /// A cartridge: bullet mass, diameter, ballistic coefficient and construction, and how deep it goes in tissue (10% ballistic-gel equivalent, cm) at a reference
    /// velocity. Muzzle velocity comes from the weapon's muzzle energy (so barrels, hand-loads and subsonic rounds change it). Figures are typical published
    /// values: a 9 mm hollow point goes about 30-36 cm and expands, so it usually stays inside a torso; 9 mm ball goes about 60 cm and often exits;
    /// 5.56 ball yaws and fragments above about 760 m/s; 7.62 rifle rounds yaw late and usually go through; 00 buckshot pellets go about 35 cm each.
    /// </summary>
    public class Cartridge
    {
        public string name; public float mass, diameter, bc, gelRef, vRef, expand = 1, expandMinV, fragV = 99999, fragDepth = 0.11f, fragK = 1, yawDepth = 99, yawK = 1, armor = 1; public Bullet type;
        /// <summary>Drag constant per metre at unit speed (retained velocity v = v0 e^(-k x)).</summary>
        public float K => 0.00034f / Mathf.Max(0.03f, bc);
        public float V0(float energy) => Mathf.Sqrt(2 * Mathf.Max(1, energy) / mass);
        /// <summary>Tissue penetration budget (cm of gel) at a speed; hollow points that are too slow don't open and go deeper.</summary>
        public float Budget(float v) { float b = gelRef * Mathf.Pow(Mathf.Max(0, v) / vRef, 1.1f); if (type == Bullet.JHP && v < expandMinV) b *= 1.6f; return b; }
        Cartridge Copy() => (Cartridge)MemberwiseClone();

        static Cartridge C(string n, float g, float dmm, float bc, float gel, float vref, Bullet t) => new Cartridge { name = n, mass = g / 1000f, diameter = dmm / 1000f, bc = bc, gelRef = gel, vRef = vref, type = t };
        static readonly (string key, Cartridge c)[] Table =
        {
            ("9×19", C("9×19 mm", 8.0f, 9.0f, 0.15f, 60, 360, Bullet.FMJ)),
            ("4.6×30", With(C("4.6×30 mm", 2.0f, 4.6f, 0.18f, 34, 725, Bullet.AP), c => { c.yawDepth = 0.12f; c.yawK = 1.5f; c.armor = 2.4f; })),
            (".45 ACP", C(".45 ACP", 15f, 11.5f, 0.19f, 62, 260, Bullet.FMJ)),
            (".50 AE", C(".50 AE", 19.4f, 12.7f, 0.16f, 70, 450, Bullet.FMJ)),
            (".44", C(".44 Magnum", 15.6f, 10.9f, 0.17f, 75, 440, Bullet.SoftPoint)),
            (".357", C(".357 Magnum", 10.2f, 9.1f, 0.16f, 62, 430, Bullet.FMJ)),
            (".45 Colt", C(".45 Colt", 16.5f, 11.5f, 0.15f, 50, 280, Bullet.Lead)),
            ("5.56", With(C("5.56×45 mm", 4.0f, 5.7f, 0.30f, 50, 920, Bullet.FMJ), c => { c.fragV = 760; c.fragDepth = 0.11f; c.fragK = 2.6f; c.yawDepth = 0.12f; c.yawK = 1.6f; c.armor = 1.4f; })),
            ("7.62×39", With(C("7.62×39 mm", 8.0f, 7.9f, 0.27f, 65, 715, Bullet.FMJ), c => { c.yawDepth = 0.25f; c.yawK = 2.0f; c.armor = 1.4f; })),
            ("7.62×51", With(C("7.62×51 mm", 9.5f, 7.8f, 0.40f, 70, 850, Bullet.FMJ), c => { c.fragV = 760; c.fragDepth = 0.16f; c.fragK = 2.0f; c.yawDepth = 0.16f; c.yawK = 2.2f; c.armor = 1.4f; })),
            ("7.62×54", With(C("7.62×54R", 9.7f, 7.9f, 0.40f, 72, 830, Bullet.FMJ), c => { c.yawDepth = 0.18f; c.yawK = 2.1f; c.armor = 1.4f; })),
            (".338", With(C(".338 Lapua", 16.2f, 8.6f, 0.65f, 95, 900, Bullet.FMJ), c => { c.yawDepth = 0.2f; c.yawK = 2.4f; c.armor = 2.0f; })),
            ("12 ga", With(C("12 ga 00 buck", 3.5f, 8.4f, 0.07f, 35, 400, Bullet.Buck), c => c.armor = 0.6f)),
        };
        static Cartridge With(Cartridge c, System.Action<Cartridge> f) { f(c); return c; }
        static readonly Dictionary<string, Cartridge> named = new Dictionary<string, Cartridge>();
        /// <summary>A factory (ball) cartridge by calibre key, e.g. "5.56", "7.62×51", "12 ga".</summary>
        public static Cartridge Named(string key) { if (named.TryGetValue(key, out var c)) return c; foreach (var (k, tc) in Table) if (k == key || tc.name.Contains(key)) { c = tc.Copy(); break; } if (c == null) c = Table[0].c.Copy(); named[key] = c; return c; }

        /// <summary>The round a weapon fires, from its calibre, its gunsmith ammo part, and what police and militaries actually load (hollow points in handguns and SMGs, ball in rifles).</summary>
        public static Cartridge For(WeaponDef w)
        {
            if (w == null || w.kind != "gun") return null; Cartridge c = null;
            foreach (var (key, tc) in Table) if (w.cal != null && w.cal.Contains(key)) { c = tc.Copy(); break; }
            if (c == null) { float v = w.energy > 1000 ? 820 : 380; c = C(w.cal ?? "round", 2000 * w.energy / (v * v), 8, 0.25f, 25 + w.energy / 60f, v, Bullet.FMJ); }
            bool handgun = w.id == "pistol" || w.id == "smg" || w.id == "revolver";
            // factory loads: duty hollow points in handguns and SMGs (not the 4.6 PDW or the old lead .45 Colt)
            if (handgun && c.type == Bullet.FMJ && !c.name.StartsWith("4.6")) { c.type = Bullet.JHP; c.gelRef *= 0.58f; c.expand = 1.65f; c.expandMinV = c.name.StartsWith("9") ? 280 : 230; c.armor = 0.7f; }
            if (c.type == Bullet.SoftPoint) { c.expand = 1.4f; c.gelRef *= 0.8f; }
            // gunsmith ammo
            var ap = w.ammoPart != null ? Gunsmith.Part(w.ammoPart) : Gunsmith.Supports(w.id) ? Gunsmith.Equipped(w.id, GSlot.Ammo) : null;
            if (ap != null && c.type != Bullet.Buck)
            {
                if (ap.id == "a_hp" && c.type != Bullet.JHP) { c.type = Bullet.JHP; c.gelRef *= 0.58f; c.expand = 1.65f; c.expandMinV = 260; c.armor = 0.7f; c.fragV = 99999; }
                else if (ap.id == "a_ap") { if (c.type == Bullet.JHP) { c.gelRef /= 0.58f; c.expand = 1; } c.type = Bullet.AP; c.gelRef *= 1.35f; c.armor = Mathf.Max(c.armor, 1) * 1.8f; c.fragV = 99999; }
                else if (ap.id == "a_hand" && c.type == Bullet.JHP) c.expand *= 1.1f;
            }
            if (c.type == Bullet.Buck && w.pellets <= 1) { c.name = "12 ga slug"; c.mass = 28f / 1000f; c.diameter = 0.018f; c.bc = 0.1f; c.gelRef = 50; c.type = Bullet.Lead; c.expand = 1.4f; }
            return c;
        }
    }

    public static partial class Ballistics
    {
        /// <summary>
        /// A real round: flies a curved path (gravity and air drag slow and drop it, so it hits with less energy far away), goes through tissue until its
        /// penetration runs out (bone costs much more than flesh, lungs less), opens up or tumbles/fragments as its construction says, punches through thin
        /// walls, doors and crates, stops in thick or hard ones, and skips off hard surfaces it meets at a shallow angle.
        /// </summary>
        public static ShotResult ShotC(Vector3 o, Vector3 dir, float energy, Cartridge c, float maxD, Hostile shooter, bool holes = true, int depth = 0, float wound = 0.018f)
        {
            var res = new ShotResult { end = o + dir * Mathf.Min(60, maxD), sev = -1 };
            float v = c.V0(energy), m = c.mass, k = c.K; Vector3 p = o, vel = dir.normalized * v; float travelled = 0; var seen = new HashSet<Collider>(); var hitBodies = new Dictionary<Hostile, float>();
            float E() => 0.5f * m * v * v;
            const float Seg = 3f;
            while (travelled < maxD && v > 40)
            {
                float len = Mathf.Min(Seg, maxD - travelled); float dt = len / Mathf.Max(1, v); var vNext = vel + Physics.gravity * dt; var segEnd = p + (vel + vNext) * 0.5f * dt; var sd = segEnd - p; float sl = sd.magnitude; var d = sd / Mathf.Max(1e-5f, sl);
                var hits = Physics.RaycastAll(p, d, sl, Game.MaskShots, QueryTriggerInteraction.Collide); System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                bool stopped = false; Vector3 resume = segEnd;
                foreach (var h in hits)
                {
                    if (seen.Contains(h.collider)) continue; seen.Add(h.collider); if (h.collider.isTrigger && !h.collider.GetComponent<Portal>()) continue;
                    var portal = h.collider.GetComponent<Portal>();
                    if (portal != null) { if (Specials.I && Specials.I.PortalPair(portal, out var other)) { Specials.I.MapThrough(portal, h.point, d, out var np, out var nd); if (depth < 3) { var sub = ShotC(np + other.n * 0.05f, nd, E(), c, Mathf.Max(0.5f, maxD - travelled - h.distance), shooter, holes, depth + 1, wound); Merge(ref res, sub); if (Game.I.settings.tracers) FX.I.Tracer(np, sub.end); } res.end = h.point; return res; } continue; }
                    { var an = h.collider.GetComponent<Animal>(); if (an) { if (shooter == null) { an.Pop(d); res.hit = true; Sev(ref res, 2, "The " + an.kind + " popped"); } res.end = h.point; return res; } }
                    if (h.collider.gameObject.layer == Game.LayerPlayer) { if (shooter != null && !shooter.IsThrall && !shooter.ally) { Game.I.player.Shot(h.point, E(), c, Mathf.Clamp(E() / 120f, 6, 30) * shooter.dmgMul, shooter.tag2 + " shot you"); res.end = h.point; return res; } continue; }
                    var bp = h.collider.GetComponent<BodyPart>();
                    if (bp != null)
                    {
                        var e = bp.owner; if (shooter != null && e == shooter) continue; if (e.Gone) continue; float Ein = E();
                        if (e.frozen) { if (!e.shattered) { e.Shatter(d); res.hit = true; Sev(ref res, 3, e.tag2 + " shattered"); } v *= 0.6f; res.end = h.point; continue; }
                        if (e.shrunk && e.state != HState.Dead) { e.lastHitDirPub = d; e.Die("Squashed while tiny"); res.hit = true; Sev(ref res, 3, e.tag2 + " popped"); res.end = h.point; v *= 0.7f; continue; }
                        Vector3 exit = h.point + d * 0.08f; if (h.collider.Raycast(new Ray(h.point + d * 1.2f, -d), out var ex, 1.2f)) exit = ex.point;
                        float L = Vector3.Distance(h.point, exit);
                        // armour: soft vests stop handgun rounds, plates stop rifle ball, armour-piercing and magnum rifle rounds defeat them
                        bool helm = bp.zone == Zone.Head && e.helmet && Random.value < 0.6f;
                        if ((bp.armored && e.armor > 0 || helm) && Random.value < 0.85f)
                        {
                            float stop = helm ? 650f : e.armor == 2 ? 4800f : 650f;
                            if (Ein * c.armor < stop * e.armorIntegrity) { e.armorIntegrity *= 0.93f; e.TakeHit(new HitInfo { byPlayer = shooter == null, part = bp, point = h.point, exit = h.point, dir = d, normal = h.normal, energy = Ein, deposited = Ein * 0.08f, armorStop = true, organs = new List<Organ>() }); FX.I.Sparks(h.point, h.normal, 4); res.hit = true; Sev(ref res, 1, e.tag2 + ": stopped by armour (" + c.name + ")"); res.end = h.point; res.e = e; return res; }
                            v *= 0.75f; e.armorIntegrity *= 0.8f; Ein = E();
                        }
                        // penetration: gel-equivalent depth of this part (bone costs much more; lungs less)
                        Vector3 A = bp.transform.InverseTransformPoint(h.point), B = bp.transform.InverseTransformPoint(exit);
                        bool bone = BodyBuilder.SegSegDist(A, B, bp.boneA, bp.boneB) < bp.boneR + 0.004f;
                        float need = L * 100f * (bp.tissueResist / 900f) + (bone ? (bp.zone == Zone.Head ? 12f : 6f) : 0f) + 1f; // +1: skin
                        float budget = c.Budget(v); bool through = budget >= need; float frac = through ? 1 : Mathf.Clamp01(budget / need);
                        float vOut = through ? v * Mathf.Sqrt(Mathf.Max(0, 1 - need / budget)) : 0; float Eout = 0.5f * m * vOut * vOut;
                        // wound size along the track: hollow points open after ~2 cm; ball rifle rounds tumble, and fast 5.56/7.62 fragment
                        hitBodies.TryGetValue(e, out float d0); float d1 = d0 + L * frac; hitBodies[e] = d1;
                        float wk = 1; if (c.type == Bullet.JHP && v >= c.expandMinV && d1 > 0.02f) wk *= c.expand; if (c.type == Bullet.SoftPoint || c.type == Bullet.Lead) wk *= c.expand;
                        if (d1 > c.yawDepth) wk *= c.yawK; if (v > c.fragV && d1 > c.fragDepth) wk *= c.fragK; if (c.type == Bullet.AP) wk *= 0.8f;
                        // visible hole: the gun's tuned wound size, nudged (not multiplied) by how the bullet opened, tumbled or fragmented
                        float wnd = wound * Mathf.Clamp(Mathf.Sqrt(wk), 1f, 1.45f);
                        // tissue damage: the length of channel actually cut, widened by expansion/yaw/fragments (capped), plus bone; never more than the energy lost
                        bool boneHit = bone && frac > 0.5f;
                        float dmg = Mathf.Min(Ein - Eout, L * frac * bp.tissueResist * Mathf.Min(wk, 2.2f) + (boneHit ? (bp.zone == Zone.Head ? 900f : 450f) : 0f));
                        Vector3 Bend = Vector3.Lerp(A, B, frac); scratch.Clear(); foreach (var org in bp.organs) if (BodyBuilder.SegSphere(A, Bend, org.local, org.radius)) scratch.Add(org);
                        bool wasDead = e.state == HState.Dead;
                        var info = new HitInfo { byPlayer = shooter == null, part = bp, point = h.point, exit = exit, end = bp.transform.TransformPoint(Bend), dir = d, normal = h.normal, energy = Ein, wound = wnd, deposited = dmg, boneHit = boneHit, through = through, cartridge = true, wk = wk, organs = new List<Organ>(scratch) };
                        string t = e.TakeHit(info); res.hit = true; if (res.e == null) { res.e = e; res.part = bp; }
                        if (t != null) Sev(ref res, !wasDead && e.state == HState.Dead ? 3 : scratch.Count > 0 ? 2 : 1, t);
                        e.BloodSpray(h.point, d, false, Ein, Hostile.Region(bp.zone)); if (!e.bloodless) FX.I.Decal(h.point, h.normal, 0.012f + c.diameter * 1.2f, true, bp.transform);
                        if (!through) { res.end = Vector3.Lerp(h.point, exit, frac); res.stopped = true; return res; }
                        e.BloodSpray(exit, d, true, Mathf.Min(Eout, dmg * 2f + 150f), Hostile.Region(bp.zone)); if (!e.bloodless) FX.I.Decal(exit, d, 0.02f + c.diameter * 1.2f * Mathf.Min(wk, 2f), true, bp.transform);
                        v = vOut; res.end = exit; continue;
                    }
                    // glass breaks and lets the round through; a hanging lamp swings (and may go out)
                    var brk = h.collider.GetComponent<Breakable>(); if (brk) { brk.Shatter(d, h.point); v *= 0.95f; continue; }
                    var lamp = h.collider.GetComponentInParent<LampSwing>(); if (lamp) { lamp.Shot(d, E(), h.point); v *= 0.9f; continue; }
                    // loose objects and moving pieces: thin wood walls (crates), full drums stop rounds
                    var lpz = h.collider.GetComponent<LooseProp>(); var sbot = h.collider.GetComponent<SpaceBot>(); if (sbot) sbot.Hit(E(), d, h.point);
                    if (lpz || h.collider.gameObject.layer == Game.LayerProp)
                    {
                        if (lpz) lpz.Shot(h.point, d, E()); else { var rb = h.collider.attachedRigidbody; if (rb) rb.AddForceAtPosition(d * PhysicsTuning.ShotImpulse(E()), h.point, ForceMode.Impulse); }
                        // what it has to get through: the object's fill (mass ÷ volume) along the path, its shell, and the bullet's construction
                        var prb = h.collider.attachedRigidbody; var slc = h.collider.GetComponentInParent<Sliceable>();
                        Penetration.Cost pc = lpz ? Penetration.Loose(h.collider, h.point, d, lpz.mass, lpz.wood, lpz.pmat == PhysicsTuning.Metal, false)
                            : Penetration.Loose(h.collider, h.point, d, prb ? prb.mass : 2f, slc && !slc.flesh, slc == null && h.collider.GetComponentInParent<Sliceable>() == null && prb && prb.mass > 1.5f && h.collider.bounds.size.magnitude < 1.2f && h.collider.GetComponentInParent<Prop>() == null, slc && slc.flesh);
                        FX.I.Chips(h.point, h.normal * 2, 6, pc.chips); if (holes) FX.I.Decal(h.point, h.normal, 0.012f + c.diameter, false, h.collider.transform);
                        float cost = Penetration.For(pc, c); float bud = c.Budget(v); if (bud <= cost) { res.end = h.point; res.env = h.collider; res.normal = h.normal; return res; }
                        v *= Mathf.Sqrt(1 - cost / bud); var pexit = h.point + d * pc.thick; FX.I.Chips(pexit, d * 2, 5, pc.chips); continue;
                    }
                    // the world: doors and thin walls are punched through if the round has the penetration; thick or hard ones stop it, or it skips off at a shallow angle
                    var dr = h.collider.GetComponent<DoorRef>();
                    var wc = Penetration.Static(h.collider, h.point, d);
                    if (holes) { if (wc.hard) FX.I.Sparks(h.point, h.normal, 5); else FX.I.Chips(h.point, h.normal * 2, 6, wc.chips); FX.I.Decal(h.point, h.normal, 0.012f + c.diameter, false, h.collider.transform); }
                    res.env = h.collider; res.normal = h.normal; res.end = h.point;
                    if (dr != null && dr.door.state != "blown") { Game.I.map.DamageDoor(dr.door, E(), d, h.point); if (dr.door.state == "blown") continue; }
                    // through it if the round has the penetration for this material and thickness (at an angle the path is longer: the thickness is measured along the shot)
                    float wcost = Penetration.For(wc, c); float wb = c.Budget(v);
                    if (wc.passable && wb > wcost)
                    {
                        v *= Mathf.Sqrt(1 - wcost / wb); var wexit = h.point + d * wc.thick; if (holes) FX.I.Decal(wexit, d, 0.02f + c.diameter * 2, false, h.collider.transform); FX.I.Chips(wexit, d * 2, 8, wc.chips);
                        continue;
                    }
                    float cos = Mathf.Abs(Vector3.Dot(d, h.normal));
                    if (wc.hard && cos < 0.2f && depth < 1 && c.type != Bullet.JHP)
                    {
                        // ricochet: skips off, slower, slightly deflected
                        var rd = Vector3.Reflect(d, h.normal) + Random.insideUnitSphere * 0.06f; FX.I.Sparks(h.point, h.normal, 10); SFX.Play("ring", h.point, 0.25f, Random.Range(1.4f, 2f));
                        var sub = ShotC(h.point + h.normal * 0.01f, rd.normalized, E() * 0.36f, c, Mathf.Max(1, maxD - travelled - h.distance), shooter, holes, depth + 1, wound); Merge(ref res, sub); if (Game.I.settings.tracers) FX.I.Tracer(h.point, sub.end); return res;
                    }
                    return res;
                }
                // flight: drag slows it (v = v0 e^(-kx)); gravity bends the path down
                travelled += sl; float speed = vNext.magnitude; float nv = Mathf.Min(v, speed) * Mathf.Exp(-k * sl); vel = vNext.normalized * nv; v = nv; p = resume; res.end = p;
                if (stopped || p.y < -5) break;
            }
            return res;
        }
        static void Merge(ref ShotResult res, ShotResult sub) { if (sub.hit) { res.hit = true; if (sub.sev > res.sev) { res.sev = sub.sev; res.txt = sub.txt; } res.e = res.e ?? sub.e; } res.end = sub.end; }
    }
}
