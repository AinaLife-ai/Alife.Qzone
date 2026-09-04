using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AinaLife.Qzone;

/// <summary>
/// QQ空间 HTTP 传输层（对齐 KiraAI qzone/client.py）：
/// - 服务端空响应抽风：独立重试额度，递增退避 1/2/3/4 秒，最多 4 次（不刷新 Cookie）
/// - 登录失效检测：HTTP 401 / 业务码(-3000,-100,-10001,-10006) / subcode=-4001 / 消息特征正则
///   命中后回调 OnAuthExpired 强制刷新 Cookie，等待 1.5s 后重试，最多 4 次
/// - 403 无业务码时包装为「权限不足」
/// </summary>
public class QzoneHttpClient : IDisposable
{
    private static readonly HashSet<long> AuthFailureCodes = new() { -3000, -100, -10001, -10006 };
    private static readonly Regex AuthMsgRegex = new(
        @"need\s*login|请先登录|需要登录|未登录|登录后|重新登录|登录失败",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly int _defaultTimeoutSeconds;
    private readonly Func<QzoneContext> _ctxProvider;

    /// <summary>登录失效回调：返回 true 表示已刷新凭证可重试。由模块注入。</summary>
    public Func<Task<bool>>? OnAuthExpired { get; set; }

    /// <param name="ctxProvider">每次请求取最新上下文（Cookie 原地刷新后自动生效）。</param>
    public QzoneHttpClient(int timeoutSeconds, ILogger logger, Func<QzoneContext> ctxProvider,
        Func<bool>? insecureSslProvider = null)
    {
        _defaultTimeoutSeconds = timeoutSeconds;
        _logger = logger;
        _ctxProvider = ctxProvider;
        // UseCookies=false：禁止自动保存响应 Set-Cookie，防旧 Cookie 残留（对齐 DummyCookieJar）
        // SslProtocols：老 Windows 默认可能协商 TLS1.0/1.1 被腾讯服务器拒绝（报 SSL 连接错误），显式指定 TLS1.2+
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        if (insecureSslProvider != null)
        {
            // 动态读取配置：证书有效时正常校验，仅当配置开启「忽略SSL证书校验」时放行（本机代理/VPN/抓包拦截场景）
            handler.ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None || insecureSslProvider();
        }
        _http = new HttpClient(handler);
    }

    public async Task<Dictionary<string, object?>> RequestAsync(
        HttpMethod method,
        string url,
        Dictionary<string, string>? query = null,
        Dictionary<string, string>? form = null,
        Dictionary<string, string>? headers = null,
        int? timeoutSeconds = null,
        int emptyRetryLimit = 4,
        CancellationToken ct = default)
    {
        return await RequestInternal(method, url, query, form, headers,
            timeoutSeconds, retry: 0, emptyRetry: 0, emptyRetryLimit, ct);
    }

    private async Task<Dictionary<string, object?>> RequestInternal(
        HttpMethod method,
        string url,
        Dictionary<string, string>? query,
        Dictionary<string, string>? form,
        Dictionary<string, string>? headers,
        int? timeoutSeconds,
        int retry,
        int emptyRetry,
        int emptyRetryLimit,
        CancellationToken ct)
    {
        var ctx = _ctxProvider(); // 每次请求（含重试）取最新上下文
        var fullUrl = url;
        if (query != null && query.Count > 0)
        {
            var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            fullUrl += (url.Contains('?') ? "&" : "?") + qs;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds ?? _defaultTimeoutSeconds));

        using var req = new HttpRequestMessage(method, fullUrl);
        foreach (var (k, v) in ctx.Headers())
            req.Headers.TryAddWithoutValidation(k, v);
        if (headers != null)
        {
            foreach (var (k, v) in headers)
                req.Headers.TryAddWithoutValidation(k, v);
        }
        // Cookie 必须合并为单个 header 用 "; " 连接（对齐 Kira/aiohttp 与浏览器行为）：
        // 逐条发多个 Cookie header 时，.NET 会用 ", " 拼接或只生效第一条，导致 skey/p_skey 丢失引发鉴权失败
        var cookies = ctx.Cookies();
        if (cookies.Count > 0)
            req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}")));
        if (form != null && form.Count > 0)
            req.Content = new FormUrlEncodedContent(form);

