using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// MiSans 预烘字集账本的唯一入口：读账本 + 现场重扫源码字面量。
    ///
    /// 账本由 `python3 tools/font_charset.py` 生成（三份，都在 tools/）：
    ///   font_charset.txt       正文账本：源码字符串字面量 ∪ ASCII ∪ 项目符号，且 MiSans 都有字形
    ///   font_baseline.txt      基线账本：只有 ASCII + 项目符号（标题字重按 96pt 采样，预烘 1200+ 字太贵）
    ///   font_exempt_chars.txt  豁免账：源码里出现但 MiSans 渲染不了的字（只许进注释与编辑器日志）
    ///
    /// **扫描口径必须与 tools/font_charset.py 一致，改一边要改两边**：
    /// 只看 `Assets/_Project/Scripts/**/*.cs` 的字符串/字符字面量（跳注释），插值表达式 `{...}` 整体跳过。
    /// 口径一旦漂移，字集账本会漏字 → 真机上某个字突然走系统回退字体（整层 UI 当帧重排）。
    /// </summary>
    public static class FontCharsetLedger
    {
        public const string CharsetPath = "tools/font_charset.txt";
        public const string BaselinePath = "tools/font_baseline.txt";
        public const string ExemptPath = "tools/font_exempt_chars.txt";

        /// <summary>扫描根目录（相对工程根）。</summary>
        public const string ScanRoot = "Assets/_Project/Scripts";

        /// <summary>豁免字检查只覆盖运行时文案：Editor/ 下的字上报 Unity Console，与 TMP 字体无关。</summary>
        public const string EditorFolderSegment = "Editor";

        public static string LoadCharset() => LoadLedger(CharsetPath);
        public static string LoadBaseline() => LoadLedger(BaselinePath);
        public static string LoadExempt() => LoadLedger(ExemptPath);

        public static string LoadLedger(string relativePath)
        {
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
            if (!File.Exists(abs)) return string.Empty;

            var sb = new StringBuilder();
            foreach (char ch in File.ReadAllText(abs))
            {
                if (IsRenderable(ch)) sb.Append(ch);
            }

            return sb.ToString();
        }

        /// <summary>账本文件是否存在（缺失时上层应提示先跑 tools/font_charset.py）。</summary>
        public static bool LedgersExist()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return File.Exists(Path.Combine(root, CharsetPath))
                && File.Exists(Path.Combine(root, BaselinePath));
        }

        /// <summary>控制字符不上屏，也不进账本（`\n` `\t` 之类会被扫到）。</summary>
        public static bool IsRenderable(char ch)
        {
            if (ch < (char)0x20 || ch == (char)0x7F) return false;
            if (ch >= (char)0x80 && ch <= (char)0x9F) return false;
            return ch != '\u200B' && ch != '\uFEFF';
        }

        public static HashSet<char> ToSet(string ledger)
        {
            var set = new HashSet<char>();
            if (ledger == null) return set;
            foreach (char ch in ledger) set.Add(ch);
            return set;
        }

        /// <summary>
        /// 现场重扫源码字面量。`runtimeOnly` = 只看运行时程序集（运行时程序集 = 扫描根下不在 Editor/ 里的 .cs）。
        /// </summary>
        public static HashSet<char> ScanLiteralChars(bool runtimeOnly, out int fileCount)
        {
            var result = new HashSet<char>();
            fileCount = 0;

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ScanRoot));
            if (!Directory.Exists(root)) return result;

            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (runtimeOnly && IsUnderEditorFolder(file)) continue;
                fileCount++;

                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                ExtractLiterals(text, result);
            }

            return result;
        }

        static bool IsUnderEditorFolder(string absolutePath)
        {
            string normalized = absolutePath.Replace('\\', '/');
            int index = normalized.LastIndexOf("/" + EditorFolderSegment + "/", System.StringComparison.Ordinal);
            return index >= 0;
        }

        /// <summary>把源码里的字符串/字符字面量收集进 <paramref name="into"/>（跳过注释与插值表达式）。</summary>
        public static void ExtractLiterals(string text, HashSet<char> into)
        {
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];

                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    int j = text.IndexOf('\n', i);
                    i = j < 0 ? n : j;
                    continue;
                }

                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int j = text.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    i = j < 0 ? n : j + 2;
                    continue;
                }

                // 前缀（@ / $@ / @$ / $）统一把 i 落到开引号上。
                bool verbatim = false, interpolated = false;
                if (c == '@' && i + 1 < n && text[i + 1] == '"')
                {
                    verbatim = true;
                    i += 1;
                }
                else if (c == '$' && i + 2 < n && text[i + 1] == '@' && text[i + 2] == '"')
                {
                    verbatim = true;
                    interpolated = true;
                    i += 2;
                }
                else if (c == '@' && i + 2 < n && text[i + 1] == '$' && text[i + 2] == '"')
                {
                    verbatim = true;
                    interpolated = true;
                    i += 2;
                }
                else if (c == '$' && i + 1 < n && text[i + 1] == '"')
                {
                    interpolated = true;
                    i += 1;
                }
                else if (c != '"')
                {
                    if (c == '\'')
                    {
                        int j = i + 1;
                        var buf = new StringBuilder();
                        while (j < n)
                        {
                            if (text[j] == '\\') { j += 2; continue; }
                            if (text[j] == '\'') break;
                            buf.Append(text[j]);
                            j++;
                        }

                        if (j < n)
                        {
                            foreach (char ch in buf.ToString())
                                if (IsRenderable(ch)) into.Add(ch);
                            i = j + 1;
                            continue;
                        }
                    }

                    i++;
                    continue;
                }

                i++; // 跳过开引号

                int depth = 0;
                while (i < n)
                {
                    char ch = text[i];

                    if (depth > 0)
                    {
                        if (ch == '"') // 插值表达式内的嵌套字符串：整体跳过
                        {
                            i++;
                            while (i < n)
                            {
                                if (text[i] == '\\') { i += 2; continue; }
                                if (text[i] == '"') break;
                                i++;
                            }

                            i++;
                            continue;
                        }

                        if (ch == '{') depth++;
                        else if (ch == '}') depth--;
                        i++;
                        continue;
                    }

                    if (!verbatim && ch == '\\')
                    {
                        if (i + 1 < n && IsRenderable(text[i + 1])) into.Add(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (ch == '"')
                    {
                        if (verbatim && i + 1 < n && text[i + 1] == '"')
                        {
                            if (IsRenderable('"')) into.Add('"');
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    if (ch == '{' && interpolated)
                    {
                        depth = 1;
                        i++;
                        continue;
                    }

                    if (IsRenderable(ch)) into.Add(ch);
                    i++;
                }

                i++;
            }
        }

        /// <summary>
        /// 三段账验收结果（菜单自检与 EditMode 测试共用，避免两边口径漂移）。
        /// 任何一段非空都是问题；<see cref="Ok"/> 为 true 才算过。
        /// </summary>
        public sealed class Report
        {
            public int CharsetCount;
            public int BaselineCount;
            public int ExemptCount;
            public int ScanFileCount;
            public int ScannedCharCount;

            /// <summary>字集账本里的字没被 body 资产预烘（新文案加了字但没重跑账本生成）。</summary>
            public string CharsetNotPrebaked = string.Empty;

            /// <summary>基线账本里的字没被 display 资产预烘。</summary>
            public string BaselineNotPrebaked = string.Empty;

            /// <summary>源码现场扫出的、既不在字集账本也不在豁免账里的字（账本漏账）。</summary>
            public string SourceDrift = string.Empty;

            /// <summary>豁免字（MiSans 无字形）出现在运行时文案里 —— 真机会走系统回退字体并整层重排。</summary>
            public string ExemptOnScreen = string.Empty;

            /// <summary>同一个字既在字集账本又在豁免账（账本自相矛盾）。</summary>
            public string ExemptOverlap = string.Empty;

            /// <summary>连 MiSans 源字体 cmap 都没有的字（文案里混进了字体本身不支持的字）。</summary>
            public string UnsupportedByFont = string.Empty;

            public bool Ok =>
                CharsetNotPrebaked.Length == 0 &&
                BaselineNotPrebaked.Length == 0 &&
                SourceDrift.Length == 0 &&
                ExemptOnScreen.Length == 0 &&
                ExemptOverlap.Length == 0 &&
                UnsupportedByFont.Length == 0;

            public string Format()
            {
                var sb = new StringBuilder();
                sb.AppendFormat("字集 {0} 字 / 基线 {1} 字 / 豁免 {2} 字；现场扫描 {3} 个 .cs、{4} 个字",
                    CharsetCount, BaselineCount, ExemptCount, ScanFileCount, ScannedCharCount);

                if (CharsetNotPrebaked.Length > 0)
                    sb.AppendFormat("\n  × 未预烘进正文图集 {0} 字：{1}", CharsetNotPrebaked.Length, Head(CharsetNotPrebaked));
                if (BaselineNotPrebaked.Length > 0)
                    sb.AppendFormat("\n  × 未预烘进标题图集 {0} 字：{1}", BaselineNotPrebaked.Length, Head(BaselineNotPrebaked));
                if (SourceDrift.Length > 0)
                    sb.AppendFormat("\n  × 账本漏账 {0} 字：{1}（跑 python3 tools/font_charset.py）",
                        SourceDrift.Length, Head(SourceDrift));
                if (ExemptOnScreen.Length > 0)
                    sb.AppendFormat("\n  × 豁免字上屏 {0} 字：{1}（换 MiSans 有的符号，别靠系统回退字体）",
                        ExemptOnScreen.Length, Head(ExemptOnScreen));
                if (ExemptOverlap.Length > 0)
                    sb.AppendFormat("\n  × 账本交叠 {0} 字：{1}", ExemptOverlap.Length, Head(ExemptOverlap));
                if (UnsupportedByFont.Length > 0)
                    sb.AppendFormat("\n  × MiSans 源字体缺字：{0}", Head(UnsupportedByFont));

                return sb.ToString();
            }

            static string Head(string s) => s.Length <= 80 ? s : s.Substring(0, 80) + "…";
        }

        /// <summary>
        /// 三段账验收：
        ///   A 字集账本 ⊆ body 资产已预烘字形；基线账本 ⊆ display 资产已预烘字形；
        ///   B 源码现场字面量 ⊆ 字集账本 ∪ 豁免账（防漂移的牙齿）；
        ///   C 字集账本 ∩ 豁免账 = ∅，且豁免字不得出现在运行时文案里。
        /// </summary>
        public static Report Analyze(TMPro.TMP_FontAsset body, TMPro.TMP_FontAsset display,
            Font bodySourceFont = null, Font displaySourceFont = null)
        {
            var report = new Report();

            string charset = LoadCharset();
            string baseline = LoadBaseline();
            string exempt = LoadExempt();

            var charsetSet = ToSet(charset);
            var baselineSet = ToSet(baseline);
            var exemptSet = ToSet(exempt);

            report.CharsetCount = charsetSet.Count;
            report.BaselineCount = baselineSet.Count;
            report.ExemptCount = exemptSet.Count;

            var prebakedBody = PrebakedChars(body);
            var prebakedDisplay = PrebakedChars(display);

            report.CharsetNotPrebaked = Missing(charsetSet, prebakedBody);
            report.BaselineNotPrebaked = Missing(baselineSet, prebakedDisplay);

            var scanned = ScanLiteralChars(false, out int files);
            report.ScanFileCount = files;
            report.ScannedCharCount = scanned.Count;

            var allowed = new HashSet<char>(charsetSet);
            foreach (char ch in exemptSet) allowed.Add(ch);
            report.SourceDrift = Missing(scanned, allowed);

            var runtimeScanned = ScanLiteralChars(true, out _);
            report.ExemptOnScreen = Intersect(exemptSet, runtimeScanned);

            var overlap = new HashSet<char>(charsetSet);
            overlap.IntersectWith(exemptSet);
            report.ExemptOverlap = Sorted(overlap);

            if (bodySourceFont != null || displaySourceFont != null)
            {
                var unsupported = new HashSet<char>();
                foreach (char ch in charsetSet)
                {
                    bool inBody = bodySourceFont != null && bodySourceFont.HasCharacter(ch);
                    bool inDisplay = displaySourceFont != null && displaySourceFont.HasCharacter(ch);
                    if (!inBody && !inDisplay) unsupported.Add(ch);
                }

                report.UnsupportedByFont = Sorted(unsupported);
            }

            return report;
        }

        static HashSet<char> PrebakedChars(TMPro.TMP_FontAsset fontAsset)
        {
            var set = new HashSet<char>();
            if (fontAsset == null || fontAsset.characterTable == null) return set;
            foreach (var character in fontAsset.characterTable)
                if (character != null) set.Add((char)character.unicode);
            return set;
        }

        static string Missing(HashSet<char> wanted, HashSet<char> have)
        {
            var missing = new HashSet<char>();
            foreach (char ch in wanted)
                if (!have.Contains(ch)) missing.Add(ch);
            return Sorted(missing);
        }

        static string Intersect(HashSet<char> a, HashSet<char> b)
        {
            var hit = new HashSet<char>(a);
            hit.IntersectWith(b);
            return Sorted(hit);
        }

        static string Sorted(HashSet<char> set)
        {
            var list = new List<char>(set);
            list.Sort();
            return new string(list.ToArray());
        }
    }
}
