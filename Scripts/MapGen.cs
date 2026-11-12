using System.Collections.Generic;
using UnityEngine;

namespace BreachPoint
{
    /// <summary>
    /// Map core shared by every theme (Warehouse, Neon district, Fantasy keep, Bosch's Hell, Baba Yaga's forest):
    /// world root, solid boxes for movement and the 0.5 m nav grid, A* paths, six fixture lights, fog/ambient/sky, and per-theme ambient animation.
    /// Theme builders live in MapWarehouse.cs, MapNeon.cs, MapFantasy.cs and MapMyth.cs; doors in Doors.cs.
    /// </summary>
    public partial class MapGen : MonoBehaviour
    {
        public const float GW = 48, GD = 32, GH = 9, CS = 0.5f;
        public static readonly string[] Maps = { "warehouse", "neon", "fantasy", "hell", "yaga", "space", "random" };
        public static readonly string[] MapNames = { "Warehouse", "Neon district", "Fantasy keep", "Bosch's Hell", "Baba Yaga's forest", "Yggdrasil Station", "Random" };
        public const int LayerBlock = 11;   // invisible movement blockers: bullets pass through these
        public const int LayerPortal = 13;  // rift triggers
        public int NX = (int)(GW / CS), NZ = (int)(GD / CS); byte[] grid;
        public Transform world; public string cur = "warehouse"; public int density = 1, lighting = 1;
        public readonly List<Bounds> solids = new List<Bounds>(); readonly List<Vector4> placed = new List<Vector4>();
        public readonly List<Light> fixtures = new List<Light>();
        System.Random rng; Transform sky; Material wallOverride;
        public Vector3 PlayerSpawn => arena ? new Vector3(-ArenaW / 2 + 1.5f, 0, 0) : hasStartRoom ? startSpawn : new Vector3(-GW / 2 + 3, 0, 0);

        float Rn() => (float)rng.NextDouble();
        float R(float a, float b) => a + Rn() * (b - a);
        T Pick<T>(IList<T> l) => l[rng.Next(l.Count)];
        float L3(float dim, float normal, float bright) => lighting == 0 ? dim : lighting == 2 ? bright : normal;
        int D3(int sparse, int normal, int dense) => density == 0 ? sparse : density == 2 ? dense : normal;

        void Awake()
        {
            for (int i = 0; i < 6; i++) { var l = new GameObject("Fixture" + i).AddComponent<Light>(); l.transform.SetParent(transform, false); l.type = LightType.Point; l.shadows = i < 4 ? LightShadows.Soft : LightShadows.None; fixtures.Add(l); }
        }

        // ---------------- building blocks (web: addBox, addCollider, envAdd, rectFree) ----------------
        public GameObject Box(float cx, float cy, float cz, float w, float h, float d, Material m, bool solid = true, float ry = 0)
        {
            var g = Obj.Mesh(world, Geo.Box(w, h, d), m, new Vector3(cx, cy, cz), Quaternion.Euler(0, ry * Mathf.Rad2Deg, 0), false, "Box");
            var bc = g.AddComponent<BoxCollider>(); bc.size = new Vector3(w, h, d); g.layer = Game.LayerEnv;
            if (solid) solids.Add(new Bounds(new Vector3(cx, cy, cz), new Vector3(w, h, d)));
            return g;
        }
        public void Block(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            var b = new Bounds(); b.SetMinMax(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)); solids.Add(b);
            var g = new GameObject("Blocker"); g.transform.SetParent(world, false); g.transform.position = b.center; g.layer = LayerBlock; g.AddComponent<BoxCollider>().size = b.size;
        }
        /// <summary>A mesh bullets can hit (web envAdd).</summary>
        public GameObject Env(Mesh mesh, Material m, Vector3 p, Quaternion r) { var g = Obj.Mesh(world, mesh, m, p, r, true, "Env"); g.layer = Game.LayerEnv; return g; }
        public GameObject Env(Mesh mesh, Material m, Vector3 p) => Env(mesh, m, p, Quaternion.identity);
        /// <summary>Decoration only: no collider.</summary>
        public GameObject Deco(Mesh mesh, Material m, Vector3 p, Quaternion r, Transform parent = null) => Obj.Mesh(parent ? parent : world, mesh, m, p, r, false, "Deco");
        public GameObject Deco(Mesh mesh, Material m, Vector3 p) => Deco(mesh, m, p, Quaternion.identity);
        public GameObject Floor(Material m, float w, float d, float y = 0) { var g = Env(Geo.Plane(w, d), m, new Vector3(0, y, 0), Geo.E(-Mathf.PI / 2, 0, 0)); return g; }
        bool RectFree(float x0, float z0, float x1, float z1, float m)
        {
            if (x0 < -GW / 2 + 1 || x1 > GW / 2 - 1 || z0 < -GD / 2 + 1 || z1 > GD / 2 - 1) return false; if (x0 < -GW / 2 + 7) return false;
            foreach (var r in placed) if (x0 < r.z + m && x1 > r.x - m && z0 < r.w + m && z1 > r.y - m) return false; return true;
        }
        void Placed(float x0, float z0, float x1, float z1) => placed.Add(new Vector4(x0, z0, x1, z1));

