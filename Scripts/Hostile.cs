using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    public enum HState { Idle, Alert, Engage, Push, Surrender, Flee, Down, Dead, Arrested }

    /// <summary>A hostile: layered-anatomy wound model, AI (cover, push, flank, surrender, flee), procedural pose and a physics ragdoll.</summary>
    public partial class Hostile : MonoBehaviour
    {
        public readonly List<BodyPart> parts = new List<BodyPart>();
        public HState state = HState.Idle; public string tag2 = "H-01"; public string cause = "";
        // wounds
        public float bloodMax = 5000f, blood = 5000f, bleed, minorBleed, pain, painTol = 1f; float heartT = -1; public int armor; public float armorIntegrity = 1f;
        public float legL = 1, legR = 1, armL = 1, armR = 1; public readonly List<string> wounds = new List<string>();
        // ai
        public Color skin = new Color(0.85f, 0.65f, 0.52f);
        public float accuracy = 0.5f, reaction = 0.6f, speedMul = 1f, aggression = 0.4f, morale = 1f, dmgMul = 1f;
        float alertT, losT, fireT, strafeT, strafeDir, repathT, idleT, morT, lostT; bool los, wantsPush, gunDropped; int burstLeft;
        Vector3 lastKnown; List<Vector3> path; public float yaw; float aimBlend, aimPitch, phase, moveSpeed; bool moving;
        // rig
        Transform root, pelvis, spine1, spine2, spine3, neck, head, gun; readonly Dictionary<string, Transform> B = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> restRot = new Dictionary<Transform, Quaternion>(); readonly Dictionary<Transform, Vector3> restDir = new Dictionary<Transform, Vector3>();
        Vector3 pelvisRest; float shin = 0.42f; bool ragdoll; Transform pool; float poolSize; float surT;

        public BodyPart Part(Zone z) { foreach (var p in parts) if (p.zone == z) return p; return null; }
        public bool Down => state == HState.Down || state == HState.Dead || state == HState.Arrested;
        public Vector3 Center => pelvis ? pelvis.position + Vector3.up * 0.25f : transform.position + Vector3.up;

        float side = 1;
        public void Init(Transform model, int id)
        {
            root = transform; side = (id & 1) == 0 ? 1 : -1; tag2 = "H-" + id.ToString("00"); model.SetParent(root, false);
            foreach (var n in new[] { "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "hand_l", "hand_r", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r" })
            { var t = BPUtil.FindDeep(model, n); if (t) B[n] = t; }
            // the character files are authored facing -Z; turn the model so its shoulders face the way this hostile is facing, before anything is measured from the bones
            modelT = model; float fe = YawError(B, root); if (Mathf.Abs(fe) > 20) Reface(model, fe, null, null);
            B.TryGetValue("pelvis", out pelvis); B.TryGetValue("spine_01", out spine1); B.TryGetValue("spine_02", out spine2); B.TryGetValue("spine_03", out spine3); B.TryGetValue("neck_01", out neck); B.TryGetValue("head", out head);
            var inv = Quaternion.Inverse(root.rotation);
            foreach (var kv in B) restRot[kv.Value] = inv * kv.Value.rotation;
            void Dir(string a, string b) { if (B.ContainsKey(a) && B.ContainsKey(b)) restDir[B[a]] = (inv * (B[b].position - B[a].position)).normalized; }
            Dir("upperarm_l", "lowerarm_l"); Dir("upperarm_r", "lowerarm_r"); Dir("lowerarm_l", "hand_l"); Dir("lowerarm_r", "hand_r");
            Dir("thigh_l", "calf_l"); Dir("thigh_r", "calf_r"); Dir("calf_l", "foot_l"); Dir("calf_r", "foot_r");
            foreach (var kv in B) restLocal[kv.Value] = kv.Value.localPosition;
            if (pelvis) pelvisRest = pelvis.localPosition; if (B.ContainsKey("calf_l") && B.ContainsKey("foot_l")) shin = Vector3.Distance(B["calf_l"].position, B["foot_l"].position);
            BodyBuilder.Build(this, root);
            foreach (var p in parts) { var rb = p.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var p in parts) if (p.zone == Zone.Chest || p.zone == Zone.Abdomen) p.armored = armor > 0;
            // rifle, carried at the chest
            // the web version's rifle: receiver, handguard, barrel, magazine, grip, stock, top rail and sight
            gun = new GameObject("Gun").transform; gun.SetParent(root, false);
            // the real M4A1 (factory build); the old box rifle if the model isn't in the project
            var real = ViewModels.FactoryRifle();
            if (real) { real.transform.SetParent(gun, false); realGun = true; }
            else
            {
            if (!gunM) { gunM = Mat.Std(0x26282b, 0.4f, 0.7f); gunP = Mat.Std(0x1c1d1f, 0.65f, 0.1f); }
            void GB(float w, float h, float d, float x, float y, float z, float rx, Material m) { var o = Obj.Mesh(gun, Geo.Box(w, h, d), m, new Vector3(x, y, z), Quaternion.Euler(rx * Mathf.Rad2Deg, 0, 0), false, "GunPart"); }
            GB(0.045f, 0.07f, 0.36f, 0, 0, 0.05f, 0, gunM); GB(0.048f, 0.052f, 0.28f, 0, 0, 0.33f, 0, gunP); GB(0.02f, 0.02f, 0.18f, 0, 0.01f, 0.55f, 0, gunM); GB(0.03f, 0.15f, 0.05f, 0, -0.1f, 0.1f, -0.18f, gunP);
            GB(0.03f, 0.085f, 0.04f, 0, -0.07f, -0.06f, 0.3f, gunP); GB(0.035f, 0.07f, 0.2f, 0, -0.01f, -0.2f, 0, gunP); GB(0.022f, 0.02f, 0.24f, 0, 0.045f, 0.12f, 0, gunM); GB(0.03f, 0.03f, 0.06f, 0, 0.07f, 0.05f, 0, gunM);
            }
            foreach (var sd in new[] { "l", "r" }) if (B.ContainsKey("upperarm_" + sd) && B.ContainsKey("lowerarm_" + sd) && B.ContainsKey("hand_" + sd)) { armU[sd] = Vector3.Distance(B["upperarm_" + sd].position, B["lowerarm_" + sd].position); armLo[sd] = Vector3.Distance(B["lowerarm_" + sd].position, B["hand_" + sd].position); }
            InitFX(model); phase = Random.value * 6f; idleT = Random.Range(1f, 4f); morale = Random.Range(0.8f, 1.2f) * Game.I.settings.nerve;
        }

        // ---------------- wounds ----------------
        public string TakeHit(HitInfo h)
        {
            var p = h.part; hitAt = Time.time; lastHitPart = p; lastHitDir = h.dir; lastHitEnergy = h.energy; if (h.byPlayer) hitByPlayer = true; Game.I.stats.hits++; if (!Down) { Hear(Game.I.player.transform.position); morale -= 0.05f; }
            if (ragdoll) { var rb = p.GetComponent<Rigidbody>(); if (settle) settle.Wake(); if (rb && !rb.isKinematic) rb.AddForceAtPosition(h.dir * PhysicsTuning.ShotImpulse(h.energy), h.point, ForceMode.Impulse); }
            if (state == HState.Dead) return null;
            if (h.armorStop) { pain += h.energy / 60f; return tag2 + ": round stopped by armour"; }
            { float r = h.cartridge ? (h.wound > 0 ? h.wound : 0.018f) * 1.2f : (h.wound > 0 ? h.wound : 0.018f) * 1.4f * (0.75f + 0.25f * Mathf.Min(2.2f, h.energy / 700f)); float rx = h.cartridge ? r * 1.4f : r * 1.6f; AddWound(p, h.point - h.dir * 0.25f, h.end + h.dir * (h.through ? 0.25f : 0.02f), r, h.through ? rx : r * 1.1f); }
            // real cartridges: the first organ on the track counts fully, further ones half (one channel, not three separate wounds); tumbling/fragmenting rounds up to +30%
            float orgK = h.cartridge ? Mathf.Clamp(0.75f + 0.15f * h.wk, 1f, 1.3f) : 1f; int orgN = 0;
            float dep = h.deposited; pain += dep / 32f * (p.IsLimb ? 0.6f : 1f) / painTol; if (!bloodless) minorBleed += 1.2f + dep / 500f;
            AddBleed(p.transform, h.point, -h.dir, 0.5f + dep / 700f, false); if (h.through) AddBleed(p.transform, h.end, h.dir, 0.7f + dep / 500f, false);
            string txt = p.label; flinch = 0.25f;
            if (h.boneHit && !p.fractured) { p.fractured = true; txt += ", " + p.boneLabel.ToLower() + " fractured"; pain += 25; wounds.Add(p.boneLabel + " fractured"); }
            foreach (var o in h.organs)
            {
                if (o.hit) continue; o.hit = true; if (!bloodless) bleed += o.bleed * 0.1f; if (o.bleed > 300) AddBleed(p.transform, h.point, -h.dir, 3, true); pain += o.pain * (h.cartridge ? orgK * (orgN++ == 0 ? 1f : 0.5f) : 1f); txt += ", " + o.label.ToLower(); Game.I.stats.organs++; wounds.Add(o.label + (o.bleed > 300 ? " severed" : " perforated"));
                if (o.fatal == Fatal.Instant) { Game.I.stats.heads++; if (h.energy > (h.cartridge ? 3000f : 1800f)) HeadBurst(h.dir); Die("Brain destroyed"); return tag2 + ": " + txt; }
                if (o.fatal == Fatal.Face) { Game.I.stats.heads++; BloodFX(h.end, h.dir + Vector3.up * 0.3f, 60, 4f); Die("Chest face destroyed"); return tag2 + ": " + txt; }
                if (o.fatal == Fatal.Relic) { RelicBurst(h.end, h.dir); Die("Reliquary heart shattered"); return tag2 + ": " + txt; }
                if (o.fatal == Fatal.Heart && !bloodless) heartT = Random.Range(4f, 9f);
                if (o.fatal == Fatal.Spine && !Down) GoDown(o.label + " severed, paralysed");
            }
            if (p.IsLeg) { if (p.IsLeft) legL = Mathf.Max(0, legL - Mathf.Clamp(dep / 900f, 0.25f, 0.6f)); else legR = Mathf.Max(0, legR - Mathf.Clamp(dep / 900f, 0.25f, 0.6f)); if (legL < 0.35f && legR < 0.35f && !Down && !crawling) StartCrawl("both legs shot out"); }
            else if (p.IsLimb) { if (p.IsLeft) armL = Mathf.Max(0, armL - Mathf.Clamp(dep / 900f, 0.25f, 0.6f)); else armR = Mathf.Max(0, armR - Mathf.Clamp(dep / 900f, 0.25f, 0.6f)); if (armL < 0.5f && armR < 0.5f && !gunDropped) { DropGun(); if (Random.value < 0.6f) Surrender(); else state = HState.Flee; } }
            if (!Down && pain > 140f * Game.I.settings.painTol * painTol) GoDown("Shock");
            return tag2 + ": " + txt;
        }

        void HeadBurst(Vector3 dir)
        {
            if (!head) return; BloodFX(head.position, dir + Vector3.up * 0.4f, 120, 6f);
            for (int i = 0; i < 5; i++) if (Physics.Raycast(head.position, (dir + Random.insideUnitSphere * 0.5f).normalized, out var sp, 3f, Game.MaskEnv)) FX.I.Decal(sp.point, sp.normal, Random.Range(0.2f, 0.5f), true, null);
            var hp = Part(Zone.Head); if (hp) { for (int i = 0; i < 4; i++) AddWound(hp, head.position + Random.insideUnitSphere * 0.08f + Vector3.up * 0.09f, Random.Range(0.06f, 0.09f)); AddWound(hp, head.position + dir * 0.12f + Vector3.up * 0.08f, 0.11f); } wounds.Add("Head destroyed");
        }

        public void GoDown(string why)
        {
            if (Down) return; if (ally) { state = HState.Down; cause = why; downAt = Time.time; Game.I.Feed("Your teammate is down: " + why); MakeRagdoll(); DropGun(); return; }
            state = HState.Down; cause = why; downAt = Time.time; Game.I.lastDown = this; Game.I.Feed(tag2 + " down: " + why); Game.I.stats.downs++; MakeRagdoll(); DropGun(); Scored();
        }
        public void Die(string why)
        {
            if (state == HState.Dead) return; if (ally) { bool w0 = Down; state = HState.Dead; cause = why; if (!w0) { downAt = Time.time; MakeRagdoll(); Game.I.Feed("Your teammate was killed: " + why); } DropGun(); return; }
            bool was = Down; state = HState.Dead; cause = why; if (!was) downAt = Time.time; Game.I.lastDown = this; Game.I.stats.kills++; Game.I.Feed(tag2 + " killed: " + why); if (!was) MakeRagdoll(); DropGun(); Scored();
        }
        public void Surrender() { if (ally || Down || state == HState.Surrender) return; if (TrySuicide()) return; state = HState.Surrender; surT = 0; DropGun(); Game.I.Feed(tag2 + " surrenders: press F up close to arrest"); }
        public void Arrest() { state = HState.Arrested; Game.I.stats.arrests++; Game.I.Feed(tag2 + " arrested"); if (!awarded) { awarded = true; Progress.AwardArrest(); } }

        void DropGun()
        {
            if (gunDropped || !gun) return; gunDropped = true; gun.SetParent(null, true);
            // a box fitted to each piece (AddComponent sizes it to the mesh); the gun never collides with the body that dropped it
            foreach (var mf in gun.GetComponentsInChildren<MeshFilter>()) { var c = mf.transform; if (!mf.sharedMesh || c.GetComponent<Collider>()) continue; var col = c.gameObject.AddComponent<BoxCollider>(); col.center = mf.sharedMesh.bounds.center; col.size = mf.sharedMesh.bounds.size; foreach (var p in parts) foreach (var pc in p.GetComponents<Collider>()) Physics.IgnoreCollision(col, pc); }
            var rb = gun.gameObject.AddComponent<Rigidbody>(); rb.mass = 3.4f; PhysicsTuning.Loose(rb, PhysicsTuning.Metal); rb.AddForce(new Vector3(Random.Range(-1f, 1f), 1.5f, Random.Range(-1f, 1f)), ForceMode.Impulse);
        }

        void MakeRagdoll()
        {
            if (ragdoll) return; ragdoll = true; if (anim) anim.Stop();
            var byBone = new Dictionary<Transform, Rigidbody>();
            // real part masses, anatomical joints (elbows and knees hinge one way), projection so nothing stretches; carry the body's current motion over
            var carry = moving ? new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * moveSpeed : Vector3.zero;
            foreach (var p in parts) { var rb = p.GetComponent<Rigidbody>(); if (!rb) continue; rb.isKinematic = false; PhysicsTuning.Part(rb, p); rb.linearVelocity = carry; byBone[p.transform] = rb; }
            foreach (var p in parts)
            {
                Transform t = p.transform.parent; Rigidbody prb = null; while (t != null && !byBone.TryGetValue(t, out prb)) t = t.parent;
                if (prb == null) continue; var j = p.gameObject.AddComponent<CharacterJoint>(); j.connectedBody = prb; PhysicsTuning.Joint(j, p, root);
            }
            settle = gameObject.AddComponent<RagdollSettle>(); settle.bodies = new List<Rigidbody>(byBone.Values).ToArray();
            // parts that overlap when the body goes limp (arms across the chest, thighs) would shove each other apart: let them pass through each other instead
            var cols = new List<Collider>(); foreach (var p in parts) foreach (var c in p.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) cols.Add(c);
            for (int a = 0; a < cols.Count; a++) for (int b = a + 1; b < cols.Count; b++) { var A = cols[a]; var Bc = cols[b]; if (A.attachedRigidbody == Bc.attachedRigidbody) continue; if (Physics.ComputePenetration(A, A.transform.position, A.transform.rotation, Bc, Bc.transform.position, Bc.transform.rotation, out _, out _)) Physics.IgnoreCollision(A, Bc); }
            // the round that dropped them: real momentum, not a launch
            var hit = lastHitPart ? lastHitPart.GetComponent<Rigidbody>() : null; if (hit) hit.AddForce(lastHitDir * PhysicsTuning.ShotImpulse(lastHitEnergy), ForceMode.Impulse);
        }
        BodyPart lastHitPart; Vector3 lastHitDir; float lastHitEnergy = 600; float flinch; RagdollSettle settle; readonly Dictionary<Transform, Vector3> restLocal = new Dictionary<Transform, Vector3>();
        public Vector3 lastHitDirPub { set { lastHitDir = value; } }

        // ---------------- per-frame ----------------
        void Update()
        {
            float dt = Time.deltaTime; if (TickPowers(dt)) return; if (TickFX(dt)) return; if (state == HState.Dead && blood <= 0) return;
            // bleeding: every wound bleeds; small ones clot, vessels don't
            if (bloodless) { bleed = 0; minorBleed = 0; } minorBleed = Mathf.Max(0, minorBleed - dt * 0.15f); float rate = bleed + minorBleed; if (state != HState.Dead) blood -= rate * dt; else blood -= rate * dt * 0.3f;
            TickBleed(dt); TickTrail(bleed + minorBleed);
            if (heartT > 0) { heartT -= dt; if (heartT <= 0) Die("Heart destroyed"); }
            if (state != HState.Dead) { float loss = 1 - blood / bloodMax; if (loss > 0.56f) Die("Bled out"); else if (loss > 0.36f && !Down) GoDown("Blood loss"); }
            if (Down || ragdoll) return;
            if (flinch > 0) flinch -= dt;
            if (Disabled(dt)) { moving = false; return; }
            if (crawling) CrawlAI(dt); else if (hostage) HostageAI(dt); else if (thrallT > 0) ThrallAI(dt); else if (zombie) ZombieAI(dt); else if (ally) AllyThink(dt); else if (Game.I.settings.behavior != 2) Think(dt); else moving = false;
        }

        void Think(float dt)
        {
            var player = Game.I.player; Vector3 p = root.position, eye = p + Vector3.up * 1.6f, pe = player.EyePos; Vector3 to = pe - eye; float dist = new Vector2(to.x, to.z).magnitude;
            losT -= dt; if (losT <= 0) { losT = Random.Range(0.15f, 0.25f); los = player.alive && !player.Ghost && !Physics.Linecast(eye, pe, Game.MaskEnv, QueryTriggerInteraction.Ignore); tgtAlly = Game.I.ally && AllyIsTarget(eye, dist, los, out _); }
            // the teammate bot is the target when it's closer or the player is out of sight
            if (tgtAlly && Game.I.ally && !Game.I.ally.Down) { pe = Game.I.ally.root.position + Vector3.up * 1.6f; to = pe - eye; dist = new Vector2(to.x, to.z).magnitude; los = true; }
            aimPitch = Mathf.Atan2(pe.y - (p.y + 1.45f), dist);
            if (suicideT >= 0) { SuicideTick(dt); return; }
            switch (state)
            {
                case HState.Surrender: moving = false; surT += dt; Turn(Mathf.Atan2(to.x, to.z), dt, 2); return;
                case HState.Arrested: moving = false; return;
                case HState.Flee: aimBlend = Mathf.Max(0, aimBlend - dt * 3); Flee(dt, p); return;
            }
            // morale: aimed at up close, hurt, outnumbered
            morT -= dt; if (morT <= 0 && state == HState.Engage) { morT = 1; if (los && dist < 10 && Vector3.Dot(player.cam.transform.forward, (Center - pe).normalized) > 0.985f) morale -= player.weapons.ads > 0.5f ? 0.12f : 0.07f; if (blood < bloodMax * 0.8f) morale -= 0.03f; if (morale < 0.35f && Random.value < (0.35f - morale) * 1.6f + 0.05f) { Surrender(); return; } }
            if (state == HState.Idle)
            {
                aimBlend = Mathf.Max(0, aimBlend - dt);
                float facing = Vector3.Dot(new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)), new Vector3(to.x, 0, to.z).normalized);
                if (los && dist < 38 && (facing > 0.35f || dist < 4)) Hear(player.transform.position);
                else if (path != null && path.Count > 0) FollowPath(dt, 0.45f);
                else { moving = false; idleT -= dt; if (idleT <= 0) { idleT = Random.Range(2f, 6f); var a = Random.value * 6.28f; var r = Random.Range(2f, 7f); path = Game.I.map.FindPath(p, p + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); } }
                return;
            }
            if (state == HState.Alert) { alertT -= dt; moving = false; var tx = los ? pe : lastKnown; Turn(Mathf.Atan2(tx.x - p.x, tx.z - p.z), dt, 3.5f); aimBlend = Mathf.Min(1, aimBlend + dt * 2.5f); if (alertT <= 0) state = HState.Engage; return; }
            aimBlend = Mathf.Min(1, aimBlend + dt * 3);
            if (CoverAI(dt, p, pe, dist)) return;
            if (los)
            {
                lastKnown = player.transform.position; lostT = 0; path = null; Turn(Mathf.Atan2(to.x, to.z), dt, 5);
                strafeT -= dt; if (strafeT <= 0) { strafeT = Random.Range(0.8f, 2.2f); strafeDir = Random.value < 0.45f ? 0 : (Random.value < 0.5f ? -1 : 1); }
                Vector3 mv = Vector3.zero; if (strafeDir != 0) mv += new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw)) * strafeDir;
                if (aggression > 0.55f && dist > 6) mv += new Vector3(to.x, 0, to.z).normalized * aggression;
                if (mv.sqrMagnitude > 0.0001f) { float sp = 0.9f * speedMul * LegFactor() * FxSpeed; MoveBy(mv.normalized * sp * dt); moving = true; moveSpeed = sp; } else moving = false;
                if (Game.I.settings.behavior == 0 && !gunDropped) FireCtl(dt, dist, pe);
            }
            else
            {
                lostT += dt; burstLeft = 0; if (!wantsPush && lostT > 8) wantsPush = true;
                if (wantsPush && lostT > 0.8f) { repathT -= dt; if (path == null || repathT <= 0) { repathT = 1.4f; path = Game.I.map.FindPath(p, FlankTarget(p)); } FollowPath(dt, 1f); }
                else { moving = false; Turn(Mathf.Atan2(lastKnown.x - p.x, lastKnown.z - p.z), dt, 2.5f); }
            }
        }

        Vector3 FlankTarget(Vector3 p)
        {
            var L = lastKnown; var d = L - p; d.y = 0; float m = d.magnitude; if (!Game.I.settings.flank || m < 4.5f) return L;
            float sd = side, off = Mathf.Min(6, m * 0.6f); var n = d / m; var t = L - n * 2.5f + new Vector3(-n.z, 0, n.x) * sd * off;
            return Game.I.map.Free(t) ? t : L;
        }

        void Flee(float dt, Vector3 p)
        {
            repathT -= dt; if (path == null || path.Count == 0 || repathT <= 0) { repathT = 2.5f; var far = Game.I.map.FarFrom(Game.I.player.transform.position, 14f); path = Game.I.map.FindPath(p, far); }
            FollowPath(dt, 1.3f);
        }

        void FireCtl(float dt, float dist, Vector3 pe)
        {
            if (flinch > 0 || aimBlend < 0.8f || blindT > 0 || burnT > 0) return; fireT -= dt; if (fireT > 0) return;
            if (prof != "rifle") { var mz = gun.TransformPoint(new Vector3(0, 0.004f, realGun ? 0.6f : 0.45f)); FireProfile(mz, pe, dist); FX.I.Glow(mz, new Color(1, 0.8f, 0.5f), 4, 1); Game.I.EnemyShotSound(mz); return; }
            if (burstLeft <= 0) { burstLeft = 2 + Random.Range(0, 4); fireT = Random.Range(0.5f, 1.3f); return; }
            burstLeft--; fireT = Random.Range(0.1f, 0.15f);
            var muzzle = gun.TransformPoint(new Vector3(0, 0.004f, realGun ? 0.6f : 0.45f)); var target = pe + Vector3.down * Random.Range(0.1f, 0.6f);
            float miss = (1.05f - accuracy * Mathf.Min(1, (armL + armR) / 2 + 0.2f)) * 0.06f * (1 + dist / 25f);
            var d = (target - muzzle).normalized + Random.insideUnitSphere * miss; d.Normalize();
            var end = Ballistics.FireC(muzzle, d, 1700f * dmgMul, "5.56", 120f, this); FX.I.Tracer(muzzle, end); FX.I.Glow(muzzle, new Color(1, 0.8f, 0.5f), 4, 1);
            Game.I.EnemyShotSound(muzzle);
        }

        float LegFactor() { float m = Mathf.Min(legL, legR); return m == 0 ? 0.3f : 0.55f + 0.45f * m; }
        void Turn(float target, float dt, float rate) { yaw = Mathf.LerpAngle(yaw * Mathf.Rad2Deg, target * Mathf.Rad2Deg, Mathf.Min(1, dt * rate)) * Mathf.Deg2Rad; }
        void MoveBy(Vector3 d)
        {
            var p = root.position + d; p = Game.I.map.PushOut(p, 0.3f);
            foreach (var o in Game.I.hostiles) { if (o == this || o.Down) continue; var q = o.root.position; var dd = p - q; dd.y = 0; float m = dd.magnitude; if (m < 0.6f && m > 1e-4f) p += dd / m * (0.6f - m) * 0.5f; }
            root.position = p;
        }
        void FollowPath(float dt, float m)
        {
            if (path == null || path.Count == 0) { moving = false; return; }
            var t = path[0]; var p = root.position; var d = t - p; d.y = 0; float L = d.magnitude; if (L < 0.25f) { path.RemoveAt(0); return; }
            float sp = 2.2f * m * speedMul * LegFactor() * FxSpeed * LimpPulse(); MoveBy(d / L * sp * dt); moving = true; moveSpeed = sp; if (!los || state == HState.Flee) Turn(Mathf.Atan2(d.x, d.z), dt, 6);
        }
        public void Hear(Vector3 at) { if (Down || state == HState.Surrender || state == HState.Flee) return; lastKnown = at; if (state == HState.Idle) { state = HState.Alert; alertT = reaction * Random.Range(0.8f, 1.4f); wantsPush = Random.value < aggression; } }

        // ---------------- procedural pose (no animation clips needed) ----------------
        void LateUpdate()
        {
            if (ragdoll || pelvis == null || frozen || Gone || Game.I.timeStop) return; float dt = Time.deltaTime;
            root.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            phase += dt * (moving ? moveSpeed * 3.4f : 0); float sw = moving ? Mathf.Clamp01(moveSpeed / 2.2f) : 0;
            bool kneel = state == HState.Surrender || state == HState.Arrested; float kk = kneel ? Mathf.Clamp01((surT - 0.25f) / 0.6f) : 0; if (state == HState.Arrested) kk = 1;
            crouchK = Mathf.MoveTowards(crouchK, InCoverCrouch && !moving ? 1 : 0, dt * 4); if (!anim) kk = Mathf.Max(kk, crouchK * 0.75f);
            var R = root.rotation;
            if (crawling) { CrawlPose(dt, sw); return; }
            if (anim && !animFaced && ++animFrames >= 2)
            {
                // the animation clips may turn the body; measure once they're playing and turn the model back if needed (rest pose data turns with it)
                animFaced = true; float e = YawError(B, root); if (Mathf.Abs(e) > 30) Reface(modelT, e, restRot, restDir);
            }
            if (anim) AnimPose(kneel, kk); else { 
            pelvis.localPosition = pelvisRest; pelvis.position += Vector3.down * ((shin - 0.06f) * Smooth(kk) + Mathf.Abs(Mathf.Sin(phase)) * 0.03f * sw);
            SetRot(pelvis, Quaternion.Euler(0, Mathf.Sin(phase) * 6 * sw, 0));
            foreach (var s in new[] { spine1, spine2, spine3 }) if (s) SetRot(s, Quaternion.Euler(kneel ? 2 : 3 * sw, -Mathf.Sin(phase) * 4 * sw, 0));
            if (neck) SetRot(neck, Quaternion.identity); }
            if (head) SetRot(head, Quaternion.Euler(-aimPitch * Mathf.Rad2Deg * 0.6f * aimBlend + (state == HState.Arrested ? 20 : 0), 0, 0));
            // legs: the clips walk them, unless a leg or foot is hurt, then a limp is walked by code on top
            bool limp = Limping; if (limp && anim) LimpHips(sw, R);
            if (!anim || limp) for (int i = 0; i < 2; i++)
            {
                string sd = i == 0 ? "l" : "r"; float ph = phase + (i == 0 ? 0 : Mathf.PI); float inj = 1 - (i == 0 ? legL : legR);
                // a hurt leg takes a short, stiff step
                float a = Mathf.Sin(ph) * 0.5f * sw * (1 - 0.55f * inj), kb = Mathf.Max(0, -Mathf.Cos(ph)) * 0.9f * sw * (1 - 0.6f * inj);
                Vector3 thigh = new Vector3(0, -Mathf.Cos(a), Mathf.Sin(a)), calf = new Vector3(0, -Mathf.Cos(a - kb), Mathf.Sin(a - kb));
                if (kk > 0) { thigh = Vector3.Slerp(thigh, new Vector3(0, -1, 0.05f), Smooth(kk)); calf = Vector3.Slerp(calf, new Vector3(0, -0.05f, -1), Smooth(kk)); }
                Limb("thigh_" + sd, thigh, "calf_" + sd, calf, "foot_" + sd);
            }
            // arms: carry or aim the rifle; hands up when surrendering; behind the back when arrested
            float ap = aimPitch * aimBlend;
            if (gun && !gunDropped)
            {
                // stock in the right shoulder pocket when aiming; muzzle down and across the body at low ready
                var sh = B.ContainsKey("upperarm_r") ? B["upperarm_r"].position : (spine3 ? spine3.position : pelvis.position + Vector3.up * 0.4f);
                var aimRot = R * Quaternion.Euler(-ap * Mathf.Rad2Deg + (1 - aimBlend) * 32f, (1 - aimBlend) * -28f, 0);
                gun.rotation = aimRot; gun.position = sh + R * new Vector3(-0.08f, -0.03f - (1 - aimBlend) * 0.1f, 0.03f) + aimRot * new Vector3(0, 0, 0.27f);
                if (suicideT >= 0 && head) SuicideGunPose(R, aimRot, gun.position);
            }
            for (int i = 0; i < 2; i++)
            {
                string sd = i == 0 ? "l" : "r"; float sx = i == 0 ? -1 : 1; Vector3 up, lo;
                if (ZombieArms(i, sx, out up, out lo)) { }
                else if (state == HState.Surrender) { float k = Smooth(Mathf.Clamp01(surT / 0.35f)); up = Vector3.Slerp(new Vector3(0.12f * sx, -1, 0.05f), new Vector3(0.2f * sx, 1, 0.05f), k); lo = Vector3.Slerp(new Vector3(0.05f * sx, -1, 0.1f), new Vector3(0.12f * sx, 1, 0.02f), k); }
                else if (state == HState.Arrested) { up = new Vector3(0.12f * sx, -0.8f, -0.55f); lo = new Vector3(-0.45f * sx, -0.25f, -0.6f); }
                else if (gunDropped && anim) continue; // the walk / idle clip swings the arms
                else if (gunDropped) { float s2 = Mathf.Sin(phase + (i == 0 ? Mathf.PI : 0)) * 0.4f * sw; up = new Vector3(0.12f * sx, -Mathf.Cos(s2), Mathf.Sin(s2)); lo = new Vector3(0.05f * sx, -1, 0.15f + s2 * 0.3f); }
                else if (gun && ArmIK(sd, gun.TransformPoint(i == 0 ? GripL : GripR), R * (i == 0 ? new Vector3(-0.5f, -1f, -0.1f) : new Vector3(0.8f, -1f, -0.3f)))) continue;
                else
                {
                    var carryU = i == 0 ? new Vector3(-0.05f, -0.75f, 0.6f) : new Vector3(0.2f, -0.85f, 0.35f); var carryL = i == 0 ? new Vector3(0.35f, -0.2f, 0.9f) : new Vector3(-0.35f, 0.1f, 0.9f);
                    var aimU = i == 0 ? new Vector3(-0.05f, -0.45f, 0.9f) : new Vector3(0.25f, -0.55f, 0.75f); var aimL = i == 0 ? new Vector3(0.3f, 0.05f, 0.95f) : new Vector3(-0.45f, 0.15f, 0.88f);
                    var q = Quaternion.Euler(-ap * Mathf.Rad2Deg, 0, 0); up = q * Vector3.Lerp(carryU, aimU, aimBlend); lo = q * Vector3.Lerp(carryL, aimL, aimBlend);
                }
                Limb("upperarm_" + sd, up, "lowerarm_" + sd, lo, "hand_" + sd);
            }
        }
        /// <summary>Animation clips (Universal Animation Library) for the body and legs: idle, walk, jog, sprint, kneeling when surrendered or held hostage.
        /// The arms, gun and head are still placed by code on top.</summary>
        public CharAnim anim;
        void AnimPose(bool kneel, float kk)
        {
            anim.paused = frozen;
            if (kneel) { anim.Play(anim.Has("Fixing_Kneeling") ? "Fixing_Kneeling" : "Crouch_Idle_Loop", 0.5f, 0.5f); return; }
            if (crouchK > 0.5f && !moving && anim.Has("Crouch_Idle_Loop")) { anim.Play("Crouch_Idle_Loop", 1, 0.3f); return; }
            if (!moving || moveSpeed < 0.15f) { anim.Play("Idle_Loop", 1, 0.3f); return; }
            // clip playback speed matched to ground speed so the feet don't slide (walk ~1.3 m/s, jog ~3 m/s, sprint ~5.5 m/s at speed 1)
            if (moveSpeed < 1.7f || (zombie && !runner)) anim.Play("Walk_Loop", Mathf.Clamp(moveSpeed / 1.3f, 0.4f, 1.6f), 0.25f);
            else if (moveSpeed < 4f) anim.Play("Jog_Fwd_Loop", Mathf.Clamp(moveSpeed / 3f, 0.6f, 1.5f), 0.25f);
            else anim.Play("Sprint_Loop", Mathf.Clamp(moveSpeed / 5.5f, 0.7f, 1.4f), 0.25f);
        }
        // ---------------- visible bleeding, ported from the web's leaks ----------------
        // Each wound grows a streak of blood running down the body while the person is upright; blood drips off the end of the streak (or straight off the wound when lying down).
        // Wherever the drips land and the body is still, a pool forms and spreads; nearby drips feed the same pool. Cut arteries also spurt with the heartbeat.
        class Leak { public Transform t, streak; public Vector3 lp, ln, dl; public float w, len, max, wid, acc; public bool artery; public BloodPool pool; }
        class BloodPool { public Transform m; public float area, r, sy; }
        readonly List<Leak> leaks = new List<Leak>(); readonly List<BloodPool> pools = new List<BloodPool>(); float heartPh;
        static Material streakMat; static Mesh streakMesh;

        public void AddBleed(Transform t, Vector3 world, Vector3 nWorld, float k, bool artery)
        {
            if (bloodless || !t) return; if (leaks.Count >= 14) leaks.RemoveAt(0);
            var n = nWorld.normalized; var L = new Leak { t = t, lp = t.InverseTransformPoint(world), ln = t.InverseTransformDirection(n).normalized, w = Mathf.Clamp(k, 0.2f, 4), artery = artery, wid = Random.Range(0.012f, 0.022f) };
            // downhill along the skin: gravity flattened onto the surface at the wound
            var down = Vector3.down - n * Vector3.Dot(Vector3.down, n); if (down.sqrMagnitude > 0.05f) L.dl = t.InverseTransformDirection(down.normalized).normalized;
            var bp = t.GetComponent<BodyPart>(); bool torso = bp && (bp.zone == Zone.Chest || bp.zone == Zone.Abdomen || bp.zone == Zone.Pelvis); L.max = torso ? Random.Range(0.12f, 0.3f) : Random.Range(0.06f, 0.2f);
            leaks.Add(L);
        }
        static void StreakAssets()
        {
            if (streakMat) return;
            // a thin run of blood: soft sides that narrow toward the bottom, ending in a drop
            int W = 32, H = 128; var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp }; var px = new Color32[W * H];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                float v = 1 - (y + 0.5f) / H, u = (x + 0.5f) / W - 0.5f; float half = Mathf.Lerp(0.42f, 0.24f, v) + Mathf.Sin(v * 23f) * 0.02f; float a = Mathf.Clamp01((half - Mathf.Abs(u)) * W / 2.5f);
                float bulb = Mathf.Clamp01((0.45f - Mathf.Sqrt(u * u + (v - 0.93f) * (v - 0.93f) * 16)) * 8); a = Mathf.Max(a * (v < 0.96f ? 1 : 0), bulb); px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px); t.Apply(true);
            streakMat = BPUtil.LitTransparent(new Color(0.29f, 0.02f, 0.02f, 0.92f), 0.85f); streakMat.SetTexture("_BaseMap", t);
            // a quad hanging down from its top edge (both sides, so it shows whichever way the body faces)
            streakMesh = new Mesh(); streakMesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, -1, 0), new Vector3(0.5f, -1, 0) };
            streakMesh.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) }; streakMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            streakMesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 0, 2, 1, 2, 3, 1 }; streakMesh.RecalculateBounds();
        }
        void TickBleed(float dt)
        {
            if (bloodless) return; float rate = bleed + minorBleed; bool alive = state != HState.Dead, lying = ragdoll || Down;
            var prb = pelvis ? pelvis.GetComponent<Rigidbody>() : null; bool mov = ragdoll ? (prb && !prb.isKinematic && prb.linearVelocity.sqrMagnitude > 0.09f) : moving;
            if (leaks.Count == 0 && rate > 1f) { var c = Part(Zone.Chest) ?? (parts.Count > 0 ? parts[0] : null); if (c) AddBleed(c.transform, Center + root.forward * 0.1f, root.forward, 1, false); }
            if (leaks.Count == 0) return;
            float bpm = alive ? Mathf.Lerp(80, 150, Mathf.Clamp01(1 - blood / Mathf.Max(1, bloodMax)) * 2) : 0; float ph0 = heartPh; heartPh += dt * bpm / 60f; bool beat = alive && Mathf.Floor(heartPh) != Mathf.Floor(ph0);
            float sw = 0; foreach (var L in leaks) sw += L.w;
            for (int i = leaks.Count - 1; i >= 0; i--)
            {
                var L = leaks[i]; if (!L.t || !L.t.gameObject.activeInHierarchy) { leaks.RemoveAt(i); continue; }
                float share = sw > 0 ? L.w / sw : 0; float q = alive ? Mathf.Max(0.002f, rate / 1000f * share) : rate * 0.3f / 1000f * share; if (q < 0.0003f) continue;
                float sc = Mathf.Max(1e-5f, L.t.lossyScale.x); bool hasDl = L.dl.sqrMagnitude > 0.5f;
                if (L.artery && beat) BloodFX(L.t.TransformPoint(L.lp + L.ln * (0.01f / sc)), (L.t.TransformDirection(L.ln).normalized + Vector3.up * 0.25f).normalized, 16, 2.6f + Random.value * 1.4f);
                // grow the streak while upright; once it reaches its end, blood drips off the tip
                if (hasDl && !lying && L.len < L.max)
                {
                    if (!L.streak) { StreakAssets(); var g = new GameObject("BloodStreak"); g.AddComponent<MeshFilter>().sharedMesh = streakMesh; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = streakMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; L.streak = g.transform; L.streak.SetParent(L.t, false); L.streak.localPosition = L.lp + L.ln * (0.004f / sc); L.streak.localRotation = Quaternion.LookRotation(L.ln, -L.dl); }
                    L.len = Mathf.Min(L.max, L.len + dt * Mathf.Clamp(0.05f + q * 2.5f, 0.05f, 0.3f)); L.streak.localScale = new Vector3(L.wid / sc, Mathf.Max(0.001f, L.len) / sc, 1);
                }
                bool atEnd = !hasDl || lying || L.len >= L.max - 1e-3f; if (!atEnd) continue;
                var P = L.t.TransformPoint((hasDl && !lying ? L.lp + L.dl * (L.len / sc) : L.lp) + L.ln * (0.01f / sc));
                L.acc += dt * Mathf.Min(25, 2 + q * 250); while (L.acc >= 1) { L.acc -= 1; FX.I.DripDrop(P); }
                if (mov) { L.pool = null; continue; }
                var pl = L.pool; if (pl != null && (!pl.m || XZ(pl.m.position, P) > 0.22f + pl.r * 0.5f)) pl = null;
                if (pl == null) foreach (var Q in pools) if (Q.m && XZ(Q.m.position, P) < Q.r + 0.18f) { pl = Q; break; }
                if (pl == null && pools.Count < 14) { pl = new BloodPool { m = FX.I.Pool(P), r = 0.02f, sy = Random.Range(0.75f, 1f) }; pools.Add(pl); }
                L.pool = pl; if (pl != null && pl.m) { pl.area += q * dt * 0.55f; pl.r = Mathf.Min(0.75f, Mathf.Sqrt(pl.area / Mathf.PI) + 0.02f); pl.m.localScale = new Vector3(pl.r * 2 / 0.72f, pl.r * 2 * pl.sy / 0.72f, 1); }
            }
        }
        static float XZ(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        Transform modelT; bool animFaced; int animFrames;
        /// <summary>How far (degrees) the body's facing, measured from the shoulders, is from root's forward.</summary>
        public static float YawError(Dictionary<string, Transform> B, Transform root)
        {
            if (!B.TryGetValue("upperarm_l", out var l) || !B.TryGetValue("upperarm_r", out var r)) return 0; var right = r.position - l.position; right.y = 0; if (right.sqrMagnitude < 1e-6f) return 0;
            var f = Vector3.Cross(right.normalized, Vector3.up); return Vector3.SignedAngle(root.forward, f, Vector3.up);
        }
        /// <summary>Turn the model about the vertical by -err degrees; rest rotations and directions (character space) turn with it.</summary>
        public static void Reface(Transform model, float err, Dictionary<Transform, Quaternion> restRot, Dictionary<Transform, Vector3> restDir)
        {
            var C = Quaternion.Euler(0, -err, 0); model.localRotation = C * model.localRotation;
            if (restRot != null) foreach (var k in new List<Transform>(restRot.Keys)) restRot[k] = C * restRot[k];
            if (restDir != null) foreach (var k in new List<Transform>(restDir.Keys)) restDir[k] = C * restDir[k];
        }
        // ---------------- leg wounds: limping and crawling ----------------
        public bool crawling; Vector3 trailAt; bool trailSet;
        bool Limping => !crawling && moving && Mathf.Min(legL, legR) < 0.9f;
        /// <summary>Which leg is worse (left = 0) and how bad (0..1).</summary>
        int BadLeg => legL < legR ? 0 : 1; float BadInj => 1 - Mathf.Min(legL, legR);
        /// <summary>1 while the bad leg is carrying the weight (its stance half of the stride).</summary>
        float BadStance => Mathf.Max(0, Mathf.Sin(phase + (BadLeg == 0 ? Mathf.PI : 0)));
        /// <summary>A limp's rhythm: a quick, short step onto the bad leg, then a lunge off the good one.</summary>
        float LimpPulse() { if (!Limping) return 1; float k = BadInj; return Mathf.Lerp(1, BadStance > 0 ? 0.45f : 1.3f, Mathf.Clamp01(k * 1.3f)); }
        void LimpHips(float sw, Quaternion R)
        {
            // hips drop and the body leans toward the good side while the bad leg holds the weight
            float s = BadStance * BadInj * sw; pelvis.localPosition = pelvisRest; pelvis.position += Vector3.down * (0.05f * s + 0.015f);
            float side = BadLeg == 0 ? -1 : 1; if (spine1) spine1.rotation = Quaternion.AngleAxis(side * 9 * s, R * Vector3.forward) * spine1.rotation;
        }
        void StartCrawl(string why)
        {
            crawling = true; DropGun(); if (anim) { anim.Stop(); anim = null; } moving = false; Game.I.Feed(tag2 + " can't stand: " + why);
        }
        /// <summary>Crawling: away from the player, slowly; gives up (surrenders where they lie) when the player is close.</summary>
        void CrawlAI(float dt)
        {
            var pl = Game.I.player; var p = root.position; var to = pl.transform.position - p; to.y = 0; float dist = to.magnitude;
            if (state == HState.Arrested) { moving = false; return; }
            if (state == HState.Surrender) { moving = false; surT += dt; return; }
            if (pl.alive && dist < 4.5f) { Surrender(); moving = false; return; }
            var d = dist > 0.01f ? -to / dist : root.forward; Turn(Mathf.Atan2(d.x, d.z), dt, 1.2f);
            MoveBy(new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.32f * dt); moving = true; moveSpeed = 0.32f;
        }
        void CrawlPose(float dt, float sw)
        {
            var R = root.rotation; float mv = moving ? 1 : 0.15f;
            // face down, hips just off the floor, chest propped up a little, head up looking ahead
            pelvis.localPosition = pelvisRest; pelvis.position = root.position + Vector3.up * 0.13f + R * Vector3.forward * 0.05f;
            SetRot(pelvis, Quaternion.Euler(84, Mathf.Sin(phase) * 7 * mv, 0));
            if (spine1) SetRot(spine1, Quaternion.Euler(82, -Mathf.Sin(phase) * 5 * mv, 0)); if (spine2) SetRot(spine2, Quaternion.Euler(76, 0, 0)); if (spine3) SetRot(spine3, Quaternion.Euler(70, 0, 0));
            if (neck) SetRot(neck, Quaternion.Euler(40, 0, 0)); if (head) SetRot(head, Quaternion.Euler(15, 0, 0));
            // arms pull hand over hand
            for (int i = 0; i < 2; i++)
            {
                string sd = i == 0 ? "l" : "r"; float sx = i == 0 ? -1 : 1; float ph = phase * 0.7f + (i == 0 ? 0 : Mathf.PI);
                if (state == HState.Surrender) { ArmIK(sd, root.position + R * new Vector3(sx * 0.35f, 0.05f, 0.75f), R * new Vector3(sx, 0.4f, 0)); continue; }
                var t = root.position + R * new Vector3(sx * 0.22f, 0.03f + Mathf.Max(0, Mathf.Cos(ph)) * 0.08f * mv, 0.6f + Mathf.Sin(ph) * 0.22f * mv);
                ArmIK(sd, t, R * new Vector3(sx, 0.5f, -0.3f));
            }
            // legs drag behind; the better leg pushes a little
            for (int i = 0; i < 2; i++)
            {
                string sd = i == 0 ? "l" : "r"; float sx = i == 0 ? -1 : 1; float good = i == 0 ? legL : legR; float k = Mathf.Sin(phase * 0.7f + i * Mathf.PI) * 0.25f * good * mv;
                Limb("thigh_" + sd, new Vector3(sx * 0.12f, -0.12f, -1).normalized, "calf_" + sd, new Vector3(sx * 0.1f, -0.05f + k, -1).normalized, "foot_" + sd);
            }
        }
        /// <summary>Blood trail: drops along the path while walking, a smeared drag mark while crawling.</summary>
        void TickTrail(float rate)
        {
            if (bloodless || rate < 0.4f || !moving) { trailSet = false; return; }
            var p = root.position; if (!trailSet) { trailAt = p; trailSet = true; return; }
            var step = p - trailAt; step.y = 0; float gap = crawling ? 0.14f : Mathf.Lerp(0.6f, 0.25f, Mathf.Clamp01(rate / 30f)); if (step.magnitude < gap) return; trailAt = p;
            if (crawling) FX.I.FloorMark(p + root.rotation * new Vector3(0, 0.2f, -0.15f), Random.Range(0.16f, 0.26f), step.normalized, 2.2f);
            else { var from = leaks.Count > 0 && leaks[leaks.Count - 1].t ? leaks[leaks.Count - 1].t.TransformPoint(leaks[leaks.Count - 1].lp) : Center; FX.I.FloorMark(from + Random.insideUnitSphere * 0.03f, Random.Range(0.03f, 0.07f) * Mathf.Clamp(rate / 10f, 0.8f, 2f), step.normalized, 1.3f); }
        }
        static float Smooth(float k) => k * k * (3 - 2 * k);
        static Material gunM, gunP; readonly Dictionary<string, float> armU = new Dictionary<string, float>(), armLo = new Dictionary<string, float>();
        // where the wrists go on the rifle (gun space): right behind the pistol grip, left under the handguard
        bool realGun; Vector3 GripR => realGun ? new Vector3(0, -0.08f, -0.09f) : new Vector3(0, -0.055f, -0.12f); Vector3 GripL => realGun ? new Vector3(-0.01f, -0.07f, 0.24f) : new Vector3(0, -0.06f, 0.26f);
        /// <summary>Two-bone arm IK: puts the wrist on a point, with the elbow bending toward the pole direction.</summary>
        bool ArmIK(string sd, Vector3 target, Vector3 pole)
        {
            if (!armU.ContainsKey(sd) || !B.ContainsKey("upperarm_" + sd)) return false; float a = armU[sd], b = armLo[sd]; var S = B["upperarm_" + sd].position; var d = target - S; float L0 = d.magnitude; if (L0 < 1e-4f) return false;
            var dir = d / L0; float L = Mathf.Clamp(L0, Mathf.Abs(a - b) + 0.01f, a + b - 0.002f); float ca = Mathf.Clamp((a * a + L * L - b * b) / (2 * a * L), -1, 1); float along = a * ca, h = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            var pd = pole - dir * Vector3.Dot(pole, dir); if (pd.sqrMagnitude < 1e-6f) pd = Vector3.down; pd.Normalize(); var E = S + dir * along + pd * h; var T = S + dir * L; var inv = Quaternion.Inverse(root.rotation);
            Limb("upperarm_" + sd, inv * (E - S), "lowerarm_" + sd, inv * (T - E), "hand_" + sd); return true;
        }
        void SetRot(Transform t, Quaternion offsetCharSpace) { if (t && restRot.ContainsKey(t)) t.rotation = root.rotation * (offsetCharSpace * restRot[t]); }
        /// <summary>Point a two-bone limb (character space directions) without twisting it: knees bend forward, elbows bend toward the side they should.</summary>
        void Limb(string a, Vector3 dirA, string b, Vector3 dirB, string c)
        {
            if (!B.ContainsKey(a) || !B.ContainsKey(b) || !restDir.ContainsKey(B[a]) || !restDir.ContainsKey(B[b])) return; var ta = B[a]; var tb = B[b];
            Quaternion qa, qb; LimbRot(a.StartsWith("thigh"), restDir[ta], restDir[tb], dirA, dirB, out qa, out qb);
            ta.rotation = root.rotation * (qa * restRot[ta]); tb.rotation = root.rotation * (qb * restRot[tb]);
            if (B.ContainsKey(c)) { var tc = B[c]; tc.rotation = root.rotation * (qb * restRot[tc]); }
        }
        /// <summary>Shared by hostiles and the player's body: rotations taking rest bone directions to new ones, keeping the bend plane (knee / elbow) sensible.</summary>
        public static void LimbRot(bool leg, Vector3 restA, Vector3 restB, Vector3 dirA, Vector3 dirB, out Quaternion qa, out Quaternion qb)
        {
            dirA.Normalize(); dirB.Normalize();
            if (leg)
            {
                Vector3 P(Vector3 d) { var p = Vector3.Cross(d, Vector3.right); return p.sqrMagnitude < 1e-4f ? Vector3.forward : p.normalized; }
                qa = Quaternion.LookRotation(dirA, P(dirA)) * Quaternion.Inverse(Quaternion.LookRotation(restA, P(restA)));
                qb = Quaternion.LookRotation(dirB, P(dirB)) * Quaternion.Inverse(Quaternion.LookRotation(restB, P(restB)));
            }
            else
            {
                // elbow direction: away from the forearm, square to the upper arm; straight arms keep the elbow down and back
                var pole = dirA - dirB; pole -= Vector3.Dot(pole, dirA) * dirA; if (pole.sqrMagnitude < 1e-3f) { pole = new Vector3(0, -0.6f, -1) - Vector3.Dot(new Vector3(0, -0.6f, -1), dirA) * dirA; } pole.Normalize();
                var restPole = new Vector3(0, 0, -1); var rpA = restPole - Vector3.Dot(restPole, restA) * restA; var rpB = restPole - Vector3.Dot(restPole, restB) * restB; if (rpA.sqrMagnitude < 1e-3f) rpA = Vector3.down; if (rpB.sqrMagnitude < 1e-3f) rpB = Vector3.down;
                var poleB = pole - Vector3.Dot(pole, dirB) * dirB; if (poleB.sqrMagnitude < 1e-3f) poleB = pole;
                qa = Quaternion.LookRotation(dirA, pole) * Quaternion.Inverse(Quaternion.LookRotation(restA, rpA.normalized));
                qb = Quaternion.LookRotation(dirB, poleB.normalized) * Quaternion.Inverse(Quaternion.LookRotation(restB, rpB.normalized));
            }
        }
    }
}
