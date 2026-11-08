using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P1.Tests
{
    /// <summary>
    /// [P32 敌方墨覆盖] 视觉取证：用一台专用相机（渲染到 RenderTexture，不含任何 UI）拍角色正面，
    /// 把覆盖量推到 0 / 0.25 / 0.5 / 0.75 / 1.0 各拍一张，供人工核对墨斑形态与生长曲线。
    /// 手动单跑；产物 Logs/p32_inkcoat_*.png。
    ///
    /// 为什么不用 ScreenCapture：它抓的是整屏（含常驻主菜单 UI），而且连续几帧会串帧
    /// （本项目已踩过，见 MatchCalloutVisualCapturePlayTests 注释）。RenderTexture + ReadPixels 是同步的。
    /// </summary>
    [Explicit("视觉取证用，手动在 Test Runner 里单跑；不进常规套件（会写 Logs/ 且耗时）。")]
    public class InkCoatVisualCapturePlayTests
    {
        private const string SceneName = "PhaseA_Playbox";

        private IEnumerator LoadPlaybox()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;

            PlayerController player = PlayerController.LocalPlayer;
            Assert.IsNotNull(player);
            Assert.IsNotNull(InkSurfaceController.Instance);
            player.enabled = false;

            AI.BotController[] bots = Object.FindObjectsByType<AI.BotController>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].enabled = false;
                InkCharacterMotor m = bots[i].GetComponent<InkCharacterMotor>();
                if (m != null) m.enabled = false;
                Weapons.WeaponController w = bots[i].GetComponent<Weapons.WeaponController>();
                if (w != null) w.enabled = false;
            }

            yield return null;
        }

        /// <summary>直接写覆盖量字段并重写 MPB（跳过累积/淡出逻辑，固定取证档位）。</summary>
        private static void ForceCoat(CombatantHealth health, float amount, Team inkTeam)
        {
            var t = typeof(CombatantHealth);
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            t.GetField("_inkCoverageFlash", F).SetValue(health, amount);
            t.GetField("_inkCoatAmount", F).SetValue(health, amount);
            t.GetField("_inkCoatColorTeam", F).SetValue(health, inkTeam);
            t.GetField("_inkCoatColor", F).SetValue(
                health, InkSurfaceController.Instance.GetTeamColor(inkTeam));
            t.GetMethod("WriteBodyBlock", F).Invoke(health, null);
        }

        private static IEnumerator CaptureTo(RenderTexture rt, Camera cam, string fileName)
        {
            yield return new WaitForEndOfFrame();

            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            System.IO.File.WriteAllBytes("Logs/" + fileName, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        [UnityTest]
        public IEnumerator Capture_InkCoatLadder()
        {
            yield return LoadPlaybox();

            PlayerController player = PlayerController.LocalPlayer;
            CombatantHealth health = player.Health;

            // 角色视觉根（KidRoot 上一级），用它算包围盒决定取景
            CharacterVisualController visual = player.GetComponentInChildren<CharacterVisualController>();
            Assert.IsNotNull(visual, "玩家应有 CharacterVisualController");

            // 专用相机：纯色底、无 UI、正对角色
            var camGo = new GameObject("P32CaptureCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.cullingMask = ~0;

            var rt = new RenderTexture(720, 960, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            Transform kid = visual.KidRoot.transform;
            float[] amounts = { 0f, 0.25f, 0.5f, 0.75f, 1f };

            for (int i = 0; i < amounts.Length; i++)
            {
                ForceCoat(health, amounts[i], Team.TeamB);

                // 相机放在角色正前方（角色本地 +Z），对准胸口
                Vector3 chest = kid.position + Vector3.up * 0.72f;
                Vector3 fwd = kid.forward;
                cam.transform.position = chest + fwd * 2.6f + Vector3.up * 0.05f;
                cam.transform.LookAt(chest);

                yield return null;
                yield return null;

                string name = $"p32_inkcoat_{i}_{Mathf.RoundToInt(amounts[i] * 100f):000}.png";
                yield return CaptureTo(rt, cam, name);
                Debug.Log($"[P32] 已输出 {name}（覆盖量 {amounts[i]:F2}）");
            }

            ForceCoat(health, 0f, Team.TeamB);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(camGo);
        }
    }
}