        using var resp = await _http.SendAsync(req, cts.Token);
        var text = await resp.Content.ReadAsStringAsync(cts.Token);
        var parsed = QzoneParser.ParseResponse(text);

        // 服务端偶发空响应（QZone 常见抽风）：独立重试额度，递增退避
        if (parsed.GetValueOrDefault("message")?.ToString() == QzoneParser.MsgEmptyResponse && emptyRetry < emptyRetryLimit)
        {
            var wait = emptyRetry + 1;
            _logger.LogWarning("响应内容为空，{Wait}秒后重试({N}/{Limit}): {Url}", wait, emptyRetry + 1, emptyRetryLimit, url);
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            return await RequestInternal(method, url, query, form, headers,
                timeoutSeconds, retry, emptyRetry + 1, emptyRetryLimit, ct);
        }

        // 登录失效检测 → 刷新 Cookie 自救
        if (IsAuthFailure((int)resp.StatusCode, parsed))
        {
            if (retry >= 4)
                throw new Exception("登录失效，Cookie 刷新后重试仍失败");

            _logger.LogWarning("检测到登录失效，尝试刷新 Cookie (status={Status}, 响应片段: {Snippet})",
                (int)resp.StatusCode, text.Length > 200 ? text[..200] : text);

            var refreshed = false;
            if (OnAuthExpired != null)
            {
                try { refreshed = await OnAuthExpired(); }
                catch (Exception e) { _logger.LogError(e, "刷新 Cookie 回调异常"); }
            }
            if (!refreshed)
                throw new Exception("登录失效且无法从 OneBot 刷新 Cookie");

            // 刷新成功后稍等再重试：新凭证在服务端有短暂生效窗口
            await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            // RequestInternal 每次都会重新取最新上下文（Session 已原地更新）
            return await RequestInternal(method, url, query, form, headers,
                timeoutSeconds, retry + 1, emptyRetry, emptyRetryLimit, ct);
        }

        // 403 且无业务码 → 包装为权限不足（code 可能是 long/int/string，统一转数值比较）
        if ((int)resp.StatusCode == 403)
        {
            var codeVal = parsed.GetValueOrDefault("code");
            bool noBizCode = codeVal == null;
            if (!noBizCode)
            {
                try { noBizCode = Convert.ToInt64(codeVal) == -1; }
                catch { noBizCode = true; }
            }
            if (noBizCode)
            {
                parsed["code"] = 403L;
                parsed["message"] = "权限不足";
            }
        }

        return parsed;
    }

    private static bool IsAuthFailure(int status, Dictionary<string, object?> parsed)
    {
        if (status == 401) return true;

        // 部分接口（如图片上传）把错误码放在 ret 或嵌套的 data.ret 里
        var candidates = new List<object?> { parsed.GetValueOrDefault("code"), parsed.GetValueOrDefault("ret") };
        if (parsed.GetValueOrDefault("data") is Dictionary<string, object?> data)
        {
            candidates.Add(data.GetValueOrDefault("code"));
            candidates.Add(data.GetValueOrDefault("ret"));
        }
        foreach (var c in candidates)
        {
            if (c == null) continue;
            try
            {
                var v = Convert.ToInt64(c);
                if (v == -3000 || AuthFailureCodes.Contains(v)) return true;
            }
            catch { }
        }
        try
        {
            if (Convert.ToInt64(parsed.GetValueOrDefault("subcode") ?? 0) == -4001) return true;
        }
        catch { }

        var messages = new List<string>();
        foreach (var k in new[] { "message", "msg", "tips" })
        {
            if (parsed.GetValueOrDefault(k)?.ToString() is { Length: > 0 } m) messages.Add(m);
        }
        if (parsed.GetValueOrDefault("data") is Dictionary<string, object?> d2)
        {
            foreach (var k in new[] { "message", "msg", "tips" })
            {
                if (d2.GetValueOrDefault(k)?.ToString() is { Length: > 0 } m) messages.Add(m);
            }
        }
        return messages.Count > 0 && AuthMsgRegex.IsMatch(string.Join(" ", messages));
    }

    public void Dispose() => _http.Dispose();
}
