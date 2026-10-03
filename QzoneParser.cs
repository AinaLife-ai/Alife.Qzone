using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AinaLife.Qzone;

/// <summary>QQ空间响应解析器（完整移植自 KiraAI_qzone_plugin）</summary>
public static class QzoneParser
{
    /// <summary>下载图片时的 SSL 放行开关（由模块配置「忽略SSL证书校验」注入）。</summary>
    public static Func<bool>? InsecureSslProvider;
    /// <summary>空响应特征消息（传输层据此做抽风重试）</summary>
    public const string MsgEmptyResponse = "响应内容为空";

    /// <summary>规范化说说tid：剥离unikey形式、剥离.311/.1等appid后缀</summary>
    public static string NormalizeTid(string? tid)
    {
        var s = tid?.Trim() ?? "";
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Contains("/mood/")) s = s.Split("/mood/", 2)[1];
        if (s.Contains('.'))
        {
            var idx = s.LastIndexOf('.');
            if (idx > 0 && s[(idx + 1)..].All(char.IsDigit)) s = s[..idx];
        }
        return s;
    }

    // ==================== 响应体诊断（R1）====================

    /// <summary>响应体类型判定（供重试/归类/日志使用）</summary>
    public enum BodyKind
    {
        Json,        // 正常 JSON/JSONP
        Empty,       // 空响应（服务端抽风，可重试）
        Truncated,   // 被截断的不完整 JSON（可重试）
        Html,        // 返回的是网页（风控/验证/错误页）
        Garbage      // 其他无法识别的正文
    }

    /// <summary>解析过程诊断信息（R1：解析失败也能拿到可排查的线索）</summary>
    public sealed class ParseDiagnostics
    {
        public BodyKind Kind = BodyKind.Json;
        public int Length;
        public string Snippet = "";
        public string Attempts = "";
        /// <summary>疑似登录失效（需要刷新 Cookie）</summary>
        public bool LooksLikeLogin;
        /// <summary>疑似风控/需要验证（刷新 Cookie 无用，应等待）</summary>
        public bool LooksLikeRisk;
        /// <summary>严格解析失败的精确位置线索（R1 加强：定位到行 + 该行内容）</summary>
        public string SyntaxHint = "";
        public string Describe() =>
            $"kind={Kind} len={Length} attempts=[{Attempts}]" +
            (LooksLikeLogin ? " login-required" : "") + (LooksLikeRisk ? " risk-page" : "") +
            (SyntaxHint.Length > 0 ? " | " + SyntaxHint : "");
    }

    /// <summary>可重试的解析失败（服务端抽风类）</summary>
    public static bool IsRetryableParseFailure(ParseDiagnostics d) =>
        d.Kind is BodyKind.Empty or BodyKind.Truncated or BodyKind.Garbage;

    private static readonly Regex LoginHintRegex = new(
        @"please\s*login|need\s*login|请先登录|需要登录|未登录|登录后|重新登录|登录失败|login_?state",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RiskHintRegex = new(
        @"验证码|captcha|verify|安全验证|异常访问|系统繁忙|系统错误|访问过于频繁|操作频繁|请稍后再试|forbidden",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>取正文片段用于日志（转义换行，限长）</summary>
    public static string SnippetOf(string? text, int max = 300)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = text.Length <= max ? text : text[..max] + "…";
        return s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", " ");
    }

    /// <summary>正文归类（在解析失败后调用）</summary>
    private static BodyKind ClassifyBody(string raw, string candidate)
    {
        var head = raw.Length > 2000 ? raw[..2000] : raw;
        bool looksHtml = head.TrimStart().StartsWith("<") ||
                         Regex.IsMatch(head, @"<(html|head|body|div|script|meta)\b", RegexOptions.IgnoreCase);
        if (looksHtml) return BodyKind.Html;
        // 括号/引号不平衡 ⇒ 截断
        if (!Balanced(candidate)) return BodyKind.Truncated;
        return BodyKind.Garbage;
    }

    /// <summary>
    /// JSON 感知清洗（4.5.4 新增，最后一档兜底）：单遍扫描，修正两类真实故障——
    /// ① **字符串值内的裸控制字符**（服务端把 HTML 直接塞进 JSON 时，里面的换行/制表没转义 ⇒ 全文档非法）
    /// ② **字符串值内未转义的引号**（HTML 属性引号；仅当其后不是结构字符 /,/}/]/: 时才判为内容并转义）
    /// 对合法 JSON 零副作用（合法字符串里的引号必然已转义）。
    /// </summary>
    public static string SanitizeJson(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 64);
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (!inStr)
            {
                if (c == '"') inStr = true;
                sb.Append(c);
                continue;
            }
            // —— 字符串内部 ——
            if (c == '\\' && i + 1 < s.Length) { sb.Append(c).Append(s[i + 1]); i++; continue; }
            if (c == '"')
            {
                int j = i + 1;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                if (j >= s.Length || s[j] == ',' || s[j] == '}' || s[j] == ']' || s[j] == ':')
                {
                    inStr = false;
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\\"");   // 内嵌引号（HTML 属性等）→ 转义
                }
                continue;
            }
            if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4")); continue; }   // 裸控制字符 → \uXXXX
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>严格解析失败的精确线索：行号 + 列号 + 报错那一行的内容（截断到 160 字）</summary>
    private static string BuildSyntaxHint(string jsonStr)
    {
        try
        {
            JsonDocument.Parse(jsonStr);
            return "";
        }
        catch (JsonException ex)
        {
            long line = (ex.LineNumber ?? 0) + 1;
            long col = (ex.BytePositionInLine ?? 0) + 1;
            string badLine = "";
            try
            {
                var lines = jsonStr.Split('\n');
                if (lines.Length > 0)
                {
                    int li = (int)Math.Clamp(line - 1, 0, lines.Length - 1);
                    badLine = lines[li];
                    if (badLine.Length > 160) badLine = badLine[..160] + "…";
                }
            }
            catch { }
            return $"首次语法错误 line {line} col {col}（{ex.Message}）该行：{badLine}";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// 汇报响应结构（解析成功但取不到数据时用）：定位"接口结构变了"这类问题，而不是静默返回 0 条。
    /// </summary>
    public static string DescribeShape(Dictionary<string, object?> data, int maxKeys = 12)
    {
        try
        {
            var top = data.Keys.Take(maxKeys).ToList();
            string detail = "";
            if (data.GetValueOrDefault("data") is Dictionary<string, object?> d)
            {
                var dk = d.Keys.Take(maxKeys).ToList();
                string inner = d.GetValueOrDefault("data") is List<object?> l
                    ? $"list(len={l.Count})"
                    : (d.ContainsKey("data") ? "非列表" : "无该键");
                detail = $" | data.keys=[{string.Join(",", dk)}] | data.data={inner}" +
                         $" | data.main={(d.ContainsKey("main") ? "有" : "无")}";
            }
            return $"top.keys=[{string.Join(",", top)}]{detail}";
        }
        catch { return "(结构描述失败)"; }
    }

    /// <summary>粗略平衡检查（只在失败后调用，用于区分"截断"与"真乱码"）</summary>
    private static bool Balanced(string s)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
        }
        return depth == 0 && !inStr;
    }

    // ==================== 解析（R1–R5）====================

    /// <summary>解析JSON/JSONP/非标准JSON响应</summary>
    public static Dictionary<string, object?> ParseResponse(string text)
        => ParseResponse(text, out _);

    /// <summary>
    /// 解析响应（带诊断输出）。
    /// 尝试顺序（R3 排列组合，任一成功即返回）：
    ///   strict → lenient → repair → lenient(repair) → repair(lenient) → 截断补全
    /// 全失败后再做字段级抢救（R4：只解析 msglist / data.html），最后归类（R5）。
    /// </summary>
    public static Dictionary<string, object?> ParseResponse(string text, out ParseDiagnostics diag)
    {
        diag = new ParseDiagnostics { Length = text?.Length ?? 0, Snippet = SnippetOf(text) };

        if (string.IsNullOrWhiteSpace(text))
        {
            diag.Kind = BodyKind.Empty;
            return new() { ["code"] = -1, ["message"] = MsgEmptyResponse };
        }

        // JSONP 回调剥离 / 取首尾花括号
        string jsonStr;
        var m = Regex.Match(text, @"callback\s*\(\s*([^{]*(\{.*\})[^)]*)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
        {
            jsonStr = m.Groups[2].Value;
        }
        else
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start == -1 || end == -1 || end < start)
            {
                diag.Kind = ClassifyBody(text, text);
                diag.LooksLikeLogin = LoginHintRegex.IsMatch(text);
                diag.LooksLikeRisk = RiskHintRegex.IsMatch(text);
                return new() { ["code"] = -1, ["message"] = DescribeFailure(diag) };
            }
            jsonStr = text.Substring(start, end - start + 1);
        }

        jsonStr = jsonStr.Replace("undefined", "null").Trim();

        // R3：多种宽松化排列组合，逐个尝试
        var attempts = new (string Name, Func<string> Build)[]
        {
            ("strict", () => jsonStr),
            ("js-normalize", () => JsObjectToJson(jsonStr)),
            ("repair", () => RepairHtmlQuotes(jsonStr)),
            ("js+repair", () => JsObjectToJson(RepairHtmlQuotes(jsonStr))),
            ("repair+js", () => RepairHtmlQuotes(JsObjectToJson(jsonStr))),
            ("sanitize", () => SanitizeJson(jsonStr)),                      // ★ 4.5.4：裸控制字符 + 内嵌引号
            ("sanitize+js", () => SanitizeJson(JsObjectToJson(jsonStr))),
            ("js+sanitize", () => JsObjectToJson(SanitizeJson(jsonStr))),
        };
        foreach (var (name, build) in attempts)
        {
            if (TryParseObject(build(), out var dict))
            {
                diag.Attempts = name;
                return dict!;
            }
        }
        // 截断补全：正文被切掉尾巴时补上闭合符号再试
        if (TryCompleteTruncated(SanitizeJson(jsonStr), out var completed) && TryParseObject(completed, out var dict2))
        {
            diag.Attempts = "truncation-completed";
            diag.Kind = BodyKind.Truncated;
            return dict2!;
        }

        // R4：字段级抢救——整文档废了也要把我们要的那段捞出来（先用清洗结果，引号才一致）
        string sanitized = SanitizeJson(jsonStr);
        if (TrySalvageMsgList(sanitized, out var salvaged))
        {
            diag.Attempts = "salvage:msglist";
            return salvaged!;
        }
        if (TrySalvageFeedsArray(sanitized, out var salvagedFeeds))
        {
            diag.Attempts = "salvage:feeds-array";
            return salvagedFeeds!;
        }
        if (TrySalvageDataHtml(sanitized, out var salvaged2))
        {
            diag.Attempts = "salvage:data.html";
            return salvaged2!;
        }

        // 精确报错位置：优先报告“规范化后”文档的错误（原始文档的问题可能早已被修好）
        diag.SyntaxHint = BuildSyntaxHint(JsObjectToJson(jsonStr));
        string rawHint = BuildSyntaxHint(jsonStr);
        if (rawHint.Length > 0 && diag.SyntaxHint.Length == 0) diag.SyntaxHint = rawHint;

        // R5：归类 + 可读错误
        diag.Kind = ClassifyBody(text, jsonStr);
        diag.LooksLikeLogin = LoginHintRegex.IsMatch(text);
        diag.LooksLikeRisk = RiskHintRegex.IsMatch(text);
        return new() { ["code"] = -1, ["message"] = DescribeFailure(diag) };
    }

    /// <summary>失败原因的可读文案（R5：不再只有一句"JSON 解析失败"）</summary>
    private static string DescribeFailure(ParseDiagnostics d)
    {
        if (d.LooksLikeLogin) return "响应提示需要登录（Cookie 可能已失效）";
        if (d.Kind == BodyKind.Html)
            return d.LooksLikeRisk
                ? "空间返回的是网页而非数据（疑似风控/需要验证，请稍后再试）"
                : "空间返回的是网页而非数据（可能是异常页，请稍后再试）";
        if (d.Kind == BodyKind.Truncated) return "响应不完整（可能被截断，请稍后再试）";
        if (d.Kind == BodyKind.Garbage) return "响应格式异常（非 JSON 数据）";
        return "JSON 解析失败";
    }

    private static bool TryParseObject(string candidate, out Dictionary<string, object?>? dict)
    {
        dict = null;
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            dict = JsonToDict(doc.RootElement);
            return true;
        }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>截断补全：补齐未闭合的字符串/数组/对象（最多补 64 个字符）</summary>
    private static bool TryCompleteTruncated(string s, out string completed)
    {
        completed = "";
        var sb = new System.Text.StringBuilder(s);
        var stack = new List<char>();
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') inStr = true;
            else if (c == '{' || c == '[') stack.Add(c);
            else if (c == '}' || c == ']')
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
        }
        int added = 0;
        if (inStr) { sb.Append('"'); added++; }
        for (int k = stack.Count - 1; k >= 0 && added < 64; k--)
        {
            sb.Append(stack[k] == '{' ? '}' : ']');
            added++;
        }
        if (added == 0) return false;
        completed = sb.ToString();
        return true;
    }

    /// <summary>R4：从损坏文档里把 "msglist":[...] 这个平衡数组整段抠出来单独解析</summary>
    public static bool TrySalvageMsgList(string jsonStr, out Dictionary<string, object?>? result)
    {
        result = null;
        int key = jsonStr.IndexOf("\"msglist\"", StringComparison.Ordinal);
        if (key < 0) return false;
        int colon = jsonStr.IndexOf(':', key);
        if (colon < 0) return false;
        int arrStart = jsonStr.IndexOf('[', colon);
        if (arrStart < 0) return false;
        string? arr = ExtractBalanced(jsonStr, arrStart, '[', ']');
        if (arr == null) return false;
        try
        {
            using var doc = JsonDocument.Parse(arr);
            var list = JsonToValue(doc.RootElement);
            if (list is List<object?> msgList)
            {
                result = new() { ["code"] = 0, ["message"] = "salvaged", ["msglist"] = msgList };
                return true;
            }
        }
        catch (JsonException) { }
        return false;
    }

    /// <summary>
    /// R4 加强（4.5.5）：把 `data:[{...}]` 这串 feed 数组**整段抠出来**、规范化后单独解析
    /// ⇒ 即使整文档含未知构造，好友动态仍可用（保住 uin/key/html 元数据，不做退化抢救）
    /// </summary>
    public static bool TrySalvageFeedsArray(string jsonStr, out Dictionary<string, object?>? result)
    {
        result = null;
        foreach (string key in new[] { "\"data\"", "data" })
        {
            int from = 0;
            while (true)
            {
                int at = jsonStr.IndexOf(key, from, StringComparison.Ordinal);
                if (at < 0) break;
                int colon = jsonStr.IndexOf(':', at + key.Length);
                if (colon < 0) break;
                int open = jsonStr.IndexOf('[', colon);
                if (open < 0) break;
                string? arr = ExtractBalanced(jsonStr, open, '[', ']');
                if (arr != null && TryParseArray(JsObjectToJson(arr), out var list) && list != null && list.Count > 0)
                {
                    result = new()
                    {
                        ["code"] = 0,
                        ["message"] = "salvaged-feeds",
                        ["data"] = new Dictionary<string, object?> { ["data"] = list }
                    };
                    return true;
                }
                from = at + key.Length;
            }
        }
        return false;
    }

    private static bool TryParseArray(string candidate, out List<object?>? list)
    {
        list = null;
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
            list = JsonToValue(doc.RootElement) as List<object?>;
            return list != null;
        }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>R4：从损坏文档里把 data.html / 各条 feed 的 html 字段抠出来（recent-feeds 备用路径）</summary>
    public static bool TrySalvageDataHtml(string jsonStr, out Dictionary<string, object?>? result)
    {
        result = null;
        if (jsonStr.IndexOf("\"html\"", StringComparison.Ordinal) < 0) return false;
        var feeds = new List<object?>();
        int idx = 0;
        while (true)
        {
            int key = jsonStr.IndexOf("\"html\"", idx, StringComparison.Ordinal);
            if (key < 0) break;
            idx = key + 6;
            int colon = jsonStr.IndexOf(':', key);
            if (colon < 0) break;
            int q = jsonStr.IndexOf('"', colon + 1);
            if (q < 0) break;
            string? raw = ExtractRawString(jsonStr, q);
            if (raw == null) continue;
            if (raw.TrimStart().StartsWith("<")) feeds.Add(new Dictionary<string, object?> { ["html"] = raw });
        }
        if (feeds.Count == 0) return false;
        result = new()
        {
            ["code"] = 0,
            ["message"] = "salvaged-html",
            ["data"] = new Dictionary<string, object?> { ["data"] = feeds }
        };
        return true;
    }

    /// <summary>从 start（指向 '[' 或 '{'）抠出平衡的一段</summary>
    private static string? ExtractBalanced(string s, int start, char open, char close)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return s.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    /// <summary>读取一个（可能被内嵌未转义引号破坏的）字符串值：
    /// 以「后随结构字符（, } ]）或另一个引号键」为结束判据，尽可能把整段 HTML 取回来</summary>
    private static string? ExtractRawString(string s, int quoteStart)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = quoteStart + 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length) { sb.Append(c).Append(s[i + 1]); i++; continue; }
            if (c == '"')
            {
                // 后随结构字符 ⇒ 认为字符串结束
                int k = i + 1;
                while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                if (k >= s.Length || s[k] == ',' || s[k] == '}' || s[k] == ']') return sb.ToString();
                sb.Append(c); // 否则是内嵌引号，保留
                continue;
            }
            sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// 修复双引号字符串内未转义的双引号（QQ空间 feeds3_html_more 的 html 字段内嵌 HTML 时常见）。
    /// 启发式：字符串内遇到 '"' 且前一个字符不是 '\'，若其后（跨空白）是 JSON 结构字符
    /// （, } ] : 或文本结尾）则视为字符串结束，否则视为 HTML 属性引号，转义为 \"。
    /// 对标准 JSON 零副作用（合法字符串内的引号必然已转义，先被 \\ 分支消费）。
    /// </summary>
    private static string RepairHtmlQuotes(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 32);
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < s.Length)
                {
                    sb.Append(c).Append(s[++i]); // 保留转义对
                    continue;
                }
                if (c == '"')
                {
                    int j = i + 1;
                    while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                    if (j >= s.Length || s[j] == ',' || s[j] == '}' || s[j] == ']' || s[j] == ':')
                    {
                        inString = false;
                        sb.Append(c);
                        continue;
                    }
                    sb.Append('\\').Append(c); // 字符串内未转义引号（HTML 属性），转义
                    continue;
                }
                sb.Append(c);
                continue;
            }
            if (c == '"') { inString = true; sb.Append(c); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// **JS 对象字面量 → 严格 JSON** 规范化器（4.5.5 重写，取代拼凑式宽松化）。
    ///
    /// 背景：`feeds3_html_more` 的 `data` 值是 **JavaScript 对象字面量**而非 JSON
    /// （无引号键、单引号字符串、可能有注释/尾随逗号），字符串里常直接嵌 HTML（含裸换行、未转义属性引号）。
    /// 本器是**字符级状态机**，输出保证为合法 JSON；比 json5 更宽（json5 不允许字符串内裸换行）。
    /// </summary>
    public static string JsObjectToJson(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 64);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i = Math.Min(s.Length, i + 2);
                continue;
            }

            if (c == '"' || c == '\'')
            {
                i = ReadStringInto(s, i, sb);
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                int st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++;
                string word = s.Substring(st, i - st);
                int k = i;
                while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                if (k < s.Length && s[k] == ':') sb.Append('"').Append(word).Append('"');
                else if (word is "true" or "false" or "null") sb.Append(word);
                else if (word is "undefined" or "NaN" or "Infinity") sb.Append("null");
                else sb.Append('"').Append(word).Append('"');
                continue;
            }

            if (char.IsDigit(c) || c == '+' || (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])) ||
                (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int st = i;
                if (s[i] == '+') i++;
                if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
                {
                    i += 2;
                    int hs = i;
                    while (i < s.Length && Uri.IsHexDigit(s[i])) i++;
                    long hv = 0;
                    try { hv = Convert.ToInt64(s.Substring(hs, i - hs), 16); } catch { }
                    sb.Append(hv);
                    continue;
                }
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                       ((s[i] == '+' || s[i] == '-') && i > st && (s[i - 1] == 'e' || s[i - 1] == 'E')))) i++;
                sb.Append(s, st, i - st);
                continue;
            }

            if (c == ',')
            {
                int k = i + 1;
                while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                if (k < s.Length && (s[k] == '}' || s[k] == ']')) { i++; continue; }
                sb.Append(',');
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>读入一个字符串字面量并写出合法 JSON 字符串；返回下一个待处理下标</summary>
    private static int ReadStringInto(string s, int start, System.Text.StringBuilder sb)
    {
        char quote = s[start];
        bool single = quote == '\'';
        sb.Append('"');
        int i = start + 1;
        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\\' && i + 1 < s.Length)
            {
                char nx = s[i + 1];
                if (nx == 'n') { sb.Append("\\n"); i += 2; continue; }
                if (nx == 'r') { sb.Append("\\r"); i += 2; continue; }
                if (nx == 't') { sb.Append("\\t"); i += 2; continue; }
                if (nx == 'b') { sb.Append("\\b"); i += 2; continue; }
                if (nx == 'f') { sb.Append("\\f"); i += 2; continue; }
                if (nx == '"') { sb.Append("\\\""); i += 2; continue; }
                if (nx == '\\') { sb.Append("\\\\"); i += 2; continue; }
                if (nx == '/') { sb.Append('/'); i += 2; continue; }
                if (nx == '\'') { sb.Append("'"); i += 2; continue; }
                if (nx == 'n') { i += 2; continue; }
                if (nx == 'u' && i + 5 < s.Length)
                {
                    sb.Append('\\').Append('u').Append(s, i + 2, 4);
                    i += 6;
                    continue;
                }
                if (nx == 'x' && i + 3 < s.Length && Uri.IsHexDigit(s[i + 2]) && Uri.IsHexDigit(s[i + 3]))
                {
                    int code = Convert.ToInt32(s.Substring(i + 2, 2), 16);
                    sb.Append("\\u").Append(code.ToString("x4"));
                    i += 4;
                    continue;
                }
                if (nx == '\n') { i += 2; continue; }      // 续行
                sb.Append('\\').Append(nx);
                i += 2;
                continue;
            }

            if (c == quote)
            {
                if (!single)
                {
                    int k = i + 1;
                    while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
                    bool terminator = k >= s.Length || s[k] == ',' || s[k] == '}' || s[k] == ']' || s[k] == ':';
                    if (!terminator) { sb.Append("\\\""); i++; continue; }   // HTML 属性引号
                }
                sb.Append('"');
                return i + 1;
            }

            if (c == '"' && single) { sb.Append("\\\""); i++; continue; }
            if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4")); i++; continue; }
            if (c == '\n' || c == '\r') { i++; continue; }
            sb.Append(c);
            i++;
        }
        sb.Append('"');
        return i;
    }

    public static string ToLenientJsonPublic(string s) => JsObjectToJson(s);

    private static string ToLenientJson(string s) => JsObjectToJson(s);

    private static Dictionary<string, object?> JsonToDict(JsonElement el)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in el.EnumerateObject())
        {
            dict[prop.Name] = JsonToValue(prop.Value);
        }
        return dict;
    }

    private static object? JsonToValue(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                return JsonToDict(el);
            case JsonValueKind.Array:
                return el.EnumerateArray().Select(JsonToValue).ToList();
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                return el.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    /// <summary>解析上传结果，返回(picbo, richval)</summary>
    public static (string PicBo, string RichVal) ParseUploadResult(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("data", out var dataObj) || dataObj is not Dictionary<string, object?> data)
            throw new Exception("上传结果缺少data字段");

        var url = data.GetValueOrDefault("url")?.ToString() ?? "";
        var picbo = url.Split("&bo=", 2) is { Length: 2 } parts ? parts[1] : "";

        var richval = $",{data.GetValueOrDefault("albumid")},{data.GetValueOrDefault("lloc")},{data.GetValueOrDefault("sloc")},{data.GetValueOrDefault("type")},{data.GetValueOrDefault("height")},{data.GetValueOrDefault("width")},,{data.GetValueOrDefault("height")},{data.GetValueOrDefault("width")}";
        return (picbo, richval);
    }

    /// <summary>
    /// 解析说说详情（兼容三种返回结构）：
    /// 1) 顶层即说说字段（content/commentlist 等，h5 msgdetail_v6 常见）
    /// 2) msglist: [{...}]（与 msglist_v6 同构）
    /// 3) data: {...} 嵌套（data 内再含 msglist 或直接是说说字段）
    /// 解析不出有效说说时返回空列表（不抛异常）。
    /// </summary>
    public static List<QzonePost> ParseDetail(Dictionary<string, object?> raw)
    {
        if (raw == null || raw.Count == 0) return new();
        // 结构2：msglist 数组
        if (raw.GetValueOrDefault("msglist") is List<object?> msgList && msgList.Count > 0)
            return ParseFeeds(msgList);
        // 结构3：data 嵌套
        if (raw.GetValueOrDefault("data") is Dictionary<string, object?> dataDict)
        {
            if (dataDict.GetValueOrDefault("msglist") is List<object?> innerList && innerList.Count > 0)
                return ParseFeeds(innerList);
            var innerPosts = ParseFeeds(new List<object?> { dataDict });
            if (innerPosts.Count > 0 && !string.IsNullOrEmpty(innerPosts[0].Tid) && innerPosts[0].Tid != "0")
                return innerPosts;
        }
        // 结构1：顶层即说说
        var topPosts = ParseFeeds(new List<object?> { raw });
        if (topPosts.Count > 0 && !string.IsNullOrEmpty(topPosts[0].Tid) && topPosts[0].Tid != "0")
            return topPosts;
        return new();
    }

    /// <summary>解析说说列表</summary>
    public static List<QzonePost> ParseFeeds(List<object?> msgList)
    {
        var posts = new List<QzonePost>();
        foreach (var item in msgList)
        {
            if (item is not Dictionary<string, object?> msg) continue;

            var imageUrls = new List<string>();
            if (msg.TryGetValue("pic", out var picObj) && picObj is List<object?> picList)
            {
                foreach (var p in picList)
                {
                    if (p is not Dictionary<string, object?> imgData) continue;
                    foreach (var key in new[] { "url2", "url3", "url1", "smallurl" })
                    {
                        if (imgData.TryGetValue(key, out var raw) && raw != null)
                        {
                            imageUrls.Add(raw.ToString()!);
                            break;
                        }
                    }
                }
            }
            if (msg.TryGetValue("video", out var videoObj) && videoObj is List<object?> videoList)
            {
                foreach (var v in videoList)
                {
                    if (v is not Dictionary<string, object?> video) continue;
                    var videoImage = video.GetValueOrDefault("url1")?.ToString() ?? video.GetValueOrDefault("pic_url")?.ToString();
                    if (!string.IsNullOrEmpty(videoImage))
                        imageUrls.Add(videoImage);
                }
            }

            var comments = new List<QzoneComment>();
            if (msg.TryGetValue("commentlist", out var cmtObj) && cmtObj is List<object?> cmtList)
            {
                comments = ParseComments(cmtList);
            }

            var likeUsers = new List<string>();
            int likeCount = 0;
            string likeKey = "";
            bool isLiked = false;
            if (msg.TryGetValue("likeinfo", out var likeObj) && likeObj is Dictionary<string, object?> likeInfo)
            {
                if (likeInfo.TryGetValue("like_uin_info", out var uinInfoObj) && uinInfoObj is List<object?> uinInfoList)
                {
                    foreach (var u in uinInfoList)
                    {
                        if (u is not Dictionary<string, object?> uinDict) continue;
                        var nick = uinDict.GetValueOrDefault("nick")?.ToString() ?? uinDict.GetValueOrDefault("fuin")?.ToString();
                        if (!string.IsNullOrEmpty(nick))
                            likeUsers.Add(nick);
                    }
                }
                likeCount = Convert.ToInt32(likeInfo.GetValueOrDefault("total_num") ?? likeInfo.GetValueOrDefault("total_number") ?? likeUsers.Count);
                likeKey = likeInfo.GetValueOrDefault("curlikekey")?.ToString() ?? likeInfo.GetValueOrDefault("orglikekey")?.ToString() ?? "";
            }
            var likedFlag = msg.GetValueOrDefault("isliked") ?? msg.GetValueOrDefault("isLiked") ?? msg.GetValueOrDefault("liked") ?? msg.GetValueOrDefault("is_liked");
            isLiked = likedFlag is 1L or 1 or true or "1";

            var tid = msg.GetValueOrDefault("tid")?.ToString() ?? "0";
            var post = new QzonePost
            {
                Tid = tid,
                Uin = Convert.ToInt64(msg.GetValueOrDefault("uin") ?? 0),
                Name = msg.GetValueOrDefault("name")?.ToString() ?? "",
                Text = msg.GetValueOrDefault("content")?.ToString()?.Trim() ?? "",
                Images = imageUrls,
                CreateTime = Convert.ToInt64(msg.GetValueOrDefault("created_time") ?? 0),
                RtCon = (msg.GetValueOrDefault("rt_con") as Dictionary<string, object?>)?.GetValueOrDefault("content")?.ToString() ?? "",
                Comments = comments,
                LikeCount = likeCount,
                LikeUsers = likeUsers,
                LikeKey = likeKey,
                IsLiked = isLiked,
                ExtraText = msg.GetValueOrDefault("source_name")?.ToString() ?? "",
            };
            posts.Add(post);
        }
        return posts;
    }

    /// <summary>解析评论列表（含楼中楼list_3扁平化）</summary>
    public static List<QzoneComment> ParseComments(List<object?> cmtList)
    {
        var comments = new List<QzoneComment>();
        foreach (var item in cmtList)
        {
            if (item is not Dictionary<string, object?> raw) continue;
            comments.Add(ParseComment(raw, null));
            if (raw.TryGetValue("list_3", out var subObj) && subObj is List<object?> subList)
            {
                var mainTid = Convert.ToInt32(raw.GetValueOrDefault("tid") ?? 0);
                foreach (var sub in subList)
                {
                    if (sub is Dictionary<string, object?> subRaw)
                        comments.Add(ParseComment(subRaw, mainTid));
                }
            }
        }
        return comments;
    }

    private static QzoneComment ParseComment(Dictionary<string, object?> raw, int? parentTid)
    {
        var rawTid = raw.GetValueOrDefault("tid")?.ToString() ?? raw.GetValueOrDefault("id")?.ToString() ?? "";
        var commentId = raw.GetValueOrDefault("commentid")?.ToString()
            ?? raw.GetValueOrDefault("comment_id")?.ToString()
            ?? raw.GetValueOrDefault("cid")?.ToString()
            ?? raw.GetValueOrDefault("commentId")?.ToString()
            ?? rawTid;
        int tidInt = 0;
        int.TryParse(rawTid, out tidInt);

        return new QzoneComment
        {
            Uin = Convert.ToInt64(raw.GetValueOrDefault("uin") ?? 0),
            Nickname = raw.GetValueOrDefault("name")?.ToString() ?? "",
            Content = raw.GetValueOrDefault("content")?.ToString() ?? "",
            CreateTime = Convert.ToInt64(raw.GetValueOrDefault("create_time") ?? raw.GetValueOrDefault("ctime") ?? 0),
            CreateTimeStr = raw.GetValueOrDefault("createTime2")?.ToString() ?? raw.GetValueOrDefault("createTimeStr")?.ToString() ?? "",
            Tid = tidInt,
            CommentId = commentId,
            ParentTid = parentTid ?? (raw.TryGetValue("parent_tid", out var pt) && pt != null ? Convert.ToInt32(pt) : null),
            SourceName = raw.GetValueOrDefault("source_name")?.ToString() ?? "",
            SourceUrl = raw.GetValueOrDefault("source_url")?.ToString() ?? "",
        };
    }

    /// <summary>解析访客列表为可读文本</summary>
    public static string ParseVisitors(Dictionary<string, object?> raw, int maxItems = 20)
    {
        var data = raw.GetValueOrDefault("data") as Dictionary<string, object?>;
        var items = data?.GetValueOrDefault("items") as List<object?>;
        if (items == null || items.Count == 0)
            return "### 最近来访明细\n\n暂无访客记录";
        maxItems = Math.Max(1, maxItems);
        items = items.Take(maxItems).ToList();

        var srcMap = new Dictionary<int, string>
        {
            [0] = "访问空间",
            [13] = "查看动态",
            [32] = "手机QQ",
            [41] = "国际版QQ/TIM",
        };

        var lines = new List<string> { "\n### 最近来访明细\n", "| 时间 | 访客 | 来源 | 状态 | 带来了 |", "| --- | --- | --- | --- | --- |" };
        foreach (var item in items)
        {
            if (item is not Dictionary<string, object?> v) continue;
            var ts = Convert.ToInt64(v.GetValueOrDefault("time") ?? 0);
            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString("MM-dd HH:mm");
            var name = v.GetValueOrDefault("name")?.ToString();
            var visitor = SafeCell(string.IsNullOrEmpty(name) ? "匿名" : name, 16);
            var srcVal = v.GetValueOrDefault("src");
            // JsonToValue 对数字返回 long（装箱），需同时兼容 long/int
            var srcKey = srcVal is long l ? (int)l : srcVal is int i ? i : -1;
            var src = SafeCell(srcMap.GetValueOrDefault(srcKey, $"未知({srcKey})"), 12);
            var statusParts = new List<string>();
            if (v.GetValueOrDefault("yellow") is long yellowL && yellowL > 0)
                statusParts.Add($"LV{yellowL}");
            else if (v.GetValueOrDefault("yellow") is int yellowI && yellowI > 0)
                statusParts.Add($"LV{yellowI}");
            if (v.GetValueOrDefault("is_hide_visit") is true or 1L or 1)
                statusParts.Add("隐身");
            var status = SafeCell(string.Join(" / ", statusParts), 12);
            var remark = "-";
            if (v.GetValueOrDefault("shuoshuoes") is List<object?> shuos)
            {
                foreach (var s in shuos)
                {
                    if (s is Dictionary<string, object?> sd && sd.GetValueOrDefault("name")?.ToString() is { Length: > 0 } title)
                    {
                        remark = SafeCell($"说说:{title}", 30);
                        break;
                    }
                }
            }
            if (remark == "-" && v.GetValueOrDefault("uins") is List<object?> uins)
            {
                var names = new List<string>();
                foreach (var u in uins)
                {
                    if (u is Dictionary<string, object?> ud && ud.GetValueOrDefault("name")?.ToString() is { Length: > 0 } n)
                        names.Add(n);
                }
                if (names.Count > 0)
                    remark = SafeCell(string.Join("、", names), 30);
            }
            lines.Add($"| {SafeCell(dt, 16)} | {visitor} | {src} | {status} | {remark} |");
        }
        var today = Convert.ToInt32(data?.GetValueOrDefault("todaycount") ?? 0);
        var total = Convert.ToInt32(data?.GetValueOrDefault("totalcount") ?? 0);
        lines.Add($"今日访客共 {today} 人， 最近30天访客共 {total} 人");
        return string.Join("\n", lines);
    }

    private static string SafeCell(string? text, int maxLen = 30)
    {
        if (string.IsNullOrEmpty(text)) return "-";
        text = text.Replace("\n", " ").Replace("|", "｜").Trim();
        if (text.Length > maxLen) text = text[..maxLen] + "…";
        return string.IsNullOrEmpty(text) ? "-" : text;
    }

    /// <summary>解析最近说说列表（feeds3_html_more，HTML解析）</summary>
    public static List<QzonePost> ParseRecentFeeds(Dictionary<string, object?> data)
    {
        var feeds = (data.GetValueOrDefault("data") as Dictionary<string, object?>)?.GetValueOrDefault("data") as List<object?>;
        if (feeds == null || feeds.Count == 0) return new();
        var posts = new List<QzonePost>();
        foreach (var feedObj in feeds)
        {
            if (feedObj is not Dictionary<string, object?> feed) continue;
            var appid = feed.GetValueOrDefault("appid")?.ToString() ?? "";
            if (appid != "311") continue;
            var uin = feed.GetValueOrDefault("uin")?.ToString() ?? "";
            var tid = feed.GetValueOrDefault("key")?.ToString() ?? "";
            if (string.IsNullOrEmpty(uin) || string.IsNullOrEmpty(tid)) continue;
            long createTime = 0;
            long.TryParse(feed.GetValueOrDefault("abstime")?.ToString() ?? "", out createTime);
            var nickname = feed.GetValueOrDefault("nickname")?.ToString() ?? "";
            var htmlContent = feed.GetValueOrDefault("html")?.ToString() ?? "";
            if (string.IsNullOrEmpty(htmlContent)) continue;

            var text = ExtractHtmlText(htmlContent, "div", "f-info");
            var rtCon = ExtractHtmlText(htmlContent, "div", "txt-box");
            if (rtCon.Contains('：'))
                rtCon = rtCon.Split('：', 2)[1].Trim();

            var imageUrls = new List<string>();
            foreach (var src in ExtractHtmlImgSrcs(htmlContent, "div", "img-box"))
            {
                if (!src.StartsWith("http://qzonestyle.gtimg.cn"))
                    imageUrls.Add(src);
            }
            var videoImg = ExtractHtmlFirstImgSrc(htmlContent, "div", "video-img");
            if (!string.IsNullOrEmpty(videoImg)) imageUrls.Add(videoImg);

            var comments = new List<QzoneComment>();
            foreach (var itemHtml in ExtractHtmlItems(htmlContent, "li", "comments-item"))
            {
                var dataUin = ExtractAttr(itemHtml, "data-uin");
                var dataTid = ExtractAttr(itemHtml, "data-tid");
                var dataNick = ExtractAttr(itemHtml, "data-nick");
                var content = ExtractHtmlText(itemHtml, "div", "comments-content");
                if (content.Contains(':'))
                    content = content.Split(':', 2)[1].Trim();
                var timeStr = ExtractHtmlText(itemHtml, "span", "state");
                int? parentTid = null;
                if (itemHtml.Contains("mod-comments-sub"))
                    parentTid = ExtractParentTid(itemHtml);
                long.TryParse(dataUin, out var cUin);
                int.TryParse(dataTid, out var cTid);
                comments.Add(new QzoneComment
                {
                    Uin = cUin,
                    Nickname = dataNick,
                    Content = content,
                    CreateTimeStr = timeStr,
                    Tid = cTid,
                    ParentTid = parentTid,
                });
            }

            posts.Add(new QzonePost
            {
                Tid = tid,
                Uin = long.TryParse(uin, out var u) ? u : 0,
                Name = nickname,
                Text = text,
                Images = imageUrls.Distinct().ToList(),
                CreateTime = createTime,
                RtCon = rtCon,
                Comments = comments,
            });
        }
        return posts;
    }

    // ---------- 简易HTML解析（feeds3_html_more 返回的是HTML片段） ----------

    private static string ExtractHtmlText(string html, string tag, string className)
    {
        var m = Regex.Match(html, $@"<{tag}[^>]*class=[""'][^""']*{className}[^""']*[""'][^>]*>(.*?)</{tag}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        return Regex.Replace(m.Groups[1].Value, @"<[^>]+>", "").Trim();
    }

    private static List<string> ExtractHtmlImgSrcs(string html, string tag, string className)
    {
        var result = new List<string>();
        var m = Regex.Match(html, $@"<{tag}[^>]*class=[""'][^""']*{className}[^""']*[""'][^>]*>(.*?)</{tag}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!m.Success) return result;
        foreach (Match img in Regex.Matches(m.Groups[1].Value, @"<img[^>]*src=[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase))
            result.Add(img.Groups["src"].Value);
        return result;
    }

    private static string ExtractHtmlFirstImgSrc(string html, string tag, string className)
    {
        var m = Regex.Match(html, $@"<{tag}[^>]*class=[""'][^""']*{className}[^""']*[""'][^>]*>\s*<img[^>]*src=[""'](?<src>[^""']+)[""']", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["src"].Value : "";
    }

    private static List<string> ExtractHtmlItems(string html, string tag, string className)
    {
        var result = new List<string>();
        foreach (Match m in Regex.Matches(html, $@"<{tag}[^>]*class=[""'][^""']*{className}[^""']*[""'][^>]*>.*?</{tag}>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            result.Add(m.Value);
        return result;
    }

    private static string ExtractAttr(string html, string attr)
    {
        var m = Regex.Match(html, $@"{attr}=[""'](?<v>[^""']*)[""']", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["v"].Value : "";
    }

    private static int? ExtractParentTid(string itemHtml)
    {
        // 楼中回复：向上找父级 li.comments-item 的 data-tid
        var m = Regex.Match(itemHtml, @"<li[^>]*class=[""'][^""']*comments-item[^""']*[""'][^>]*data-tid=[""'](?<tid>\d+)[""']", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups["tid"].Value) : null;
    }

    /// <summary>格式化时间戳</summary>
    public static string FormatTime(long timestamp)
    {
        if (timestamp <= 0) return "未知时间";
        var dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
        var now = DateTime.Now;
        if (dt.Date == now.Date) return dt.ToString("HH:mm");
        if (dt.Year == now.Year) return dt.ToString("MM-dd HH:mm");
        return dt.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 下载图片为字节数组（对齐 Kira download_file）：
    /// 带 qzone Referer 重试，最后一次不带 Referer 兼容外部 CDN；
    /// HTTP 400（rkey 过期/签名失效）快速失败不重试，交由上层 get_msg 续命或降级；
    /// 其余失败每次重试前等 2s。全部失败返回 null。
    /// </summary>
    public static async Task<byte[]?> DownloadImageAsync(string url, int maxRetries = 3, CancellationToken ct = default)
    {
        url = CleanUrl(url);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var handler = new HttpClientHandler { SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13 };
                if (InsecureSslProvider?.Invoke() == true)
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) }; // 对齐 Kira download_file 的 60s
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                // 前几次带 qzone Referer，最后一次不带（兼容外部 CDN）
                if (attempt < maxRetries - 1)
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://qzone.qq.com/");
                using var resp = await http.GetAsync(url, ct);
                if ((int)resp.StatusCode == 200)
                    return await resp.Content.ReadAsByteArrayAsync(ct);
                // rkey 过期 / 签名失效：重试同一 URL 无意义，快速失败
                if ((int)resp.StatusCode == 400)
                    return null;
            }
            catch (Exception)
            {
                // 超时/网络异常：进入下一轮重试
            }
            if (attempt < maxRetries - 1)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return null;
    }

    /// <summary>清洗URL：去多余空格/引号，解码HTML实体，移除空白字符</summary>
    public static string CleanUrl(string url)
    {
        url = (url ?? "").Trim().Trim('"').Trim('\'');
        url = System.Net.WebUtility.HtmlDecode(url);
        url = Regex.Replace(url, @"\s+", "");
        return url;
    }

    /// <summary>
    /// 规范化图片列表，返回字节数组列表（校验图片魔数）。
    /// 本地路径支持：正反斜杠、含空格目录、首尾引号、file:// URI；allowLocalPath 非空时做目录白名单校验。
    /// </summary>
    public static async Task<List<byte[]>> NormalizeImagesAsync(List<string> images, List<string>? errors = null,
        Func<string, bool>? allowLocalPath = null, CancellationToken ct = default)
    {
        errors ??= new List<string>();
        var result = new List<byte[]>();
        foreach (var raw in images)
        {
            // 剥离首尾空白与引号（AI 传参常带引号）
            var img = (raw ?? "").Trim().Trim('"', '\'');
            if (img.Length == 0) continue;
            try
            {
                if (img.StartsWith("http://") || img.StartsWith("https://"))
                {
                    var data = await DownloadImageAsync(img, ct: ct);
                    if (data == null)
                    {
                        errors.Add($"图片下载失败（可能链接已过期）: {img[..Math.Min(80, img.Length)]}");
                        continue;
                    }
                    if (!LooksLikeImage(data))
                    {
                        errors.Add($"下载内容不是图片（链接可能已过期返回错误页）: {img[..Math.Min(80, img.Length)]}");
                        continue;
                    }
                    result.Add(data);
                }
                else
                {
                    // 本地文件路径（如 D:\ComfyUI\output\x.png / /home/u/pic.png / file:///D:/x.png）
                    var path = img;
                    if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        try { path = new Uri(path).LocalPath; } catch { }
                    }
                    try { path = Path.GetFullPath(path); } catch { }
                    if (allowLocalPath != null && !allowLocalPath(path))
                    {
                        errors.Add($"本地图片路径不在白名单目录内（见插件配置「本地图片目录白名单」）: {path}");
                        continue;
                    }
                    if (!File.Exists(path))
                    {
                        errors.Add($"本地图片文件不存在: {path}");
                        continue;
                    }
                    var data = await File.ReadAllBytesAsync(path, ct);
                    if (!LooksLikeImage(data))
                    {
                        errors.Add($"本地文件内容不是图片: {path}");
                        continue;
                    }
                    result.Add(data);
                }
            }
            catch (Exception e)
            {
                errors.Add($"{img}: {e.Message}");
            }
        }
        return result;
    }

    private static readonly byte[][] ImageMagic = {
        new byte[] { 0xFF, 0xD8, 0xFF },          // JPEG
        new byte[] { 0x89, 0x50, 0x4E, 0x47 },     // PNG
        new byte[] { 0x47, 0x49, 0x46, 0x38 },     // GIF
        new byte[] { 0x52, 0x49, 0x46, 0x46 },     // WebP/RIFF
        new byte[] { 0x42, 0x4D },                 // BMP
        new byte[] { 0x00, 0x00, 0x00 },           // HEIC/MP4 系（粗判）
    };

    /// <summary>校验下载内容是否为图片</summary>
    public static bool LooksLikeImage(byte[] data)
    {
        if (data == null || data.Length < 12) return false;
        foreach (var magic in ImageMagic)
        {
            if (data.Length >= magic.Length)
            {
                bool match = true;
                for (int i = 0; i < magic.Length; i++)
                {
                    if (data[i] != magic[i]) { match = false; break; }
                }
                if (match) return true;
            }
        }
        return false;
    }
}
