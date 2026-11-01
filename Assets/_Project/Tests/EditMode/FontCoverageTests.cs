using NUnit.Framework;
using P1.Editor;
using P1.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// MiSans 字体账本验收（对齐 godot/t-1 的 `tests/font_coverage_check.gd` 三段账）：
    ///   A 字集账本 ⊆ 正文资产已预烘字形；基线账本 ⊆ 标题资产已预烘字形；
    ///   B 源码现场字面量 ⊆ 字集账本 ∪ 豁免账（防漂移的牙齿）；
    ///   C 字集账本 ∩ 豁免账 = ∅，且豁免字不得进运行时文案。
    ///
    /// 为什么要立这条判据：预烘字集省下的显存是拿「字形覆盖面」换的 —— 覆盖面一出事，
    /// 编辑器里看不出来（macOS 拿系统回退字体兜着），只有 Android 真机上某一句才突然换字形，
    /// 而且系统回退字体的动态加载会让整层 UI 当帧重排（画面集体闪一下）。不崩不报错，只能靠静态账本钉住。
    ///
    /// 扫描口径必须与 `tools/font_charset.py` 一致（两者都只看 C# 字符串字面量、跳过注释）；
    /// 账本本身由 `python3 tools/font_charset.py` 生成，源码改动后要重跑。
    /// </summary>
    public class FontCoverageTests
    {
        const string BodyAssetPath = "Assets/_Project/Art/Fonts/TMP/MiSans-Semibold SDF.asset";
        const string DisplayAssetPath = "Assets/_Project/Art/Fonts/TMP/MiSans-Heavy SDF.asset";
        const string ThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";

        /// <summary>账本规模下限：文件被清空/截断时 A 段会「零个字全部通过」，这条是那口牙齿。</summary>
        const int CharsetMin = 800;

        /// <summary>子集化的收益全靠字形数掉下来兑现；上限防「有人把整份 MiSans 烘回去」的静默回归。</summary>
        const int GlyphCeiling = 8000;

        /// <summary>豁免账是**要人拍板**的例外清单，涨到几十条说明该换符号而不是继续加例外。</summary>
        const int ExemptCeiling = 32;

        /// <summary>源码扫描至少要捞到这么多文件，否则目录遍历坏了，B 段同样是假绿。</summary>
        const int ScanMinFiles = 50;

        static TMP_FontAsset LoadBody() => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyAssetPath);
        static TMP_FontAsset LoadDisplay() => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DisplayAssetPath);
        static UIThemeSO LoadTheme() => AssetDatabase.LoadAssetAtPath<UIThemeSO>(ThemePath);

        [Test]
        public void Ledgers_Exist_AndAreSane()
        {
            Assert.IsTrue(FontCharsetLedger.LedgersExist(), "缺字集账本：先跑 python3 tools/font_charset.py");

            int charset = FontCharsetLedger.LoadCharset().Length;
            int baseline = FontCharsetLedger.LoadBaseline().Length;
            int exempt = FontCharsetLedger.LoadExempt().Length;

            Assert.GreaterOrEqual(charset, CharsetMin, "字集账本被截断/清空了");
            Assert.LessOrEqual(charset, GlyphCeiling, "字集账本规模失控（不要预烘整份字库）");
            Assert.Greater(baseline, 0, "基线账本为空");
            Assert.LessOrEqual(baseline, charset, "基线账本应小于等于全字集");
            Assert.LessOrEqual(exempt, ExemptCeiling, "豁免账过长：该换符号，而不是继续加例外");
        }

        [Test]
        public void FontAssets_UseTieredSampling()
        {
            var body = LoadBody();
            var display = LoadDisplay();
            Assert.IsNotNull(body, "缺正文字体资产：跑 P1/画风/漫画风/0");
            Assert.IsNotNull(display, "缺标题字体资产：跑 P1/画风/漫画风/0");

            Assert.AreEqual(64, body.faceInfo.pointSize, "正文字重应按 64pt 采样");
            Assert.AreEqual(96, display.faceInfo.pointSize, "标题字重应按 96pt 采样");
            Assert.Greater(display.faceInfo.pointSize, body.faceInfo.pointSize);
        }

        [Test]
        public void Charset_IsPrebaked_IntoBodyAsset()
        {
            var body = LoadBody();
            Assert.IsNotNull(body);

            var charset = FontCharsetLedger.ToSet(FontCharsetLedger.LoadCharset());
            Assert.GreaterOrEqual(charset.Count, CharsetMin);

            var missing = new System.Text.StringBuilder();
            var prebaked = new System.Collections.Generic.HashSet<char>();
            foreach (var character in body.characterTable) prebaked.Add((char)character.unicode);

            foreach (char ch in charset)
                if (!prebaked.Contains(ch) && missing.Length < 100) missing.Append(ch);

            Assert.IsEmpty(missing.ToString(),
                "字集账本里有字没被正文资产预烘（重跑 P1/画风/漫画风/0 或 tools/font_charset.py）: " + missing);
            Assert.LessOrEqual(prebaked.Count, GlyphCeiling, "正文字体图集烘进了过多字形");
        }

        [Test]
        public void Baseline_IsPrebaked_IntoDisplayAsset()
        {
            var display = LoadDisplay();
            Assert.IsNotNull(display);

            var baseline = FontCharsetLedger.ToSet(FontCharsetLedger.LoadBaseline());
            var prebaked = new System.Collections.Generic.HashSet<char>();
            foreach (var character in display.characterTable) prebaked.Add((char)character.unicode);

            var missing = new System.Text.StringBuilder();
            foreach (char ch in baseline)
                if (!prebaked.Contains(ch) && missing.Length < 100) missing.Append(ch);

            Assert.IsEmpty(missing.ToString(),
                "基线账本里有字没被标题资产预烘（重跑 P1/画风/漫画风/0）: " + missing);
            Assert.LessOrEqual(prebaked.Count, GlyphCeiling, "标题字体图集烘进了过多字形");
        }

        [Test]
        public void SourceLiterals_AreCovered_ByLedgers()
        {
            var charset = FontCharsetLedger.ToSet(FontCharsetLedger.LoadCharset());
            var exempt = FontCharsetLedger.ToSet(FontCharsetLedger.LoadExempt());

            var scanned = FontCharsetLedger.ScanLiteralChars(runtimeOnly: false, out int files);
            Assert.GreaterOrEqual(files, ScanMinFiles, "源码扫描文件数异常，目录遍历可能坏了");

            var drift = new System.Text.StringBuilder();
            foreach (char ch in scanned)
                if (!charset.Contains(ch) && !exempt.Contains(ch) && drift.Length < 100) drift.Append(ch);

            Assert.IsEmpty(drift.ToString(),
                "源码里有账本没收录的字（跑 python3 tools/font_charset.py 并提交账本）: " + drift);
        }

        [Test]
        public void ExemptChars_DoNotReachScreen()
        {
            var exempt = FontCharsetLedger.ToSet(FontCharsetLedger.LoadExempt());
            var runtime = FontCharsetLedger.ScanLiteralChars(runtimeOnly: true, out _);

            var onScreen = new System.Text.StringBuilder();
            foreach (char ch in exempt)
                if (runtime.Contains(ch)) onScreen.Append(ch);

            Assert.IsEmpty(onScreen.ToString(),
                "豁免字（MiSans 无字形）出现在运行时文案里：换 MiSans 有的符号，别靠系统回退字体: " + onScreen);
        }

        [Test]
        public void CharsetLedger_MatchesMiSansGlyphCoverage()
        {
            // 尺子对照：账本必须"有的进、没有的不进"。MiSans 全字库没有这些 emoji/装饰字形，
            // 账本里出现它们就说明生成口径坏了（比如把系统回退字体当成了覆盖）。
            //
            // 注意不能用 UnityEngine.Font.HasCharacter 当尺子：动态字体对系统能兜住的字符也返回 true，
            // 正是「假覆盖面」的来源（见 FontCharsetLedger 的注释）。
            var charset = FontCharsetLedger.ToSet(FontCharsetLedger.LoadCharset());

            foreach (char ch in "⚙▶◀✦♥♪")
            {
                Assert.IsFalse(charset.Contains(ch),
                    string.Format("字集账本混进了 MiSans 没有的字形 U+{0:X4}（{1}）", (int)ch, ch));
            }

            // 反例：常用汉字必须进得了账本（账本被清空/口径走偏时这条会红）。
            foreach (char ch in "涂地大招比分墨")
            {
                Assert.IsTrue(charset.Contains(ch), "字集账本缺常用字: " + ch);
            }
        }

        [Test]
        public void Theme_UsesSingleMiSansFamily_AndLightBold()
        {
            var theme = LoadTheme();
            var body = LoadBody();
            var display = LoadDisplay();
            Assert.IsNotNull(theme, "缺 UITheme.asset");
            Assert.IsNotNull(body);
            Assert.IsNotNull(display);

            Assert.AreSame(display, theme.displayFont, "标题字体应指向 MiSans-Heavy");
            Assert.AreSame(body, theme.bodyFont, "正文字体应指向 MiSans-Semibold");
            Assert.AreSame(display, theme.displayCjkFont, "标题中文字体应指向 MiSans-Heavy");
            Assert.AreSame(body, theme.bodyCjkFont, "正文中文字体应指向 MiSans-Semibold");

            // 伪粗体收口：Bold 只做"轻加粗"，标题字重不叠加膨胀（0.75 的默认值会让密笔汉字糊成黑块）。
            Assert.AreEqual(0.25f, body.boldStyle, 0.001f, "正文 boldStyle 应为 0.25");
            Assert.AreEqual(0f, display.boldStyle, 0.001f, "标题 boldStyle 应为 0");
            Assert.IsNotNull(body.material);
            Assert.AreEqual(0.25f, body.material.GetFloat(ShaderUtilities.ID_WeightBold), 0.001f,
                "正文字体材质 _WeightBold 应为 0.25（运行时用的是材质值）");
            Assert.AreEqual(0f, display.material.GetFloat(ShaderUtilities.ID_WeightBold), 0.001f,
                "标题字体材质 _WeightBold 应为 0");
        }

        [Test]
        public void TmpSettings_FallbackOnlyContainsMiSans()
        {
            var fallbacks = TMP_Settings.fallbackFontAssets;
            Assert.IsNotNull(fallbacks, "TMP Settings 缺全局 fallback（跑 P1/画风/漫画风/0）");
            Assert.IsNotEmpty(fallbacks);

            foreach (var font in fallbacks)
            {
                Assert.IsNotNull(font);
                StringAssert.Contains("MiSans", font.name, "全局 fallback 里混进了旧字体: " + font.name);
            }
        }
    }
}