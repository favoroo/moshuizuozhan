using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// P6-A 打击反馈的纯数学部分（无 GameObject 依赖，EditMode 可直接断言）：
    /// 连击颜色加温、打击标记尺寸、受击方向角与连击音高。
    /// </summary>
    public static class HitFeedbackMath
    {
        /// <summary>
        /// 连击颜色：0 连击纯白；连击越高越向队伍主色加温（对齐 Inkwave hud.css 的 --hn × 11%）。
        /// </summary>
        public static Color ComboTint(Color baseColor, Color teamColor, int combo)
        {
            float warmth = Mathf.Clamp01(Mathf.Max(0, combo) * 0.11f);
            Color tinted = Color.Lerp(baseColor, teamColor, warmth);
            tinted.a = baseColor.a;
            return tinted;
        }

        /// <summary>
        /// 打击标记尺寸：伤害越高越大（0.8~1.6），连击每级再放大 6%（与 Inkwave 一致）。
        /// </summary>
        public static float MarkerScale(float damage, int combo)
        {
            float scale = Mathf.Clamp(0.8f + Mathf.Max(0f, damage) / 90f, 0.8f, 1.6f);
            return scale * (1f + Mathf.Clamp(combo, 0, 6) * 0.06f);
        }

        /// <summary>
        /// 受击方向角（度）：0 = 画面上方（正前方），+90 = 画面右侧。
        /// 传入相机水平前向/右向，保证俯视相机旋转后指示弧仍指向真实伤害来源。
        /// </summary>
        public static float SourceAngleDegrees(Vector3 toSource, Vector3 cameraForwardFlat, Vector3 cameraRightFlat)
        {
            Vector2 forward = new Vector2(cameraForwardFlat.x, cameraForwardFlat.z);
            Vector2 right = new Vector2(cameraRightFlat.x, cameraRightFlat.z);

            if (forward.sqrMagnitude < 0.0001f) forward = Vector2.up;
            if (right.sqrMagnitude < 0.0001f) right = Vector2.right;

            Vector2 source = new Vector2(toSource.x, toSource.z);
            return Mathf.Atan2(Vector2.Dot(source, right), Vector2.Dot(source, forward)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 连击音高：从 1.0 每级 +5%（上限 6 连击 ≈ 1.30），击杀再额外 +6%，读作「越打越顺」。
        /// </summary>
        public static float ComboPitch(int combo, bool killed)
        {
            float pitch = 1f + Mathf.Clamp(combo, 0, 6) * 0.05f;
            if (killed) pitch += 0.06f;
            return pitch;
        }
    }
}
