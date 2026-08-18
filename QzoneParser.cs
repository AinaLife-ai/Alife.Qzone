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

    /// <summary>解析JSON/JSONP/非标准JSON响应</summary>
    public static Dictionary<string, object?> ParseResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new() { ["code"] = -1, ["message"] = "响应内容为空" };

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
                return new() { ["code"] = -1, ["message"] = "响应内容格式异常" };
            jsonStr = text.Substring(start, end - start + 1);
        }

        jsonStr = jsonStr.Replace("undefined", "null").Trim();

        try
        {
            var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new() { ["code"] = -1, ["message"] = "JSON 根节点不是对象" };
            return JsonToDict(doc.RootElement);
        }
        catch (JsonException)
        {
            return new() { ["code"] = -1, ["message"] = "JSON 解析失败" };
        }
    }

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
            isLiked = likedFlag is 1 or true or "1";

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
            var srcKey = srcVal is int i ? i : -1;
            var src = SafeCell(srcMap.GetValueOrDefault(srcKey, $"未知({srcKey})"), 12);
            var statusParts = new List<string>();
            if (v.GetValueOrDefault("yellow") is int yellow && yellow > 0)
                statusParts.Add($"LV{yellow}");
            if (v.GetValueOrDefault("is_hide_visit") is true)
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

    /// <summary>下载图片为字节数组</summary>
    public static async Task<byte[]> DownloadImageAsync(string url, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://user.qzone.qq.com/");
        var bytes = await http.GetByteArrayAsync(url, ct);
        return bytes;
    }

    /// <summary>规范化图片列表，返回字节数组列表（校验图片魔数）</summary>
    public static async Task<List<byte[]>> NormalizeImagesAsync(List<string> images, List<string>? errors = null, CancellationToken ct = default)
    {
        errors ??= new List<string>();
        var result = new List<byte[]>();
        foreach (var img in images)
        {
            try
            {
                if (img.StartsWith("http://") || img.StartsWith("https://"))
                {
                    var data = await DownloadImageAsync(img, ct);
                    if (!LooksLikeImage(data))
                    {
                        errors.Add($"下载内容不是图片（链接可能已过期返回错误页）: {img[..Math.Min(80, img.Length)]}");
                        continue;
                    }
                    result.Add(data);
                }
                else if (File.Exists(img))
                {
                    var data = await File.ReadAllBytesAsync(img, ct);
                    if (!LooksLikeImage(data))
                    {
                        errors.Add($"本地文件内容不是图片: {img}");
                        continue;
                    }
                    result.Add(data);
                }
                else
                {
                    errors.Add($"无法识别的图片: {img}");
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
