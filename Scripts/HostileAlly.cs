using UnityEngine;

namespace BreachPoint
{
    /// <summary>
    /// The teammate bot (Settings ▸ Game ▸ Teammate bot): a soldier on your side built on the same body as the hostiles (same wounds, armor, ragdoll),
    /// who follows a little behind you, picks out visible hostiles and shoots them. Hostiles target whichever of you is closer and in sight.
    /// Its shots never hurt you, and it isn't counted in kills, arrests or the win check. Default look: a ghost (translucent blue, like Wraith Form).
    /// </summary>
    public partial class Hostile
    {
        public bool ally; bool tgtAlly; Hostile allyTarget; float allyScanT, allyStuckT; Vector3 allyLastPos;

        /// <summary>Make this body the teammate: its name, a rifle, and the ghost look if chosen.</summary>
        public void MakeAlly(bool ghostLook)
        {
            ally = true; tag2 = "Ghost"; prof = "rifle"; accuracy = 0.8f; aggression = 0.5f; morale = 1; state = HState.Engage;
            if (ghostLook)
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (!r) continue; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetVector("_Tint", new Vector4(0.6f, 0.85f, 1f, 0.8f)); r.SetPropertyBlock(mpb);
                }
        }

        void AllyThink(float dt)
        {
            var G = Game.I; var P = G.player; Vector3 p = root.position, eye = p + Vector3.up * 1.6f;
            // pick the nearest hostile it can see (rescanned a few times a second)
            allyScanT -= dt;
            if (allyScanT <= 0)
            {
                allyScanT = 0.3f; allyTarget = null; float best = 45f;
                foreach (var h in G.hostiles)
                {
                    if (!h || h.Down || h.ally || h.hostage || h.state == HState.Surrender || h.state == HState.Arrested) continue;
                    var c = h.Center; float d = Vector3.Distance(eye, c); if (d >= best) continue;
                    if (Physics.Linecast(eye, c, Game.MaskEnv, QueryTriggerInteraction.Ignore)) continue; best = d; allyTarget = h;
                }
            }
            if (allyTarget && allyTarget.Down) allyTarget = null;
            los = allyTarget != null;

            // stay a little behind and to the right of the player
            var pp = P.transform.position; var spot = pp - P.transform.forward * 1.8f + P.transform.right * 1.2f; var fp = pp - p; fp.y = 0; float dp = fp.magnitude;
            if (dp > 3.2f)
            {
                repathT -= dt; if (path == null || path.Count == 0 || repathT <= 0) { path = G.map.FindPath(p, spot); repathT = 0.8f; }
                FollowPath(dt, dp > 8f ? 1.45f : 1f);
                // stuck or left far behind out of sight: catch up
                allyStuckT = (p - allyLastPos).sqrMagnitude < 0.0004f ? allyStuckT + dt : 0; allyLastPos = p;
                if ((allyStuckT > 3f || dp > 30f) && Vector3.Dot(P.cam.transform.forward, (p - P.EyePos).normalized) < 0.2f) // only while you're not looking at it
                { root.position = G.map.NearFree(pp, 1.5f); path = null; allyStuckT = 0; }
            }
            else moving = false;

            if (allyTarget)
            {
                var c = allyTarget.Center + Vector3.up * 0.15f; var to = c - eye; float dist = new Vector2(to.x, to.z).magnitude;
                Turn(Mathf.Atan2(to.x, to.z), dt, 6); aimPitch = Mathf.Atan2(c.y - (p.y + 1.45f), dist); aimBlend = Mathf.Min(1, aimBlend + dt * 3);
                if (Game.I.settings.behavior != 2) { int before = burstLeft; FireCtl(dt, dist, c); if (burstLeft != before) G.Noise(p, 25); }
            }
            else
            {
                aimBlend = Mathf.MoveTowards(aimBlend, 0.35f, dt * 2); aimPitch = 0;
                if (!moving) Turn(P.yaw * Mathf.Deg2Rad, dt, 2);
            }
        }

        /// <summary>For hostiles: is the teammate a better target than the player right now (closer, or the player is out of sight)?</summary>
        bool AllyIsTarget(Vector3 eye, float playerDist, bool playerSeen, out Vector3 allyEye)
        {
            allyEye = default; var a = Game.I.ally; if (!a || a.Down) return false;
            allyEye = a.root.position + Vector3.up * 1.6f; var to = allyEye - eye; float d = new Vector2(to.x, to.z).magnitude;
            if (playerSeen && d >= playerDist) return false; if (d > 60f) return false;
            return !Physics.Linecast(eye, allyEye, Game.MaskEnv, QueryTriggerInteraction.Ignore);
        }
    }
}
