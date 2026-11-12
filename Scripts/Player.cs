using UnityEngine;
using UnityEngine.InputSystem;

namespace BreachPoint
{
    /// <summary>First-person player: walk/sprint/crouch/lean/jump, mouse look with recoil, flashlight, arrests, health.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        public Camera cam; public WeaponController weapons; public Light flashlight; CharacterController cc;
        public float hurtAt = -99, hp = 100, yaw, pitch, recoilP, lean, speed, bob, hurtFlash; public Vector2 moveIn; public bool alive = true, crouched, sprinting, thirdPerson; bool padSprint; Vector3 spawnAt; float vy, crouchK, eyeH = 1.62f;
        public Vector3 EyePos => cam ? cam.transform.position : transform.position + Vector3.up * 1.6f;
        public Vector3 Velocity => cc ? cc.velocity : Vector3.zero;
        public float MaxHp => 100 + 20 * Progress.Skill("b_tough");
        public void Push(Vector3 d) { if (cc && cc.enabled) cc.Move(d); }
        // Wraith Form
        public float wraith, wraithCd; public bool Ghost => wraith > 0; bool ghostDoors;
        PlayerBody body; float slowHeld = -1; bool slowFired;
        /// <summary>Turn the view straight at a point (Dead Eye).</summary>
        public void LookAt(Vector3 p) { var d = p - cam.transform.position; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; recoilP = 0; transform.rotation = Quaternion.Euler(0, yaw, 0); cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0); }
        void WraithKey()
        {
            int r = Progress.Skill("w_form"); if (r == 0) { Game.I.Feed("Learn Wraith Form on the Progress page first"); return; } if (wraith > 0) return;
            if (wraithCd > 0) { Game.I.Feed("Wraith Form ready in " + Mathf.CeilToInt(wraithCd) + " s"); return; }
            wraith = 1.5f + 1.5f * r; wraithCd = 30 - 5 * r; SFX.Play("eerie", null, 0.6f, 0.6f); SFX.Play("eerie", null, 0.5f, 0.8f); Game.I.Feed("Wraith Form");
        }
        void GhostDoors(bool on)
        {
            if (ghostDoors == on) return; ghostDoors = on;
            foreach (var d in Game.I.map.doors) if (d != null && d.panel) foreach (var c in d.panel.GetComponents<Collider>()) Physics.IgnoreCollision(cc, c, on);
        }
        /// <summary>Move instantly (rifts), facing yawDeg, with an optional upward pop.</summary>
        public void Teleport(Vector3 p, float yawDeg, float upVel) { cc.enabled = false; transform.position = p; cc.enabled = true; yaw = yawDeg; vy = upVel; }

        public void Init(Camera c)
        {
            cc = GetComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.33f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.35f; gameObject.layer = Game.LayerPlayer;
            cam = c; cam.transform.SetParent(transform, false); cam.transform.localPosition = new Vector3(0, eyeH, 0); cam.nearClipPlane = 0.03f;
            flashlight = new GameObject("Flashlight").AddComponent<Light>(); flashlight.type = LightType.Spot; flashlight.spotAngle = 48; flashlight.innerSpotAngle = 20; flashlight.range = 30; flashlight.intensity = 0; flashlight.color = new Color(1f, 0.96f, 0.88f); flashlight.shadows = LightShadows.Soft;
            flashlight.transform.SetParent(cam.transform, false); flashlight.transform.localPosition = new Vector3(0.15f, -0.1f, 0);
            weapons = cam.gameObject.AddComponent<WeaponController>(); weapons.Init(this, cam);
        }

        public void Spawn(Vector3 p, float yawDeg)
        {
            // stand on whatever floor is below the spawn point, a little above it (the floor is a zero-thickness surface: starting exactly on it could fall through)
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var gh, 6f, Game.MaskEnv, QueryTriggerInteraction.Ignore)) p.y = gh.point.y + 0.05f; else p.y += 0.1f;
            spawnAt = p; cc.enabled = false; transform.position = p; cc.enabled = true; Physics.SyncTransforms(); yaw = yawDeg; pitch = 0; hp = MaxHp; alive = true; vy = 0; recoilP = 0; wraith = 0; wraithCd = 0; GhostDoors(false); cam.transform.localPosition = new Vector3(0, eyeH, 0); weapons.Refill();
        }

        public void AddRecoil(float up, float side) { recoilP += up * 0.75f; pitch -= up * 0.25f; yaw += side; }

        /// <summary>Touch aim assist: look slows down while the crosshair is on (or just off) a hostile you can see, so small corrections don't overshoot.</summary>
        float TouchAssist()
        {
            var G = Game.I; var pe = EyePos; var f = cam.transform.forward; float lim = Mathf.Cos((weapons.ads > 0.5f ? 2.5f : 4.5f) * Mathf.Deg2Rad);
            foreach (var h in G.hostiles)
            {
                if (!h || h.Down || h.ally) continue; var d = h.Center - pe; float m = d.magnitude; if (m > 45f || m < 0.5f) continue;
                if (Vector3.Dot(f, d / m) < lim) continue; if (Physics.Linecast(pe, h.Center, Game.MaskEnv, QueryTriggerInteraction.Ignore)) continue;
                return 0.5f;
            }
            return 1f;
        }

        // ---------------- body armor (Loadout): same armor physics as the hostiles ----------------
        public static readonly string[] ArmorNames = { "None", "Soft vest", "Plate carrier", "Plates + helmet" };
        public static readonly string[] ArmorInfo = { "No armor. Full speed.", "IIIA soft vest: stops handgun rounds to the chest and back. 3% slower.", "Rifle plates front and back over a vest: stops most rifle rounds to the torso. 10% slower.", "Plate carrier plus a ballistic helmet that stops handgun rounds and fragments. 14% slower." };
        static readonly float[] ArmorSpeed = { 1f, 0.97f, 0.9f, 0.86f };
        public float ArmorSpeedMul => ArmorSpeed[Mathf.Clamp(Game.I.settings.playerArmor, 0, 3)];
        /// <summary>
        /// A round hits you. Where it lands (by height on your body) decides whether your armor covers it; the round's energy times its armor-defeat factor
        /// decides whether the armor stops it (soft vest 650 J, plates 4800 J, helmet 650 J: the same thresholds as the hostiles). A stopped round is blunt trauma.
        /// </summary>
        public void Shot(Vector3 at, float energy, Cartridge c, float dmg, string why)
        {
            int a = Mathf.Clamp(Game.I.settings.playerArmor, 0, 3); float rel = (at.y - transform.position.y) / Mathf.Max(0.6f, cc.height);
            bool head = rel > 0.84f, torso = rel > 0.48f && !head; float ae = energy * (c != null ? c.armor : 1f); string stop = null;
            if (torso && a >= 1 && Random.value < 0.85f) // the panels cover most of the torso, not the sides and shoulders
            {
                if (ae < (a == 1 ? 650f : 4800f)) stop = a == 1 ? "Your vest" : "Your plate"; else if (a >= 2) dmg *= 0.75f; // a defeated plate still slows the round
            }
            else if (head && a >= 3 && Random.value < 0.7f && ae < 650f) stop = "Your helmet";
            if (stop != null) { dmg *= stop == "Your plate" ? 0.12f : 0.22f; Hurt(dmg, stop + " stopped a round (" + why + ")"); SFX.Play("ring", at, 0.35f, 1.6f); return; }
            Hurt(dmg, why);
        }

        public void Hurt(float d, string why)
        {
            if (!alive || Game.I.settings.god || Game.I.protectT > 0) return; hp -= d; hurtAt = Time.time; hurtFlash = 1; Game.I.Feed(why);
            if (hp <= 0) { hp = 0; alive = false; Game.I.OnPlayerDied(); }
        }

        void Update()
        {
            var G = Game.I; if (G == null || G.state != Game.State.Playing) return; float dt = Time.unscaledDeltaTime * (Time.timeScale < 1 ? Mathf.Max(Time.timeScale, 0.3f) : 1f);
            hurtFlash = Mathf.Max(0, hurtFlash - Time.deltaTime * 1.5f);
            if (!alive) { cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, new Vector3(0, 0.3f, 0), Time.deltaTime * 3); return; }
            // safety net: fell out of the map somehow, put back at the spawn point
            if (transform.position.y < -4f) { Teleport(spawnAt, yaw, 0); G.Feed("Fell out of the map: back at the start"); }
            if (wraith > 0) { wraith -= Time.deltaTime; if (wraith <= 0) { wraith = 0; G.Feed("Wraith Form ended"); } } if (wraithCd > 0) wraithCd -= Time.deltaTime; GhostDoors(wraith > 0);
            // look
            bool aim = BPInput.Aim.IsPressed();
            if (!weapons.wheelOpen) { float zoom = cam.fieldOfView / G.settings.fov; var d = BPInput.LookDegrees(G.settings, Time.unscaledDeltaTime, zoom, aim) + TouchUI.Look * G.settings.sens * zoom * (TouchUI.Active ? TouchAssist() : 1f); if (Specials.I.bladeMode) { Specials.I.BladeSteer(d); d = Vector2.zero; } yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -85, 85); }
            recoilP = Mathf.Lerp(recoilP, 0, Time.deltaTime * 7);
            // move
            var mvIn = BPInput.Move.ReadValue<Vector2>() + TouchUI.Move; if (mvIn.magnitude < 0.12f) mvIn = Vector2.zero; if (mvIn.sqrMagnitude > 1) mvIn.Normalize(); moveIn = mvIn;
            float f = mvIn.y, s = mvIn.x; bool crouchKey = BPInput.Crouch.IsPressed() || TouchUI.Crouch, jump = BPInput.Jump.WasPressedThisFrame() || TouchUI.Jump;
            // classic controller sprint: click the stick once, sprint lasts until you stop pushing forward
            if (BPInput.Sprint.WasPressedThisFrame() && BPInput.UsingPad) padSprint = true; if (f < 0.5f) padSprint = false;
            bool sprintKey = BPInput.Sprint.IsPressed() || padSprint || TouchUI.Sprint;
            crouched = crouchKey; crouchK = Mathf.MoveTowards(crouchK, crouched ? 1 : 0, Time.deltaTime * 6);
            sprinting = sprintKey && f > 0 && weapons.ads < 0.3f && crouchK < 0.5f;
            float spd = (crouchK > 0.5f ? 1.9f : sprinting ? 6f * (1 + 0.08f * Progress.Skill("o_sprint")) : Mathf.Lerp(3.8f, 2.3f, weapons.ads)) * (wraith > 0 ? 1.3f : 1) * (weapons.W != null ? weapons.W.mobility : 1) * ArmorSpeedMul;
            { int rg = Progress.Skill("o_regen"); if (rg > 0 && Time.time - hurtAt > 4 && hp < MaxHp) hp = Mathf.Min(MaxHp, hp + 2 * rg * Time.deltaTime); }
            var mv = transform.forward * f + transform.right * s; if (mv.sqrMagnitude > 1) mv.Normalize();
            if (cc.isGrounded) { vy = -1f; if (jump && crouchK < 0.5f) vy = 3.6f; } else vy -= 9.8f * Time.deltaTime;
            cc.Move((mv * spd + Vector3.up * vy) * Time.deltaTime);
            var hv = cc.velocity; hv.y = 0; speed = hv.magnitude; bob += Time.deltaTime * speed * 2.2f;
            cc.height = Mathf.Lerp(1.8f, 1.2f, crouchK); cc.center = new Vector3(0, cc.height / 2, 0);
            // lean (Q/E)
            float leanIn = (BPInput.LeanRight.IsPressed() ? 1 : 0) - (BPInput.LeanLeft.IsPressed() ? 1 : 0); lean = Mathf.Lerp(lean, leanIn, Time.deltaTime * 8);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            float eye = Mathf.Lerp(eyeH, 1.08f, crouchK) + Mathf.Sin(bob * 2) * 0.025f * Mathf.Min(1, speed / 4f) * (1 - weapons.ads * 0.8f);
            cam.transform.localPosition = new Vector3(lean * 0.38f, eye - Mathf.Abs(lean) * 0.05f, 0);
            cam.transform.localRotation = Quaternion.Euler(pitch - recoilP, 0, -lean * 8f);
            cam.fieldOfView = Mathf.Lerp(G.settings.fov, weapons.W.adsFov, weapons.ads);
            if (weapons.wheelOpen) { var md = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.delta.ReadValue() : Vector2.zero; var sd = UnityEngine.InputSystem.Gamepad.current != null ? UnityEngine.InputSystem.Gamepad.current.rightStick.ReadValue() * 12 : Vector2.zero; weapons.wheelV = Vector2.ClampMagnitude(weapons.wheelV + md + sd, 60); float L = weapons.wheelV.magnitude; weapons.wheelPick = L > 16 ? ((Mathf.RoundToInt(Mathf.Atan2(weapons.wheelV.y, weapons.wheelV.x) / (Mathf.PI / 4)) % 8) + 8) % 8 : -1; }
            // weapons
            if (BPInput.Reload.WasPressedThisFrame() || TouchUI.Reload) weapons.StartReload();
            if (TouchUI.Switch) weapons.Cycle(1);
            if (BPInput.DeadEye.WasPressedThisFrame() || TouchUI.DeadEye) Specials.I.DeadEyeKey();
            if (BPInput.Wraith.WasPressedThisFrame()) WraithKey();
            if (BPInput.ToggleView.WasPressedThisFrame()) thirdPerson = !thirdPerson;
            if (BPInput.SuperShot.WasPressedThisFrame()) { weapons.superShot = !weapons.superShot; G.Feed("Bow super shot " + (weapons.superShot ? "on: full draws pin people to walls" : "off")); }
            if (BPInput.Weapon1.WasPressedThisFrame()) weapons.SelectSlot(0); if (BPInput.Weapon2.WasPressedThisFrame()) weapons.SelectSlot(1); if (BPInput.Weapon3.WasPressedThisFrame()) weapons.SelectSlot(2); if (BPInput.Weapon4.WasPressedThisFrame()) weapons.SelectSlot(3);
            if (BPInput.Melee.WasPressedThisFrame()) weapons.CycleMelee(); if (BPInput.Grenade.WasPressedThisFrame()) weapons.QuickNade();
            if (BPInput.Bow.WasPressedThisFrame()) weapons.QuickSelect("bow"); if (BPInput.Flamer.WasPressedThisFrame()) weapons.QuickSelect("flamer");
            if (BPInput.DoorOpen.WasPressedThisFrame()) G.map.DoorAction(false); if (BPInput.DoorKick.WasPressedThisFrame()) G.map.DoorAction(true);
            if ((BPInput.Interact.WasPressedThisFrame() || TouchUI.Use) && !G.TryArrest()) flashlight.intensity = flashlight.intensity > 0 ? 0 : 6f;
            // slow motion: tap toggles; hold 2 s for Chronoshift (time stop); tap again during it to end it
            if (BPInput.SlowMo.WasPressedThisFrame()) { if (G.timeStop) { Specials.I.StopTimeStop(); slowHeld = -2; } else { slowHeld = 0; slowFired = false; } }
            if (slowHeld >= 0 && BPInput.SlowMo.IsPressed()) { slowHeld += Time.unscaledDeltaTime; if (slowHeld >= 2 && !slowFired) { slowFired = true; Specials.I.StartTimeStop(); } }
            if (BPInput.SlowMo.WasReleasedThisFrame()) { if (slowHeld >= 0 && !slowFired && Specials.I.dePhase == "off" && !G.KillCamActive) Time.timeScale = Time.timeScale < 1 ? 1 : G.settings.slowmo; slowHeld = -1; }
            if (BPInput.NextWeapon.WasPressedThisFrame()) weapons.Cycle(1); if (BPInput.PrevWeapon.WasPressedThisFrame()) weapons.Cycle(-1);
            weapons.Tick(Time.deltaTime);
        }

        /// <summary>Third person over the shoulder, and the takedown camera; both after the first-person camera has been placed.</summary>
        void LateUpdate()
        {
            var G = Game.I; if (G == null) return;
            bool tp = thirdPerson && alive && !G.KillCamActive; if (tp || G.KillCamActive) { if (!body) body = PlayerBody.Make(this); }
            if (body) body.Show(tp || G.KillCamActive, this);
            if (tp)
            {
                var eye = transform.position + Vector3.up * Mathf.Lerp(eyeH, 1.08f, crouchK); var rot = cam.transform.rotation; var want = eye + rot * new Vector3(0.55f, 0.25f, -2.4f);
                var dir = want - eye; float L = dir.magnitude; if (Physics.SphereCast(eye, 0.2f, dir / L, out var h, L, Game.MaskEnv, QueryTriggerInteraction.Ignore)) want = eye + dir / L * Mathf.Max(0.3f, h.distance - 0.05f);
                cam.transform.position = want;
            }
            if (G.KillCamActive) G.ApplyKillCam(cam);
        }
            /// <summary>Walking into loose objects shoves them: light ones get knocked loose, moving ones are pushed.</summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.moveDirection.y < -0.3f || speed < 0.8f) return; var dir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z).normalized;
            var lp = hit.collider.GetComponent<LooseProp>(); if (lp && !lp.awake) { if (lp.mass <= 60) lp.Wake(dir * Mathf.Min(speed, 5) * 6f, hit.point); return; }
            var rb = hit.collider.attachedRigidbody; if (rb && !rb.isKinematic) rb.AddForceAtPosition(dir * 400f, hit.point, ForceMode.Force);
        }
}
}
