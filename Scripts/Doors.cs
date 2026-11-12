using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    /// <summary>A hinged door: open (G), kick (K), shoot it apart, blow it into planks. Hostiles open doors they walk into.</summary>
    public class Door
    {
        public Transform g, panel; public bool axisX; public float cx, cz; public string state = "closed"; public float t, dur = 1, target, hp = 1200; public Bounds box;
        public Vector3 homePos; public bool Blocks => state == "closed";
        // the breach door: locked until kicked in, its lock or both hinges shot out, blown with a charge, or cut through
        public bool locked, breach; public float lockHp, hingeA, hingeB, kicks; public int cuts; public GameObject charge; public float chargeT = -1, beepT; public Vector3 chargeFrom;
    }
    /// <summary>Lets a raycast hit find its door.</summary>
    public class DoorRef : MonoBehaviour { [System.NonSerialized] public Door door; }

    public partial class MapGen
    {
        public readonly List<Door> doors = new List<Door>(); readonly List<GameObject> debris = new List<GameObject>();
        static Material doorWood, beamM, officeM;
        static void DoorMats() { if (doorWood) return; doorWood = Mat.Std(0x7a5534, 0.7f); beamM = Mat.Std(0x3a3f45, 0.7f, 0.4f); officeM = Mat.Std(0xbdb8ad, 0.92f); }

        Door MakeDoor(float cx, float cz, bool axisX)
        {
            DoorMats(); const float W = 1.3f, Hd = 2.05f;
            var g = Obj.Group(world, axisX ? new Vector3(cx - W / 2, 0, cz) : new Vector3(cx, 0, cz - W / 2), "Door");
            var p = Obj.Mesh(g, Geo.Box(axisX ? W : 0.05f, Hd, axisX ? 0.05f : W), doorWood, new Vector3(axisX ? W / 2 : 0, Hd / 2, axisX ? 0 : W / 2), Quaternion.identity, false, "Panel");
            var bc = p.AddComponent<BoxCollider>(); bc.size = new Vector3(axisX ? W : 0.05f, Hd, axisX ? 0.05f : W); p.layer = Game.LayerEnv;
            Obj.Mesh(g, Geo.Box(axisX ? 0.12f : 0.14f, 0.04f, axisX ? 0.14f : 0.12f), beamM, new Vector3(axisX ? W - 0.12f : 0, 1.0f, axisX ? 0 : W - 0.12f));
            var box = new Bounds(); if (axisX) box.SetMinMax(new Vector3(cx - W / 2, 0, cz - 0.06f), new Vector3(cx + W / 2, Hd, cz + 0.06f)); else box.SetMinMax(new Vector3(cx - 0.06f, 0, cz - W / 2), new Vector3(cx + 0.06f, Hd, cz + W / 2));
            var d = new Door { g = g, panel = p.transform, axisX = axisX, cx = cx, cz = cz, box = box, homePos = g.localPosition }; p.AddComponent<DoorRef>().door = d; doors.Add(d); return d;
        }

        /// <summary>Kick whatever loose object is in front of you.</summary>
        void KickProp(Player P)
        {
            var f = P.transform.forward; LooseProp best = null; float bd = 1.7f; Rigidbody brb = null;
            foreach (var lp in LooseProp.All) { if (!lp) continue; var b = lp.B; var q = b.ClosestPoint(P.transform.position + Vector3.up * 0.6f); var d = q - P.transform.position; d.y = 0; float m = d.magnitude; if (m < bd && Vector3.Dot(d.normalized, f) > 0.4f) { bd = m; best = lp; } }
            if (!best) foreach (var pr in Prop.All) { var rb = pr ? pr.Body : null; if (!rb || rb.isKinematic) continue; var d = rb.position - P.transform.position; d.y = 0; float m = d.magnitude; if (m < bd && Vector3.Dot(d.normalized, f) > 0.4f) { bd = m; brb = rb; } }
            if (!best && !brb) return; P.recoilP += 2f; SFX.Play("thud", P.transform.position + f, 0.7f); Game.I.Noise(P.transform.position, 10);
            // a hard kick is roughly 60 kg·m/s; light things fly, heavy ones shift
            var imp = (f + Vector3.up * 0.25f).normalized * 60f;
            if (best) { var at = best.B.ClosestPoint(P.transform.position + Vector3.up * 0.5f); best.Wake(Vector3.ClampMagnitude(imp, best.mass * 8f), at); if (best.wood && best.mass < 30 && Random.value < 0.25f) best.Splinter(f, 3); }
            else brb.AddForce(Vector3.ClampMagnitude(imp, brb.mass * 8f), ForceMode.Impulse);
        }
        /// <summary>A cut-through door gets a fresh panel on reset.</summary>
        void RebuildPanel(Door d)
        {
            DoorMats(); const float W = 1.3f, Hd = 2.05f; bool axisX = d.axisX;
            var p = Obj.Mesh(d.g, Geo.Box(axisX ? W : 0.05f, Hd, axisX ? 0.05f : W), doorWood, new Vector3(axisX ? W / 2 : 0, Hd / 2, axisX ? 0 : W / 2), Quaternion.identity, false, "Panel");
            var bc = p.AddComponent<BoxCollider>(); bc.size = new Vector3(axisX ? W : 0.05f, Hd, axisX ? 0.05f : W); p.layer = Game.LayerEnv; p.AddComponent<DoorRef>().door = d; d.panel = p.transform;
        }
        /// <summary>A wall from a0 to a1 along x (axisX) or z at c, with doorways at the given openings (web wallRun).</summary>
        void WallRun(bool axisX, float a0, float a1, float c, float[] openings, float H)
        {
            DoorMats(); const float T = 0.12f, ow = 1.4f; var mat = wallOverride ? wallOverride : officeM; var ops = (float[])openings.Clone(); System.Array.Sort(ops); float s0 = a0;
            void Seg(float p0, float p1) { if (p1 - p0 < 0.02f) return; float m = (p0 + p1) / 2, L = p1 - p0; if (axisX) Box(m, H / 2, c, L, H, T, mat); else Box(c, H / 2, m, T, H, L, mat); }
            foreach (var o in ops)
            {
                Seg(s0, o - ow / 2);
                if (axisX) Box(o, 2.1f + (H - 2.1f) / 2, c, ow, H - 2.1f, T, mat, false); else Box(c, 2.1f + (H - 2.1f) / 2, o, T, H - 2.1f, ow, mat, false);
                MakeDoor(axisX ? o : c, axisX ? c : o, axisX); s0 = o + ow / 2;
            }
            Seg(s0, a1);
        }
        /// <summary>Door centres on .25/.75 so the nav grid keeps a walkable cell through the doorway.</summary>
        static float SnapD(float v) => Mathf.Round((v - 0.25f) * 2) / 2 + 0.25f;

        public void OpenDoor(Door d, float fromX, float fromZ, float dur)
        {
            if (d.state != "closed") return;
            if (d.locked) { SFX.Play("click", new Vector3(d.cx, 1, d.cz), 0.6f, 0.7f); return; } d.state = "opening"; d.dur = dur; d.t = 0;
            d.target = d.axisX ? (fromZ < d.cz ? -1.5f : 1.5f) : (fromX < d.cx ? 1.5f : -1.5f);
            SFX.Play(dur < 0.3f ? "thud" : "creak", new Vector3(d.cx, 1, d.cz), dur < 0.3f ? 0.9f : 0.35f);
        }

        void StunBehind(Door d, float fromX, float fromZ, bool hard)
        {
            foreach (var e in Game.I.hostiles)
            {
                if (!e || e.Down) continue; var p = e.transform.position; float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.cx, d.cz)); if (dist > 3) continue;
                bool same = d.axisX ? ((p.z - d.cz) * (fromZ - d.cz) > 0) : ((p.x - d.cx) * (fromX - d.cx) > 0); if (same) continue;
                e.Hear(Game.I.player.transform.position); e.Stagger(dist < 1.3f ? (hard ? 45 : 25) : 0, hard ? 1.6f : 1.0f);
                if (dist < 1.3f && hard && Random.value < 0.5f) e.GoDown("Knocked down by the breached door");
            }
        }

        public void SplinterDoor(Door d, Vector3 dir, float force, Vector3 pt)
        {
            if (d.state == "blown") return; d.state = "blown"; const float W = 1.3f, H = 2.05f, T = 0.05f; const int cols = 4, rows = 4; float cw = W / cols, ch = H / rows;
            var q = d.panel.rotation; var ctr = d.panel.position; d.g.gameObject.SetActive(false);
            for (int a = 0; a < cols; a++) for (int r = 0; r < rows; r++)
                {
                    float w = cw * Random.Range(0.8f, 1f), h = ch * Random.Range(0.75f, 1.05f), u = -W / 2 + cw * (a + 0.5f), v = -H / 2 + ch * (r + 0.5f);
                    var lp = d.axisX ? new Vector3(u, v, 0) : new Vector3(0, v, u); var size = d.axisX ? new Vector3(w, h, T) : new Vector3(T, h, w);
                    var m = Obj.Mesh(world, Geo.Box(size.x, size.y, size.z), doorWood, ctr + q * lp, q, false, "Plank"); m.layer = Game.LayerProp;
                    m.AddComponent<BoxCollider>().size = size; var rb = m.AddComponent<Rigidbody>(); rb.mass = 2.5f; PhysicsTuning.Loose(rb, PhysicsTuning.Wood);
                    float k = 1 / (1 + Vector3.Distance(m.transform.position, pt) * 1.6f), sp = force * (0.35f + 1.4f * k) * Random.Range(0.7f, 1.2f);
                    rb.linearVelocity = new Vector3(dir.x * sp + Random.Range(-1f, 1f), Random.Range(0.3f, 1.8f) * k + 0.2f, dir.z * sp + Random.Range(-1f, 1f)); rb.angularVelocity = Random.insideUnitSphere * 8f;
                    m.AddComponent<Prop>().label = "plank"; debris.Add(m);
                }
            while (debris.Count > 160) { var o = debris[0]; debris.RemoveAt(0); if (o) Destroy(o); }
            FX.I.Chips(pt, dir * force, 45, new Color(0.55f, 0.38f, 0.22f));
            SFX.Play("thud", pt, 0.9f); SFX.Play("splinter", pt, 0.6f);
        }

        public void KickDoor(Door d)
        {
            var P = Game.I.player; P.recoilP += 3f; Game.I.Noise(P.transform.position, 16);
            int kick = Progress.Skill("b_kick");
            if (d.locked)
            {
                // a reinforced door takes one to three kicks; each failed kick is loud
                d.kicks += Random.Range(0.34f, 0.5f) + 0.17f * kick; P.weapons.Shake(0.6f);
                if (d.kicks < 1) { SFX.Play("thud", new Vector3(d.cx, 1, d.cz), 1f, 0.8f); FX.I.Chips(new Vector3(d.cx, 1.0f, d.cz), P.transform.forward, 8, new Color(0.55f, 0.38f, 0.22f)); Game.I.Noise(new Vector3(d.cx, 1, d.cz), 22); Game.I.Feed(d.kicks > 0.6f ? "The frame is splitting" : "The door holds"); return; }
                d.locked = false; var f0 = P.transform.forward; StunBehind(d, P.transform.position.x, P.transform.position.z, true);
                if (Random.value < 0.4f) { SplinterDoor(d, new Vector3(f0.x, 0, f0.z), 7, new Vector3(d.cx, 1, d.cz)); Game.I.Feed("Door kicked to pieces"); }
                else { OpenDoor(d, P.transform.position.x, P.transform.position.z, 0.12f); Game.I.Feed("Door kicked in"); }
                return;
            }
            if (Random.value < 0.75f + 0.0834f * kick)
            {
                var f = P.transform.forward; StunBehind(d, P.transform.position.x, P.transform.position.z, kick >= 2);
                if (Random.value < 0.45f + 0.15f * kick) { SplinterDoor(d, new Vector3(f.x, 0, f.z), 6, new Vector3(d.cx, 1, d.cz)); Game.I.Feed("Door smashed apart"); }
                else { OpenDoor(d, P.transform.position.x, P.transform.position.z, 0.14f); Game.I.Feed("Door breached"); }
            }
            else { SFX.Play("thud", new Vector3(d.cx, 1, d.cz), 0.8f); Game.I.Feed("Kick failed, the door held"); }
        }
        public void DamageDoor(Door d, float energy, Vector3 dir, Vector3 pt)
        {
            if (d.state == "blown") return;
            // an open door on its hinges swings from the hit
            var rb = d.g.GetComponent<Rigidbody>(); if (rb && !rb.isKinematic) rb.AddForceAtPosition(dir * PhysicsTuning.ShotImpulse(energy), pt, ForceMode.Impulse);
            if (d.locked)
            {
                // weak points: the lock (by the handle) and the two hinges; shotgun pellets hit them hard, single bullets less
                const float W = 1.3f; var axis = d.g.rotation * (d.axisX ? Vector3.right : Vector3.forward); float u = Vector3.Dot(pt - d.g.position, axis), v = pt.y - d.g.position.y; float dmg = energy <= 450 ? energy * 1.3f : energy * 0.5f;
                if (Mathf.Abs(u - (W - 0.12f)) < 0.16f && Mathf.Abs(v - 1.0f) < 0.18f) { d.lockHp -= dmg; FX.I.Sparks(pt, -dir, 8); SFX.Play("metal", pt, 0.5f); if (d.lockHp <= 0) { d.locked = false; Game.I.Feed("Lock shot out"); var P = Game.I.player.transform.position; StunBehind(d, P.x, P.z, false); OpenDoor(d, P.x, P.z, 0.35f); } return; }
                if (u < 0.17f && (Mathf.Abs(v - 0.4f) < 0.2f || Mathf.Abs(v - 1.65f) < 0.2f))
                {
                    if (v < 1) d.hingeA -= dmg; else d.hingeB -= dmg; FX.I.Sparks(pt, -dir, 8); SFX.Play("metal", pt, 0.5f);
                    if (d.hingeA <= 0 && d.hingeB <= 0) { d.locked = false; Game.I.Feed("Hinges shot out: the door falls"); DropDoor(d, dir); }
                    return;
                }
            }
            d.hp -= energy * (energy <= 450 ? 1 : 0.12f); if (d.hp <= 0) { d.locked = false; BlowDoor(d, dir, pt); }
        }
        /// <summary>Both hinges gone: the whole door falls away from the shooter.</summary>
        void DropDoor(Door d, Vector3 dir)
        {
            d.state = "blown"; var rb = d.g.gameObject.AddComponent<Rigidbody>(); rb.mass = 40; PhysicsTuning.Loose(rb, PhysicsTuning.Wood); d.g.gameObject.layer = Game.LayerProp; foreach (Transform c in d.g) c.gameObject.layer = Game.LayerProp;
            rb.linearVelocity = new Vector3(dir.x, 0, dir.z).normalized * 1.5f; rb.angularVelocity = Random.insideUnitSphere * 0.5f; SFX.Play("thud", d.panel.position, 1f, 0.7f);
            var P = Game.I.player.transform.position; StunBehind(d, P.x, P.z, false);
        }
        /// <summary>Plant the breaching charge on the door; it goes off after 3 s.</summary>
        void PlantCharge(Door d)
        {
            breachCharges--; var P = Game.I.player.transform.position; var toP = new Vector3(P.x - d.cx, 0, P.z - d.cz).normalized; var at = new Vector3(d.cx, 1.1f, d.cz) + toP * 0.05f;
            var c = Obj.Mesh(world, Geo.Box(0.22f, 0.12f, 0.06f), Mat.Std(0xc8b88a, 0.8f), at, Quaternion.LookRotation(toP), false, "BreachCharge");
            Obj.Mesh(c.transform, Geo.Box(0.03f, 0.03f, 0.02f), Mat.Hdr(0xff2020, 3), new Vector3(0.07f, 0.04f, 0.035f), Quaternion.identity, false, "Led");
            d.charge = c; d.chargeT = 3; d.beepT = 0; d.chargeFrom = P; SFX.Play("click", at, 0.6f, 1.4f); Game.I.Feed("Breaching charge set: 3 seconds, get back");
        }
        void DetonateCharge(Door d)
        {
            var at = d.charge ? d.charge.transform.position : new Vector3(d.cx, 1.1f, d.cz); if (d.charge) Destroy(d.charge); d.charge = null; d.chargeT = -1;
            var through = new Vector3(d.cx - d.chargeFrom.x, 0, d.cz - d.chargeFrom.z).normalized;
            SFX.Play("boom", at, 1f, 0.9f); Game.I.player.weapons.Shake(1.6f); FX.I.Glow(at, new Color(1, 0.7f, 0.3f), 30, 6);
            for (int i = 0; i < 70; i++) FX.I.Emit(at, (through * 0.6f + Random.onUnitSphere).normalized * Random.Range(3f, 12f), new Color(3f, Random.Range(1f, 1.8f), 0.3f), Random.Range(0.4f, 1.1f), 3, Random.Range(0.3f, 1.2f));
            d.locked = false; SplinterDoor(d, through, 11, at); StunBehind(d, d.chargeFrom.x, d.chargeFrom.z, true); Game.I.Noise(at, 40); Game.I.Feed("Door blown");
            WorldPhysics.Blast(at + through * 0.3f, 6, 70);
            var P = Game.I.player; float dist = Vector3.Distance(P.transform.position + Vector3.up, at); if (P.alive && dist < 2.2f) P.Hurt((2.2f - dist) * 50, "Too close to the breaching charge");
        }
        /// <summary>A blade through the door: a cut that takes off a big enough piece drops it through; smaller cuts score it, and three of them break it.</summary>
        public void CutDoor(Door d, Plane pl, Vector3 push)
        {
            const float W = 1.3f, H = 2.05f; var axis = d.g.rotation * (d.axisX ? Vector3.right : Vector3.forward); var o = d.g.position;
            var poly = new List<Vector2> { new Vector2(0, 0), new Vector2(W, 0), new Vector2(W, H), new Vector2(0, H) };
            float SideArea(bool pos)
            {
                var outp = new List<Vector2>();
                for (int i = 0; i < 4; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % 4]; float da = pl.GetDistanceToPoint(o + axis * a.x + Vector3.up * a.y), db = pl.GetDistanceToPoint(o + axis * b.x + Vector3.up * b.y); if (!pos) { da = -da; db = -db; }
                    if (da >= 0) outp.Add(a); if ((da >= 0) != (db >= 0)) outp.Add(Vector2.Lerp(a, b, da / (da - db)));
                }
                float A = 0; for (int i = 0; i < outp.Count; i++) { var p = outp[i]; var q = outp[(i + 1) % outp.Count]; A += p.x * q.y - q.x * p.y; } return Mathf.Abs(A) / 2;
            }
            float frac = Mathf.Min(SideArea(true), SideArea(false)) / (W * H);
            SFX.Play("splinter", d.panel.position, 0.5f, 1.3f); FX.I.Chips(pl.ClosestPointOnPlane(d.panel.position), push.normalized, 14, new Color(0.55f, 0.38f, 0.22f));
            if (frac >= 0.28f)
            {
                // the panel is cut in two: the hinge side stays hanging, the other piece falls
                var mr = d.panel.GetComponent<MeshRenderer>(); d.locked = false; d.state = "blown"; var hingeTop = o + Vector3.up * 1.0f;
                foreach (Transform c in d.g) if (c != d.panel) c.gameObject.SetActive(false);
                var s = Sliceable.FromEnv(mr); d.panel = null;
                if (s && s.Slice(pl, push)) { var pieces = new[] { Sliceable.lastA, Sliceable.lastB }; { Sliceable keep = null; float best = 1e9f; foreach (var p in pieces) { if (!p) continue; float dd = Vector3.Distance(p.transform.TransformPoint(p.GetComponent<Rigidbody>().centerOfMass), hingeTop); if (dd < best) { best = dd; keep = p; } } if (keep) keep.GetComponent<Rigidbody>().isKinematic = true; } }
                var P = Game.I.player.transform.position; StunBehind(d, P.x, P.z, false); Game.I.Feed("Door cut through"); return;
            }
            d.cuts++; for (int i = 0; i < 6; i++) { var q = pl.ClosestPointOnPlane(o + axis * Random.Range(0.1f, W - 0.1f) + Vector3.up * Random.Range(0.1f, H - 0.1f)); FX.I.Decal(q, -push.normalized, 0.03f, false, d.panel); }
            if (d.cuts >= 3) { d.locked = false; var P = Game.I.player.transform.position; SplinterDoor(d, (new Vector3(d.cx, 0, d.cz) - new Vector3(P.x, 0, P.z)).normalized, 4, d.panel.position); Game.I.Feed("Door hacked apart"); }
            else Game.I.Feed("Door scored: cut a bigger piece, or keep cutting");
        }
        /// <summary>Once open, a door hangs on a real hinge: bodies, blasts and shots swing it.</summary>
        void MakeSwing(Door d)
        {
            if (d.g.GetComponent<Rigidbody>()) return; var rb = d.g.gameObject.AddComponent<Rigidbody>(); rb.mass = 28; PhysicsTuning.Loose(rb, PhysicsTuning.Wood); rb.angularDamping = 1.5f;
            var hj = d.g.gameObject.AddComponent<HingeJoint>(); hj.anchor = Vector3.zero; hj.axis = Vector3.up; hj.useLimits = true;
            float open = d.target * Mathf.Rad2Deg; var lim = new JointLimits(); if (open > 0) { lim.min = -open; lim.max = 110 - open; } else { lim.min = -110 - open; lim.max = -open; } hj.limits = lim;
        }
        public void BlowDoor(Door d, Vector3 dir, Vector3 pt)
        {
            var P = Game.I.player.transform.position; SplinterDoor(d, dir, 5, pt); StunBehind(d, P.x, P.z, true); Game.I.Noise(P, 25); Game.I.Feed("Door blown open");
        }
        public Door NearDoor(Transform who)
        {
            Door best = null; float bd = 1.8f; var f = who.forward;
            foreach (var d in doors)
            {
                if (d.state != "closed") continue; float dx = d.cx - who.position.x, dz = d.cz - who.position.z, dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist < bd && (dx * f.x + dz * f.z) / Mathf.Max(dist, 1e-3f) > 0.2f) { bd = dist; best = d; }
            }
            return best;
        }
        public void DoorAction(bool kick)
        {
            var P = Game.I.player; if (!P.alive) return; var d = NearDoor(P.transform);
            if (d == null) { if (kick) KickProp(P); return; }
            DoorActionOn(d, kick);
        }
        /// <summary>Open, plant a charge on, or kick a specific door (touch: tapping the door itself).</summary>
        public void DoorActionOn(Door d, bool kick)
        {
            var P = Game.I.player; if (!P.alive || d == null) return;
            if (Vector2.Distance(new Vector2(d.cx, d.cz), new Vector2(P.transform.position.x, P.transform.position.z)) > 2.6f) { Game.I.Feed("Get closer to the door"); return; }
            if (kick) KickDoor(d);
            else if (d.locked && d.chargeT < 0 && breachCharges > 0) PlantCharge(d);
            else if (d.locked) Game.I.Feed(d.chargeT >= 0 ? "Charge set: get back!" : "Locked. Kick it (K), shoot the lock or both hinges, or cut it");
            else OpenDoor(d, P.transform.position.x, P.transform.position.z, 0.9f);
        }
        void UpdateDoors(float dt)
        {
            if (dt <= 0) return;
            foreach (var d in doors)
            {
                if (d.chargeT >= 0) { d.chargeT -= dt; d.beepT -= dt; if (d.beepT <= 0 && d.charge) { d.beepT = Mathf.Lerp(0.12f, 0.5f, d.chargeT / 3f); SFX.Play("click", d.charge.transform.position, 0.5f, 2f); } if (d.chargeT <= 0) DetonateCharge(d); continue; }
                if (d.state == "opening") { d.t += dt; float k = Mathf.Min(1, d.t / d.dur); d.g.localRotation = Quaternion.Euler(0, d.target * (1 - Mathf.Pow(1 - k, 3)) * Mathf.Rad2Deg, 0); if (k >= 1) { d.state = "open"; MakeSwing(d); } }
                else if (d.state == "closed" && !d.locked && Game.I != null)
                    foreach (var e in Game.I.hostiles) { if (!e || e.Down) continue; var q = e.transform.position; if (Vector2.Distance(new Vector2(q.x, q.z), new Vector2(d.cx, d.cz)) < 0.95f) { OpenDoor(d, q.x, q.z, 1.0f); break; } }
            }
        }
        /// <summary>Put every door back (reset hostiles keeps the map).</summary>
        public void ResetDoors()
        {
            foreach (var o in debris) if (o) Destroy(o); debris.Clear();
            foreach (var d in doors)
            {
                var hj = d.g.GetComponent<HingeJoint>(); if (hj) DestroyImmediate(hj); var rb = d.g.GetComponent<Rigidbody>(); if (rb) DestroyImmediate(rb);
                d.g.gameObject.SetActive(true); foreach (Transform c in d.g) c.gameObject.SetActive(true); d.g.localPosition = d.homePos; d.g.localRotation = Quaternion.identity; d.state = "closed"; d.hp = 1200; d.t = 0;
                if (!d.panel) RebuildPanel(d); d.g.gameObject.layer = 0; foreach (Transform c in d.g) c.gameObject.layer = c == d.panel ? Game.LayerEnv : 0;
                if (d.charge) Destroy(d.charge); d.charge = null; d.chargeT = -1;
                if (d.breach) { d.locked = true; d.hp = 6000; d.lockHp = 2400; d.hingeA = d.hingeB = 2400; d.kicks = 0; d.cuts = 0; }
            }
            breachCharges = 1;
        }
    }
}
