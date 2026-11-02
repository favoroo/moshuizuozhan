using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace P1.UI
{
    /// <summary>
    /// 更新说明 Markdown → TMP 富文本转换器（纯逻辑，EditMode 可测）。
    ///
    /// 背景：Release API（Gitee / GitHub）的 body 是 Markdown 原文，直接塞进 TextMeshProUGUI
    /// 会把 `##`、`**`、`-` 原样露出来。这里只做「弹窗里读得懂」的最小集：
    /// 标题、无序/有序列表、引用、分隔线、表格行、加粗/斜体/删除线/行内代码/链接。
    ///
    /// 约定：
    /// - 只生成 TMP 认识的标签（b/i/s/u/color/size/noparse），不引入第三方 Markdown 库；
    /// - 原文里的 `&lt;` 必须转义成 noparse，否则玩家/作者写的尖括号会被当成富文本标签吞掉；
    /// - 空行折叠为一个，避免 Release 里的多行空行把弹窗撑爆。
    /// </summary>
    public static class UpdateNotesMarkdown
    {
        private const float DefaultBodySize = 22f;
        private const string AngleToken = "\uE000";
        private const string CodeTokenOpen = "\uE100";
        private const string CodeTokenClose = "\uE101";
        private const string LinkColor = "#2456FF";
        private const string CodeColor = "#2456FF";
        private const string QuoteColor = "#4A4A4A";
        private const string RuleColor = "#B4B4B4";

        private static readonly Regex HtmlCommentRx = new Regex(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex HeadingRx = new Regex(@"^(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
        private static readonly Regex BulletRx = new Regex(@"^(\s*)[-*+]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex OrderedRx = new Regex(@"^(\s*)(\d{1,3})[.)、]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex QuoteRx = new Regex(@"^\s*>\s?(.*)$", RegexOptions.Compiled);
        private static readonly Regex RuleRx = new Regex(@"^[-*_](?:\s*[-*_]){2,}\s*$", RegexOptions.Compiled);
        private static readonly Regex ImageRx = new Regex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex LinkRx = new Regex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRx = new Regex(@"`([^`\n]+)`", RegexOptions.Compiled);
        private static readonly Regex BoldRx = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldUnderscoreRx = new Regex(@"__(.+?)__", RegexOptions.Compiled);
        private static readonly Regex StrikeRx = new Regex(@"~~(.+?)~~", RegexOptions.Compiled);
        private static readonly Regex ItalicRx = new Regex(@"(?<!\*)\*(?!\*)([^*\n]+?)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex ItalicUnderscoreRx = new Regex(@"(?<![\w])_([^_\n]+?)_(?![\w])", RegexOptions.Compiled);

        private enum BlockKind
        {
            None,
            Text,
            Heading,
            List,
            Quote,
            Code,
            Rule,
            Table
        }

        /// <summary>
        /// 把 Markdown 更新说明转成 TMP 富文本；空说明返回「暂无更新说明」。
        /// </summary>
        /// <param name="markdown">Release API 的 body 原文。</param>
        /// <param name="bodyFontSize">正文 TMP 字号；标题会在此基础上放大。</param>
        public static string ToTmp(string markdown, float bodyFontSize = DefaultBodySize)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "暂无更新说明";

            float body = bodyFontSize > 4f ? bodyFontSize : DefaultBodySize;
            string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
            normalized = HtmlCommentRx.Replace(normalized, "");

            var sb = new StringBuilder(normalized.Length + 256);
            BlockKind previous = BlockKind.None;
            bool inFence = false;

            foreach (string rawLine in normalized.Split('\n'))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.TrimStart();

                // 代码围栏：开/关行本身不显示，块内不做行内 Markdown 解析
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inFence = !inFence;
                    previous = BlockKind.None;
                    continue;
                }

                if (inFence)
                {
                    string codeLine = line.Length > 0 ? "  " + line : "";
                    AppendBlock(sb, "<color=#5A5A5A>" + RestoreAngleBrackets(EscapeAngleBrackets(codeLine)) + "</color>", BlockKind.Code, ref previous);
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    // 空行不立刻写：交给下一个块按需补一个空行，连续空行自动折叠
                    previous = BlockKind.None;
                    continue;
                }

                Match heading = HeadingRx.Match(trimmed);
                if (heading.Success)
                {
                    int level = heading.Groups[1].Length;
                    float bump = level <= 1 ? 12f : level <= 2 ? 8f : level == 3 ? 4f : 2f;
                    float size = body + bump;
                    string text = "<size=" + size.ToString("0") + "><b>" + FormatInline(heading.Groups[2].Value) + "</b></size>";
                    AppendBlock(sb, text, BlockKind.Heading, ref previous);
                    continue;
                }

                if (RuleRx.IsMatch(trimmed))
                {
                    AppendBlock(sb, "<color=" + RuleColor + ">················</color>", BlockKind.Rule, ref previous);
                    continue;
                }

                if (IsTableRow(trimmed))
                {
                    if (IsTableSeparator(trimmed)) continue;
                    AppendBlock(sb, FormatTableRow(trimmed), BlockKind.Table, ref previous);
                    continue;
                }

                Match quote = QuoteRx.Match(trimmed);
                if (quote.Success)
                {
                    string text = "<color=" + QuoteColor + ">" + FormatInline(quote.Groups[1].Value) + "</color>";
                    AppendBlock(sb, text, BlockKind.Quote, ref previous);
                    continue;
                }

                Match ordered = OrderedRx.Match(line);
                if (ordered.Success)
                {
                    int level = ordered.Groups[1].Value.Length / 2;
                    string indent = Indent(level);
                    string text = indent + "<b>" + ordered.Groups[2].Value + ".</b> " + FormatInline(ordered.Groups[3].Value);
                    AppendBlock(sb, text, BlockKind.List, ref previous);
                    continue;
                }

                Match bullet = BulletRx.Match(line);
                if (bullet.Success)
                {
                    int level = bullet.Groups[1].Value.Length / 2;
                    string text = Indent(level) + "• " + FormatInline(bullet.Groups[2].Value);
                    AppendBlock(sb, text, BlockKind.List, ref previous);
                    continue;
                }

                AppendBlock(sb, FormatInline(trimmed), BlockKind.Text, ref previous);
            }

            // 去掉末尾多余空行
            int end = sb.Length;
            while (end > 0 && sb[end - 1] == '\n') end--;
            if (end == 0) return "暂无更新说明";
            sb.Length = end;
            return sb.ToString();
        }

        private static void AppendBlock(StringBuilder sb, string body, BlockKind kind, ref BlockKind previous)
        {
            bool sameFlow = previous == kind && (kind == BlockKind.Text || kind == BlockKind.List
                || kind == BlockKind.Quote || kind == BlockKind.Code || kind == BlockKind.Table);

            if (sb.Length > 0 && !sameFlow) EnsureBlankLine(sb);
            sb.Append(body);
            sb.Append('\n');
            previous = kind;
        }

        private static void EnsureBlankLine(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (sb[sb.Length - 1] != '\n') sb.Append('\n');
            if (sb.Length < 2 || sb[sb.Length - 2] != '\n') sb.Append('\n');
        }

        private static string Indent(int level)
        {
            if (level <= 0) return "";
            if (level > 3) level = 3;
            return new string(' ', level * 4);
        }

        private static bool IsTableRow(string trimmed)
        {
            return trimmed.Length > 2 && trimmed[0] == '|' && trimmed[trimmed.Length - 1] == '|';
        }

        private static bool IsTableSeparator(string trimmed)
        {
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c != '|' && c != '-' && c != ':' && c != ' ' && c != '\t') return false;
            }
            return trimmed.Contains("---") || trimmed.Contains("--");
        }

        private static string FormatTableRow(string trimmed)
        {
            string inner = trimmed.Substring(1, trimmed.Length - 2);
            string[] cells = inner.Split('|');
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append("  |  ");
                sb.Append(FormatInline(cells[i].Trim()));
            }
            return sb.ToString();
        }

        private static string EscapeAngleBrackets(string text)
        {
            return text.Replace("<", AngleToken);
        }

        private static string RestoreAngleBrackets(string text)
        {
            return text.Replace(AngleToken, "<noparse><</noparse>");
        }

        /// <summary>行内 Markdown → TMP：先保护代码段与尖括号，再套标签。</summary>
        private static string FormatInline(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string s = EscapeAngleBrackets(text);

            // 行内代码先抽成占位符，避免里面的 * / _ 被当成强调标记
            var codes = new List<string>();
            s = InlineCodeRx.Replace(s, m =>
            {
                codes.Add(m.Groups[1].Value);
                return CodeTokenOpen + (codes.Count - 1) + CodeTokenClose;
            });

            s = ImageRx.Replace(s, m =>
            {
                string alt = m.Groups[1].Value.Trim();
                return "<color=" + QuoteColor + ">[图] " + alt + "</color>";
            });
            s = LinkRx.Replace(s, "<color=" + LinkColor + "><u>$1</u></color>");
            s = BoldRx.Replace(s, "<b>$1</b>");
            s = BoldUnderscoreRx.Replace(s, "<b>$1</b>");
            s = StrikeRx.Replace(s, "<s>$1</s>");
            s = ItalicRx.Replace(s, "<i>$1</i>");
            s = ItalicUnderscoreRx.Replace(s, "<i>$1</i>");

            for (int i = 0; i < codes.Count; i++)
            {
                s = s.Replace(CodeTokenOpen + i + CodeTokenClose, "<color=" + CodeColor + ">" + codes[i] + "</color>");
            }

            return RestoreAngleBrackets(s);
        }
    }
}