using System;
using System.Collections;
using P1.Environment;
using P1.Ink;
using UnityEngine;
using UnityEngine.Rendering;

namespace P1.Core
{
    /// <summary>
    /// 环境反射 Cubemap 捕获器（P29 基础设施，P31 升级）：
    /// 战斗场景加载后延时 0.35s 捕获 512×512 RGBAHalf 的场景环境反射立方体贴图，
    /// 推送给 RenderEnvGlobals 供墨面/水面着色器采样。
    /// P31：分辨率 256→512（湿墨/金属反射明显更细腻）；新增结算阶段二次捕获——
    /// 让结算画面的反射内容与满场战况一致（战斗中不重捕获，避免 6 面渲染 spike 撞战斗采样）。
    /// 单次执行无持续开销。
    /// </summary>
    public class ArenaReflectionCapture : MonoBehaviour
    {
        private const int CubeResolution = 512;

        private Cubemap _capturedCube;
        private MatchManager _matchManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }

            // 仅在存在墨水地面系统（即主对局/测试场景）时启动，避免在 UIDemo 等纯 UI 场景生效
            if (FindAnyObjectByType<InkSurfaceController>() == null)
            {
                return;
            }

            var go = new GameObject("ArenaReflectionCapture");
            go.AddComponent<ArenaReflectionCapture>();
        }

        private IEnumerator Start()
        {
            // 0.35s：让 Arena/材质完成 Awake，同时落在 PlayMode 预热期（编辑器 Profiler 预热 30 帧），
            // 避免一次性 6 面渲染污染战斗稳态采样（旧 1.0s 会正好撞上实测开始，拉低 1% Low）。
            yield return new WaitForSecondsRealtime(0.35f);
            Capture();

            // P31：订阅比赛阶段切换，进入结算时刷新一次反射（结算画面无战斗 spike 顾虑，
            // 结算时满场战况的墨面反射更准）。比赛中不重捕获。
            _matchManager = FindAnyObjectByType<MatchManager>();
            if (_matchManager != null)
            {
                _matchManager.PhaseChanged += OnPhaseChanged;
            }
        }

        private void OnPhaseChanged(MatchManager.Phase phase)
        {
            if (phase == MatchManager.Phase.Result)
            {
                Capture();
            }
        }

        private void OnDestroy()
        {
            if (_matchManager != null)
            {
                _matchManager.PhaseChanged -= OnPhaseChanged;
                _matchManager = null;
            }
            if (_capturedCube != null)
            {
                RenderEnvGlobals.ClearEnvironmentCube();
                Destroy(_capturedCube);
                _capturedCube = null;
            }
        }

        private void Capture()
        {
            GameObject tempCamGo = null;
            Cubemap cube = null;
            try
            {
                // 竞技场中心基于 ArenaLayoutV2（MapSizeX 40m, MapSizeZ 56m，点对称中心为原点 (0, 0, 0)），高度设在 y = 7m
                Vector3 center = new Vector3(0f, 7f, 0f);

                tempCamGo = new GameObject("Temp_ReflectionCaptureCamera");
                tempCamGo.transform.position = center;
                tempCamGo.transform.rotation = Quaternion.identity;

                Camera cam = tempCamGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.cullingMask = ~0;
                cam.allowHDR = true;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 160f;

                cube = new Cubemap(CubeResolution, TextureFormat.RGBAHalf, true);
                cube.name = "P1_ArenaEnv";

                bool ok = cam.RenderToCubemap(cube);
                cube.Apply(true, false);

                var style = RenderEnvGlobals.CurrentStyle;
                float probeStrength = style != null ? style.inkProbeStrength : 1f;

                if (ok)
                {
                    if (_capturedCube != null)
                    {
                        Destroy(_capturedCube);
                    }
                    _capturedCube = cube;
                    RenderEnvGlobals.SetEnvironmentCube(cube, probeStrength);
                    Debug.Log($"[ArenaReflectionCapture] 环境 Cubemap 捕获成功 ({CubeResolution}x{CubeResolution} RGBAHalf, mips={cube.mipmapCount}, strength={probeStrength})。");
                }
                else
                {
                    Debug.LogWarning("[ArenaReflectionCapture] cam.RenderToCubemap 返回失败，清除环境 Cubemap。");
                    RenderEnvGlobals.ClearEnvironmentCube();
                    if (cube != null)
                    {
                        Destroy(cube);
                        cube = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ArenaReflectionCapture] 捕获环境 Cubemap 异常: {ex.Message}");
                RenderEnvGlobals.ClearEnvironmentCube();
                if (cube != null)
                {
                    Destroy(cube);
                    cube = null;
                }
            }
            finally
            {
                if (tempCamGo != null)
                {
                    Destroy(tempCamGo);
                }
            }
        }
    }
}
