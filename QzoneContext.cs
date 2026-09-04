using System;
using System.Collections.Generic;
using System.Linq;

namespace AinaLife.Qzone;

/// <summary>QQ空间请求上下文，封装动态参数</summary>
public class QzoneContext
{
    public long Uin { get; }
    public string Skey { get; }
    public string PSkey { get; }
    private readonly Dictionary<string, string> _cookies;

    public QzoneContext(long uin, string skey, string pSkey, Dictionary<string, string>? cookies = null)
    {
        Uin = uin;
        Skey = skey;
        PSkey = pSkey;
        _cookies = new Dictionary<string, string>(cookies ?? new());
        _cookies["uin"] = _cookies.GetValueOrDefault("uin") ?? $"o{uin}";
        if (!string.IsNullOrEmpty(skey)) _cookies["skey"] = skey;
        if (!string.IsNullOrEmpty(pSkey)) _cookies["p_skey"] = pSkey;
    }

    /// <summary>动态计算gtk2（p_skey优先，缺失回退skey）</summary>
    public string Gtk2 => CalcGtk(string.IsNullOrEmpty(PSkey) ? Skey : PSkey);

    private static string CalcGtk(string key)
    {
        long hash = 5381;
        foreach (char ch in key)
            hash += (hash << 5) + ch;
        return ((hash & 0x7FFFFFFF)).ToString();
    }

    public Dictionary<string, string> Cookies() => new(_cookies);

    public Dictionary<string, string> Headers() => new()
    {
        ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36",
        ["referer"] = $"https://user.qzone.qq.com/{Uin}",
        ["origin"] = "https://user.qzone.qq.com",
        ["Connection"] = "keep-alive",
        // 注意：不设置 Host——由 HttpClient 按请求 URL 自动设置。
        // 若固定为 user.qzone.qq.com，h5/up 等域名的请求会带错误 Host 导致路由失败。
    };
}
