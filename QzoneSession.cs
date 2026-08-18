using System;
using System.Collections.Generic;
using System.Linq;

namespace AinaLife.Qzone;

/// <summary>QQ登录上下文构建器</summary>
public static class QzoneSession
{
    /// <summary>从Cookie字符串构建上下文</summary>
    public static QzoneContext BuildContext(string cookiesStr)
    {
        if (string.IsNullOrEmpty(cookiesStr))
            throw new Exception("未提供Cookie，请在插件配置中填写cookies_str");

        var c = new Dictionary<string, string>();
        foreach (var part in cookiesStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            c[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }

        var uinStr = c.GetValueOrDefault("uin") ?? c.GetValueOrDefault("p_uin") ?? "";
        if (!uinStr.StartsWith("o"))
            throw new Exception("Cookie中缺少合法uin");
        var uin = long.Parse(uinStr[1..]);

        return new QzoneContext(
            uin,
            c.GetValueOrDefault("skey") ?? "",
            c.GetValueOrDefault("p_skey") ?? "",
            c);
    }
}
