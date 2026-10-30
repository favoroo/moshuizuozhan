using System.Collections.Generic;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 零 GC 视觉反馈池：负责墨水落点溅射粒子、手雷/大招冲击波环以及地面延时预警圈。
    /// </summary>
    public class VfxPool : MonoBehaviour
    {
        public static VfxPool Instance { get; private set; }

        private readonly Queue<ParticleSystem> _splatParticles = new Queue<ParticleSystem>();
        private readonly List<ExpandingRing> _activeRings = new List<ExpandingRing>();
        private readonly Queue<ExpandingRing> _ringPool = new Queue<ExpandingRing>();
        private readonly List<PendingRing> _pendingRings = new List<PendingRing>(4);
        private readonly List<SatelliteDroplet> _activeDroplets = new List<SatelliteDroplet>(24);
        private readonly Queue<SatelliteDroplet> _dropletPool = new Queue<SatelliteDroplet>(24);

        private Material _sharedParticleMat;
        private Material _sharedRingMat;
        private Mesh _discMesh;
        private Mesh _dropletMesh;

        private struct SatelliteDroplet
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public Vector3 Velocity;
            public float Gravity;
            public float Elapsed;
            public float Lifetime;
            public float BaseScale;
            public float StretchY;
        }

        private struct PendingRing
        {
            public float Delay;
            public Vector3 Position;
            public Color Color;
            public float StartRadius;
            public float EndRadius;
            public float Duration;
        }

        private struct ExpandingRing
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock PropBlock;
            public Color BaseColor;
            public float StartRadius;
            public float EndRadius;
            public float Elapsed;
            public float Duration;
        }

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            Instance = this;
            InitializeResources();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            DestroyObject(_sharedParticleMat);
            _sharedParticleMat = null;

            DestroyObject(_sharedRingMat);
            _sharedRingMat = null;

            DestroyObject(_discMesh);
            _discMesh = null;
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        /// <summary>当前活跃的扩散环数量（测试/调试可观测，用于零泄露守门）。</summary>
        public int ActiveRingCount => _activeRings.Count;

        /// <summary>当前活跃的卫星墨珠数量（测试/调试可观测，用于零泄露守门）。</summary>
        public int ActiveDropletCount => _activeDroplets.Count;

        /// <summary>等待延迟触发的冲击波环数量。</summary>
        public int PendingRingCount => _pendingRings.Count;

        /// <summary>
        /// 所有有限生命周期的 VFX 是否已经自然回收（不包含 ParticleSystem 自身剩余粒子）。
        /// 供 PlayMode 冒烟测试断言对象池无泄露。
        /// </summary>
        public bool IsEffectPoolSettled =>
            _activeRings.Count == 0 && _activeDroplets.Count == 0 && _pendingRings.Count == 0;

        private void InitializeResources()
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Sprites/Default");

            _sharedParticleMat = new Material(unlitShader) { name = "VfxInkParticleMat" };
            _sharedRingMat = new Material(unlitShader) { name = "VfxRingMat" };
            _discMesh = BuildRingMesh(32, 0.84f, 1.0f);
            _dropletMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

            for (int i = 0; i < 20; i++)
            {
                _splatParticles.Enqueue(CreateSplatParticleSystem(i));
            }

            for (int i = 0; i < 16; i++)
            {
                _ringPool.Enqueue(CreateRingInstance(i));
            }

            for (int i = 0; i < 24; i++)
            {
                _dropletPool.Enqueue(CreateDropletInstance(i));
            }
        }

        public void SpawnInkSplat(Vector3 worldPos, Color color, float radius = 0.9f, int burstCount = 10)
        {
            if (_splatParticles.Count == 0)
                return;

            ParticleSystem ps = _splatParticles.Dequeue();
            _splatParticles.Enqueue(ps);

            ps.transform.position = worldPos + Vector3.up * 0.08f;
            var main = ps.main;
            main.startColor = color;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f * radius, 0.24f * radius);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f * radius, 4.5f * radius);

            ps.Emit(burstCount);
        }

        /// <summary>
        /// 弹丸撞击的综合反馈入口：主溅射粒子 + 沿撞击法线甩出的延迟落地卫星墨珠。
        /// 卫星墨珠只做视觉表现（不参与涂地判定），因此不会给 Ink 系统带来额外耦合。
        /// </summary>
        public void SpawnInkImpact(Vector3 worldPos, Vector3 normal, Color color, float radius = 0.9f, int satelliteCount = 4)
        {
            int burstCount = Mathf.Clamp(Mathf.RoundToInt(6f + radius * 5f), 6, 20);
            SpawnInkSplat(worldPos, color, radius, burstCount);
            SpawnSatelliteDroplets(worldPos, normal, color, radius, satelliteCount);
        }

        private void SpawnSatelliteDroplets(Vector3 worldPos, Vector3 normal, Color color, float radius, int count)
        {
            if (count <= 0)
                return;

            Vector3 up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            Vector3 tangentA = Vector3.ProjectOnPlane(Vector3.right, up);
            if (tangentA.sqrMagnitude < 0.0001f)
                tangentA = Vector3.ProjectOnPlane(Vector3.forward, up);
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(up, tangentA);

            float sizeScale = Mathf.Clamp(radius * 1.2f, 0.7f, 1.6f);

            for (int i = 0; i < count && _dropletPool.Count > 0; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                droplet.Transform.gameObject.SetActive(true);
                droplet.Transform.position = worldPos + up * 0.04f;

                float angle = Random.value * Mathf.PI * 2f;
                Vector3 outward = tangentA * Mathf.Cos(angle) + tangentB * Mathf.Sin(angle);
                Vector3 launchDir = (outward * Random.Range(0.35f, 0.9f) + up * Random.Range(0.75f, 1.35f)).normalized;
                droplet.Velocity = launchDir * (Random.Range(2.6f, 4.6f) * sizeScale);
                droplet.Gravity = Random.Range(16f, 22f);
                droplet.Elapsed = 0f;
                droplet.Lifetime = Random.Range(0.32f, 0.62f);
                droplet.BaseScale = Random.Range(0.045f, 0.085f) * sizeScale;
                droplet.StretchY = 1f;
                droplet.Transform.localScale = Vector3.one * droplet.BaseScale;

                droplet.PropBlock.SetColor(BaseColorProp, color);
                droplet.PropBlock.SetColor(ColorProp, color);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        public void SpawnShockwaveRing(Vector3 worldPos, Color color, float startRadius, float endRadius, float duration = 0.28f)
        {
            ExpandingRing ring = _ringPool.Count > 0 ? _ringPool.Dequeue() : CreateRingInstance(_activeRings.Count + 30);
            ring.Transform.gameObject.SetActive(true);
            ring.Transform.position = new Vector3(worldPos.x, worldPos.y + 0.04f, worldPos.z);
            ring.StartRadius = startRadius;
            ring.EndRadius = endRadius;
            ring.Elapsed = 0f;
            ring.Duration = Mathf.Max(0.05f, duration);
            ring.BaseColor = color;

            UpdateRingVisual(ring, 0f);
            _activeRings.Add(ring);
        }

        /// <summary>
        /// 延迟冲击波环：用于砸地/爆炸的第二层扩散波，制造「余波追上主波」的层次感。
        /// </summary>
        public void SpawnDelayedShockwaveRing(
            float delay,
            Vector3 worldPos,
            Color color,
            float startRadius,
            float endRadius,
            float duration = 0.3f)
        {
            _pendingRings.Add(new PendingRing
            {
                Delay = Mathf.Max(0f, delay),
                Position = worldPos,
                Color = color,
                StartRadius = startRadius,
                EndRadius = endRadius,
                Duration = duration
            });
        }

        /// <summary>
        /// 风暴云落雨：从云底向下坠落的拉伸墨滴（纯视觉，不参与涂地判定）。
        /// </summary>
        public void SpawnRainDrops(Vector3 cloudBottom, float radius, Color color, int count)
        {
            if (count <= 0)
                return;

            float safeRadius = Mathf.Max(0.1f, radius);
            for (int i = 0; i < count && _dropletPool.Count > 0; i++)
            {
                SatelliteDroplet droplet = _dropletPool.Dequeue();
                float angle = Random.value * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Random.value) * safeRadius;

                droplet.Transform.gameObject.SetActive(true);
                droplet.Transform.position = cloudBottom + new Vector3(
                    Mathf.Cos(angle) * distance,
                    Random.Range(0f, 0.6f),
                    Mathf.Sin(angle) * distance);
                droplet.Velocity = Vector3.down * Random.Range(9f, 13f);
                droplet.Gravity = 0f;
                droplet.Elapsed = 0f;
                droplet.Lifetime = Random.Range(0.20f, 0.34f);
                droplet.BaseScale = Random.Range(0.030f, 0.055f);
                droplet.StretchY = Random.Range(2.2f, 3.2f);
                droplet.Transform.localScale = new Vector3(
                    droplet.BaseScale * 0.7f, droplet.BaseScale * droplet.StretchY, droplet.BaseScale * 0.7f);

                droplet.PropBlock.SetColor(BaseColorProp, color);
                droplet.PropBlock.SetColor(ColorProp, color);
                droplet.Renderer.SetPropertyBlock(droplet.PropBlock);

                _activeDroplets.Add(droplet);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = _pendingRings.Count - 1; i >= 0; i--)
            {
                PendingRing pending = _pendingRings[i];
                pending.Delay -= dt;
                if (pending.Delay > 0f)
                {
                    _pendingRings[i] = pending;
                    continue;
                }

                _pendingRings.RemoveAt(i);
                SpawnShockwaveRing(pending.Position, pending.Color, pending.StartRadius, pending.EndRadius, pending.Duration);
            }

            for (int i = _activeRings.Count - 1; i >= 0; i--)
            {
                ExpandingRing r = _activeRings[i];
                r.Elapsed += dt;
                float t = Mathf.Clamp01(r.Elapsed / r.Duration);
                UpdateRingVisual(r, t);

                if (r.Elapsed >= r.Duration)
                {
                    r.Transform.gameObject.SetActive(false);
                    _ringPool.Enqueue(r);
                    _activeRings.RemoveAt(i);
                }
                else
                {
                    _activeRings[i] = r;
                }
            }

            UpdateSatelliteDroplets(dt);
        }

        private void UpdateSatelliteDroplets(float dt)
        {
            for (int i = _activeDroplets.Count - 1; i >= 0; i--)
            {
                SatelliteDroplet droplet = _activeDroplets[i];
                droplet.Elapsed += dt;

                if (droplet.Elapsed >= droplet.Lifetime)
                {
                    droplet.Transform.gameObject.SetActive(false);
                    _dropletPool.Enqueue(droplet);
                    _activeDroplets.RemoveAt(i);
                    continue;
                }

                droplet.Velocity.y -= droplet.Gravity * dt;
                droplet.Transform.position += droplet.Velocity * dt;

                // 末段收缩，读作“墨珠落地后被地面吸收”
                float t = droplet.Elapsed / droplet.Lifetime;
                float scale = droplet.BaseScale * Mathf.Max(0.12f, 1f - t * t);
                droplet.Transform.localScale = new Vector3(scale, scale * droplet.StretchY, scale);

                _activeDroplets[i] = droplet;
            }
        }

        private void UpdateRingVisual(ExpandingRing ring, float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            float radius = Mathf.Lerp(ring.StartRadius, ring.EndRadius, eased);
            ring.Transform.localScale = new Vector3(radius, 1f, radius);

            Color c = Color.Lerp(ring.BaseColor, Color.white, (1f - t) * 0.35f);
            c.a = 1f - t;
            ring.PropBlock.SetColor(BaseColorProp, c);
            ring.PropBlock.SetColor(ColorProp, c);
            ring.Renderer.SetPropertyBlock(ring.PropBlock);
        }

        private ParticleSystem CreateSplatParticleSystem(int idx)
        {
            var go = new GameObject($"InkSplatPS_{idx:00}");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            // 新建的 ParticleSystem 会自动开始播放，必须先彻底停止再改 duration 等参数，
            // 否则 Unity 会持续报 "Setting the duration while system is still playing"。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.15f;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Mesh;
            psr.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            psr.sharedMaterial = _sharedParticleMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;

            return ps;
        }

        private ExpandingRing CreateRingInstance(int idx)
        {
            var go = new GameObject($"ShockwaveRing_{idx:00}");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _discMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedRingMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);

            return new ExpandingRing
            {
                Transform = go.transform,
                Renderer = mr,
                PropBlock = new MaterialPropertyBlock()
            };
        }

        private SatelliteDroplet CreateDropletInstance(int idx)
        {
            var go = new GameObject($"InkSatelliteDrop_{idx:00}");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _dropletMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sharedParticleMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            go.SetActive(false);

            return new SatelliteDroplet
            {
                Transform = go.transform,
                Renderer = mr,
                PropBlock = new MaterialPropertyBlock(),
                BaseScale = 0.06f,
                StretchY = 1f
            };
        }

        public static Mesh BuildRingMesh(int segments, float innerRatio, float outerRadius)
        {
            Mesh mesh = new Mesh { name = "ProceduralFlatRing" };
            int vCount = segments * 2;
            Vector3[] verts = new Vector3[vCount];
            Vector3[] norms = new Vector3[vCount];
            int[] tris = new int[segments * 6];

            float innerR = outerRadius * innerRatio;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                verts[i * 2] = new Vector3(cos * innerR, 0f, sin * innerR);
                verts[i * 2 + 1] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                norms[i * 2] = Vector3.up;
                norms[i * 2 + 1] = Vector3.up;

                int next = (i + 1) % segments;
                int ti = i * 6;
                tris[ti + 0] = i * 2;
                tris[ti + 1] = next * 2 + 1;
                tris[ti + 2] = i * 2 + 1;
                tris[ti + 3] = i * 2;
                tris[ti + 4] = next * 2;
                tris[ti + 5] = next * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
