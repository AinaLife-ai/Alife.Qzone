using System;
using System.Collections.Generic;
using System.Linq;

namespace AinaLife.Qzone;

/// <summary>
/// QQ 登录会话（可变）。对齐 KiraAI QzoneSession：
/// Cookie 刷新时原地更新内部上下文，持有本对象的 Api/HttpClient 自动用上新凭证。
/// 配置中的 cookies_str 仅作自动刷新失败时的应急后备，运行态 Cookie 只存在于本对象内。
/// </summary>
public class QzoneSession
{
    private QzoneContext? _ctx;
    private readonly object _lock = new();
    private readonly Func<string> _fallbackCookieProvider;

    public QzoneSession(Func<string> fallbackCookieProvider)
    {
        _fallbackCookieProvider = fallbackCookieProvider;
    }

    public static QzoneContext BuildContext(string cookiesStr)
    {
        if (string.IsNullOrWhiteSpace(cookiesStr))
            throw new Exception("未提供 Cookie，请启用自动刷新或在插件配置中填写 Cookie 字符串");

        var c = new Dictionary<string, string>();
        foreach (var part in cookiesStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            c[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }

        var uinStr = c.GetValueOrDefault("uin") ?? c.GetValueOrDefault("p_uin") ?? "";
        if (!uinStr.StartsWith("o"))
            throw new Exception("Cookie 中缺少合法 uin");
        var uin = long.Parse(uinStr[1..]);

        return new QzoneContext(
            uin,
            c.GetValueOrDefault("skey") ?? "",
            c.GetValueOrDefault("p_skey") ?? "",
            c);
    }

    /// <summary>获取当前上下文；尚未初始化时用手动配置的后备 Cookie 构建（不发起网络请求）。</summary>
    public QzoneContext GetCtx()
    {
        lock (_lock)
        {
            _ctx ??= BuildContext(_fallbackCookieProvider());
            return _ctx;
        }
    }

    /// <summary>原地更新 Cookie（刷新凭证时调用，不重建下游对象）。</summary>
    public QzoneContext UpdateCookies(string cookiesStr)
    {
        lock (_lock)
        {
            _ctx = BuildContext(cookiesStr);
            return _ctx;
        }
    }

    /// <summary>用后备配置中的 Cookie 重建上下文（手动模式兼容重试：部分 -3000 为瞬时错误）。</summary>
    public QzoneContext Relogin()
    {
        lock (_lock)
        {
            _ctx = BuildContext(_fallbackCookieProvider());
            return _ctx;
        }
    }

    public bool HasContext
    {
        get { lock (_lock) return _ctx != null; }
    }

    public long Uin => HasContext ? GetCtx().Uin : 0;
}
