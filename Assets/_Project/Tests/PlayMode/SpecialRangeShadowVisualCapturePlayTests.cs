using System.Collections;
using NUnit.Framework;
using P1.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [大招预警贴地] 视觉取证 + 像素断言：专用正交相机俯拍「平地 + 1.5m 箱体」，
    /// 对比显示 / 隐藏 <see cref="SpecialRangeShadow"/> 的两帧 —— 范围盘必须同时盖住地面与箱顶
    /// （阴影状顺着高差铺开），且不越过伤害半径。产物 Logs/special_range_shadow_{on,off}.png。
    /// 用 RenderTexture + ReadPixels 同步取证，不用 ScreenCapture（会串帧且含主菜单 UI）。
    /// </summary>
    [Explicit("视觉取证用，手动在 Test Runner 里单跑；会写 Logs/ 并渲染 RenderTexture。")]
    public class SpecialRangeShadowVisualCapturePlayTests
    {
        private const int Width = 640;
        private const int Height = 480;

        [UnityTest]
        public IEnumerator Capture_DrapedRange_CoversFloorAndBoxTop()
        {
            SceneManager.LoadScene("PhaseA_Playbox");
            yield return null;
            yield return null;

            // 停掉玩家与 Bot，避免他们走进取样区/干扰渲染
            P1.Player.PlayerController player = P1.Player.PlayerController.LocalPlayer;
            if (player != null)
                player.enabled = false;
            var bots = Object.FindObjectsByType<P1.AI.BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
                bots[i].enabled = false;

            Vector3 center = new Vector3(500f, 0f, 500f);
            var root = new GameObject("RangeShadowCaptureRoot");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.SetParent(root.transform, false);
            ground.transform.position = center + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(root.transform, false);
            box.transform.position = center + new Vector3(0f, 0.75f, 0f);
            box.transform.localScale = new Vector3(3f, 1.5f, 3f);

            var surfaceMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            surfaceMat.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.72f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = surfaceMat;
            box.GetComponent<MeshRenderer>().sharedMaterial = surfaceMat;

            Physics.SyncTransforms();

            var camGo = new GameObject("RangeShadowCaptureCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.position = center + Vector3.up * 30f;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var shadow = new SpecialRangeShadow(null, "VisualCapture");
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            try
            {
                shadow.Show(center, 4f, 0.6f, Color.magenta);
                yield return new WaitForEndOfFrame();
                Color32[] on = RenderPixels(cam, rt, "Logs/special_range_shadow_on.png");

                shadow.Hide();
                yield return null;
                Color32[] off = RenderPixels(cam, rt, "Logs/special_range_shadow_off.png");

                int changed = 0;
                for (int i = 0; i < on.Length; i++)
                {
                    if (PixelDiff(on[i], off[i]) > 10)
                        changed++;
                }
                Assert.Greater(changed, 5000,
                    $"显示预警圈时应有大片像素被染色，实际只变了 {changed} 个像素（网格没渲染出来？）");

                int boxTop = SampleDiff(on, off, cam, center + new Vector3(0f, 1.5f, 0f));
                Assert.Greater(boxTop, 30,
                    $"范围盘必须盖到 1.5m 箱顶（贴地投影），实际像素差仅 {boxTop}");

                int floor = SampleDiff(on, off, cam, center + new Vector3(0f, 0f, 2.8f));
                Assert.Greater(floor, 10,
                    $"范围盘必须盖到箱外地面，实际像素差仅 {floor}");

                int outside = SampleDiff(on, off, cam, center + new Vector3(0f, 0f, 6.5f));
                Assert.Less(outside, 6,
                    $"4m 伤害半径之外不应被染色，实际像素差 {outside}（范围读法必须准确）");
            }
            finally
            {
                shadow.Dispose();
                Object.Destroy(root);
                Object.Destroy(camGo);
                Object.Destroy(surfaceMat);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        private static Color32[] RenderPixels(Camera cam, RenderTexture rt, string fileName)
        {
            RenderTexture prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            System.IO.File.WriteAllBytes(fileName, tex.EncodeToPNG());
            Color32[] pixels = tex.GetPixels32();
            Object.Destroy(tex);
            return pixels;
        }

        private static int SampleDiff(Color32[] on, Color32[] off, Camera cam, Vector3 world)
        {
            Vector3 viewport = cam.WorldToViewportPoint(world);
            int x = Mathf.Clamp(Mathf.RoundToInt(viewport.x * (Width - 1)), 0, Width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(viewport.y * (Height - 1)), 0, Height - 1);
            int index = y * Width + x;
            return PixelDiff(on[index], off[index]);
        }

        private static int PixelDiff(Color32 a, Color32 b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }
    }
}