        void Fixture(int i, Vector3 p, int col, float range, float intensity) { var l = fixtures[i]; l.enabled = true; l.transform.position = p; l.color = Cv.Hex(col); l.range = range; l.intensity = intensity * 3f; }
        void Env3(int bg, int fog, float near, float far, int hemi, int ground, float k)
        {
            var cam = Camera.main; if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Cv.Hex(bg); cam.farClipPlane = 220; }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = Cv.Hex(fog); RenderSettings.fogStartDistance = near; RenderSettings.fogEndDistance = far;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight; var a = Cv.Hex(hemi) * k; var gr = Cv.Hex(ground) * k;
            RenderSettings.ambientSkyColor = a; RenderSettings.ambientGroundColor = gr; RenderSettings.ambientEquatorColor = (a + gr) * 0.5f;
        }
        /// <summary>A gradient sky drawn behind everything (a small dome that follows the camera, so fog never reaches it).</summary>
        Transform SkyDome(Texture2D grad, int moonCol = -1, Vector3 moonDir = default, float moonR = 0, bool stars = false)
        {
            var m = Mat.BasicTex(grad, Color.white); m.SetFloat("_Cull", 1); m.SetFloat("_ZWrite", 0); m.renderQueue = 1000;
            var d = Obj.Mesh(world, Geo.Sphere(10, 24, 16), m, Vector3.zero, Quaternion.identity, false, "Sky").transform;
            foreach (var r in d.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (moonCol >= 0)
            {
                var mm = Mat.Hdr(moonCol, 1.6f); mm.SetFloat("_ZWrite", 0); mm.renderQueue = 1001; mm.SetFloat("_Cull", 0);
                var mo = Obj.Mesh(d, Geo.Circle(moonR / 8f, 24), mm, moonDir.normalized * 9.5f, Quaternion.LookRotation(-moonDir.normalized), false, "Moon");
            }
            if (stars)
            {
                var sm = Mat.Basic(new Color(0.9f, 0.94f, 1f)); sm.SetFloat("_ZWrite", 0); sm.renderQueue = 1001;
                for (int i = 0; i < 260; i++) { float a = Cv.R() * 6.283f, e = 0.2f + Cv.R() * 1.3f; var p = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e)) * 9.4f; Obj.Mesh(d, Geo.Box(0.018f, 0.018f, 0.018f), sm, p, Quaternion.identity, false, "Star"); }
            }
            sky = d; return d;
        }

        // ---------------- build ----------------
        public void Build(int seed, string mapId, int dens, int light, bool duelArena = false)
        {
            density = dens; lighting = light; arena = duelArena;
            UnrigLights(); if (world) Destroy(world.gameObject); world = new GameObject("World").transform; world.SetParent(transform, false);
            solids.Clear(); placed.Clear(); doors.Clear(); ClearAmbient(); sky = null; wallOverride = null; hasStartRoom = false; breachDoor = null; fallenHuts.Clear(); WorldPhysics.ClearDebris();
            foreach (var l in fixtures) { l.enabled = false; l.intensity = 0; }
            rng = new System.Random(seed);
            cur = mapId == "random" ? Maps[rng.Next(Maps.Length - 1)] : mapId;
            if (arena) BuildArena(); else switch (cur)
            {
                case "neon": BuildNeon(); break;
                case "fantasy": BuildFantasy(); break;
                case "hell": BuildHell(); break;
                case "yaga": BuildYaga(); break;
                case "space": BuildSpace(); break;
                default: cur = "warehouse"; BuildWarehouse(); break;
            }
            // every mode but the duel starts behind a locked door; crates, cartons, pallets and barrels become loose objects
            if (!arena) BuildStartRoom();
            RegisterLoose(); RigLights(); ScatterBottles(); Illuminate();
            foreach (var r in world.GetComponentsInChildren<Renderer>()) if (r.gameObject.name == "Deco" && r.bounds.size.y < 0.05f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BuildGrid();
            var probe = new GameObject("Reflections").AddComponent<ReflectionProbe>(); probe.transform.SetParent(world, false); probe.transform.position = new Vector3(0, 2, 0); probe.size = new Vector3(GW, GH, GD);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime; probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting; probe.resolution = 128; probe.boxProjection = true; probe.importance = 0; probe.RenderProbe();
            LocalProbes();
        }

        Light fill;
        /// <summary>
        /// Every room fully lit (Normal and Bright lighting): bright ambient in the map's own tint, a soft shadowless fill from above that reaches inside
        /// every room, and the fog pushed back so the far end of a hall isn't black. Dim keeps each map's original moody lighting.
        /// </summary>
        void Illuminate()
        {
            if (!fill)
            {
                fill = new GameObject("FillLight").AddComponent<Light>(); fill.transform.SetParent(transform, false); fill.type = LightType.Directional; fill.shadows = LightShadows.None;
                fill.transform.rotation = Quaternion.Euler(62, 35, 0); fill.cullingMask = ~(1 << Game.LayerViewModel);
            }
            fill.enabled = lighting > 0; if (lighting == 0) return;
            float k = lighting == 2 ? 1.2f : 1f;
            Color Lift(Color c, float min) { float m = Mathf.Max(c.maxColorComponent, 1e-3f); var hue = c / m; float v = Mathf.Max(m, min) * k; return new Color(Mathf.Lerp(1, hue.r, 0.3f), Mathf.Lerp(1, hue.g, 0.3f), Mathf.Lerp(1, hue.b, 0.3f)) * v; }
            RenderSettings.ambientSkyColor = Lift(RenderSettings.ambientSkyColor, 0.85f); RenderSettings.ambientEquatorColor = Lift(RenderSettings.ambientEquatorColor, 0.68f); RenderSettings.ambientGroundColor = Lift(RenderSettings.ambientGroundColor, 0.5f);
            fill.intensity = 0.55f * k; fill.color = Color.Lerp(Color.white, RenderSettings.ambientSkyColor / Mathf.Max(0.01f, RenderSettings.ambientSkyColor.maxColorComponent), 0.25f);
            RenderSettings.fogStartDistance *= 1.8f; RenderSettings.fogEndDistance *= 1.8f;
            var fc = RenderSettings.fogColor; RenderSettings.fogColor = Color.Lerp(fc, RenderSettings.ambientEquatorColor * 0.35f, 0.5f);
        }

        /// <summary>
        /// Local reflections: a grid of box-projected probes, each captured once from its own spot, so a pool of blood or a puddle reflects the lights and walls
        /// actually around it (one probe for the whole map reflected the middle of the map everywhere). Lowest quality keeps the single probe.
        /// </summary>
        void LocalProbes()
        {
            int q = Game.I.settings.quality; if (q <= 0 || Game.Mobile) return; float cell = q >= 2 ? 12f : 18f;
            int nx = Mathf.Clamp(Mathf.CeilToInt(GW / cell), 1, 6), nz = Mathf.Clamp(Mathf.CeilToInt(GD / cell), 1, 6); float cw = GW / nx, cd = GD / nz, eye = 1.6f;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = new Vector3(-GW / 2 + (i + 0.5f) * cw, eye, -GD / 2 + (j + 0.5f) * cd);
                    var pr = new GameObject("LocalReflections").AddComponent<ReflectionProbe>(); pr.transform.SetParent(world, false); pr.transform.position = p;
                    pr.size = new Vector3(cw + 2, GH, cd + 2); pr.center = new Vector3(0, GH / 2 - eye, 0); pr.blendDistance = 1f; pr.importance = 1;
                    pr.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime; pr.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting; pr.resolution = q >= 2 ? 128 : 64; pr.boxProjection = true; pr.RenderProbe();
                }
        }

        // ---------------- nav grid and paths ----------------
        void BuildGrid()
        {
            grid = new byte[NX * NZ];
            for (int i = 0; i < NX; i++) for (int j = 0; j < NZ; j++)
                {
                    float x0 = -GW / 2 + i * CS, z0 = -GD / 2 + j * CS; byte b = (byte)((i == 0 || j == 0 || i == NX - 1 || j == NZ - 1) ? 1 : 0);
                    if (b == 0) foreach (var c in solids) { if (c.min.y > 1.2f || c.max.y < 0.2f) continue; if (x0 < c.max.x + 0.3f && x0 + CS > c.min.x - 0.3f && z0 < c.max.z + 0.3f && z0 + CS > c.min.z - 0.3f) { b = 1; break; } }
                    grid[i + j * NX] = b;
                }
        }
        /// <summary>Add or remove an obstacle after the map was built (Mystery Box, ammo crate) and refresh the nav grid.</summary>
        public void AddSolid(Bounds b) { solids.Add(b); BuildGrid(); }
        public void RemoveSolid(Bounds b) { if (solids.Remove(b)) BuildGrid(); }
        /// <summary>An environment object was cut loose: drop its obstacle box and any invisible blockers inside it, then refresh the nav grid.</summary>
        public void Uncover(Bounds b, bool rebuild = true)
        {
            var big = b; big.Expand(0.35f); int n = solids.RemoveAll(s => big.Contains(s.center) && s.size.x <= big.size.x + 0.05f && s.size.y <= big.size.y + 0.05f && s.size.z <= big.size.z + 0.05f);
            foreach (Transform t in world) if (t.name == "Blocker" && t.gameObject.layer == LayerBlock) { var c = t.GetComponent<BoxCollider>(); if (c && big.Contains(c.bounds.min) && big.Contains(c.bounds.max)) Object.Destroy(t.gameObject); }
            if (n > 0 && rebuild) BuildGrid(); else if (n > 0) navDirty = true;
        }
        int CellX(float x) => Mathf.FloorToInt((x + GW / 2) / CS); int CellZ(float z) => Mathf.FloorToInt((z + GD / 2) / CS);
        public Vector3 CellPos(int x, int z) => new Vector3(-GW / 2 + (x + 0.5f) * CS, 0, -GD / 2 + (z + 0.5f) * CS);
        public bool Free(Vector3 p) { int x = CellX(p.x), z = CellZ(p.z); return x >= 0 && z >= 0 && x < NX && z < NZ && grid[x + z * NX] == 0; }

        public Vector3 PushOut(Vector3 p, float r)
        {
            p.x = Mathf.Clamp(p.x, -GW / 2 + r, GW / 2 - r); p.z = Mathf.Clamp(p.z, -GD / 2 + r, GD / 2 - r);
            foreach (var b in solids)
            {
                if (b.min.y > 1.2f || b.max.y < 0.2f) continue;
                float minX = b.min.x - r, maxX = b.max.x + r, minZ = b.min.z - r, maxZ = b.max.z + r;
                if (p.x > minX && p.x < maxX && p.z > minZ && p.z < maxZ)
                {
                    float dl = p.x - minX, dr = maxX - p.x, dd = p.z - minZ, du = maxZ - p.z, m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dd, du));
                    if (m == dl) p.x = minX; else if (m == dr) p.x = maxX; else if (m == dd) p.z = minZ; else p.z = maxZ;
                }
            }
            foreach (var d in doors) if (d.Blocks) { var b = d.box; float minX = b.min.x - r, maxX = b.max.x + r, minZ = b.min.z - r, maxZ = b.max.z + r; if (p.x > minX && p.x < maxX && p.z > minZ && p.z < maxZ) { if (d.axisX) p.z = p.z < d.cz ? minZ : maxZ; else p.x = p.x < d.cx ? minX : maxX; } }
            return p;
        }

        /// <summary>A free cell at least minDist from awayFrom and, like the web, past the spawn end (x ≥ −GW/2 + 14).</summary>
        /// <summary>A free spot close to a point (rings out to 3 m), or the point itself.</summary>
        public Vector3 NearFree(Vector3 c, float minR = 1f)
        {
            for (float r = minR; r <= 3.01f; r += 0.5f) for (int a = 0; a < 12; a++) { float t = a * Mathf.PI / 6 + r; var q = c + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * r; if (Free(q) && !Physics.Linecast(c + Vector3.up, q + Vector3.up, Game.MaskEnv, QueryTriggerInteraction.Ignore)) return q; }
            return c;
        }
        public Vector3 RandomFree(Vector3 awayFrom, float minDist, bool pastSpawn = true)
        {
            for (int k = 0; k < 600; k++) { int x = Random.Range(0, NX), z = Random.Range(0, NZ); if (grid[x + z * NX] != 0) continue; var p = CellPos(x, z); if (pastSpawn && p.x < -GW / 2 + 14) continue; if (Vector3.Distance(p, awayFrom) >= minDist) return p; }
            return CellPos(NX - 4, NZ / 2);
        }
        public Vector3 FarFrom(Vector3 from, float minD) { Vector3 best = from; float bs = -1; for (int k = 0; k < 40; k++) { var p = RandomFree(from, minD, false); float d = Vector3.Distance(p, from); if (d > bs) { bs = d; best = p; } } return best; }

        public List<Vector3> FindPath(Vector3 a, Vector3 b)
        {
            int ax = Mathf.Clamp(CellX(a.x), 0, NX - 1), az = Mathf.Clamp(CellZ(a.z), 0, NZ - 1), bx = Mathf.Clamp(CellX(b.x), 0, NX - 1), bz = Mathf.Clamp(CellZ(b.z), 0, NZ - 1);
            int start = az * NX + ax, goal = bz * NX + bx; if (grid[goal] != 0) goal = NearestFree(goal); if (goal < 0) return null;
            var g = new Dictionary<int, float> { [start] = 0 }; var came = new Dictionary<int, int>(); var open = new List<int> { start }; var f = new Dictionary<int, float> { [start] = H(start, goal) };
            int iter = 0;
            while (open.Count > 0 && iter++ < 6000)
            {
                int bi = 0; for (int i = 1; i < open.Count; i++) if (f[open[i]] < f[open[bi]]) bi = i; int cur = open[bi]; open.RemoveAt(bi);
                if (cur == goal) { var outp = new List<Vector3>(); while (came.ContainsKey(cur)) { outp.Add(CellPos(cur % NX, cur / NX)); cur = came[cur]; } outp.Reverse(); return outp; }
                int cx = cur % NX, cz = cur / NX;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue; int nx = cx + dx, nz = cz + dz; if (nx < 0 || nz < 0 || nx >= NX || nz >= NZ) continue; int n = nz * NX + nx; if (grid[n] != 0 && n != start) continue;
                        if (dx != 0 && dz != 0 && (grid[cz * NX + nx] != 0 || grid[nz * NX + cx] != 0)) continue;
                        float ng = g[cur] + (dx != 0 && dz != 0 ? 1.414f : 1f);
                        if (!g.TryGetValue(n, out var og) || ng < og) { g[n] = ng; came[n] = cur; f[n] = ng + H(n, goal); if (!open.Contains(n)) open.Add(n); }
                    }
            }
            return null;
        }
        float H(int a, int b) { int dx = Mathf.Abs(a % NX - b % NX), dz = Mathf.Abs(a / NX - b / NX); return Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz); }
        int NearestFree(int c) { int cx = c % NX, cz = c / NX; for (int r = 1; r < 10; r++) for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) { int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= NX || z >= NZ) continue; if (grid[z * NX + x] == 0) return z * NX + x; } return -1; }

        public bool LOS(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, Game.MaskEnv, QueryTriggerInteraction.Ignore);

        // ---------------- per-frame ambient ----------------
        void Update()
        {
            var cam = Camera.main; if (sky && cam) sky.position = cam.transform.position;
            float dt = Time.deltaTime, rdt = Time.unscaledDeltaTime;
            UpdateDoors(dt); FlushNav();
            if (cur == "neon") UpdateNeon(dt, rdt); else if (cur == "fantasy") UpdateFantasy(dt, rdt); else if (cur == "hell" || cur == "yaga") UpdateMyth(dt, rdt); else if (cur == "space") UpdateSpace(dt, rdt);
        }
        void ClearAmbient() { ClearNeon(); ClearFantasy(); ClearMyth(); ClearSpace(); }

        // ---------------- shared textures ----------------
        static Texture2D gradCache;
        public static Texture2D Grad(params (float t, int c)[] stops) { var cv = new Cv(4, 256); var s = new (float, Color)[stops.Length]; for (int i = 0; i < stops.Length; i++) s[i] = (stops[i].t, Cv.Hex(stops[i].c)); cv.GradV(s); return cv.Tex(false); }
    }
}
