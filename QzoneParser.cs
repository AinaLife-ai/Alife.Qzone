using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Demo.Plugin.Qzone;

/// <summary>QQ空间响应解析器</summary>
public static class QzoneParser
{
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

    /// <summary>解析评论列表</summary>
    public static List<QzoneComment> ParseComments(List<object?> cmtList)
    {
        var comments = new List<QzoneComment>();
        foreach (var item in cmtList)
        {
            if (item is not Dictionary<string, object?> raw) continue;
            var rawTid = raw.GetValueOrDefault("tid")?.ToString() ?? raw.GetValueOrDefault("id")?.ToString() ?? "";
            var commentId = raw.GetValueOrDefault("commentid")?.ToString()
                ?? raw.GetValueOrDefault("comment_id")?.ToString()
                ?? raw.GetValueOrDefault("cid")?.ToString()
                ?? raw.GetValueOrDefault("commentId")?.ToString()
                ?? rawTid;
            int tidInt = 0;
            int.TryParse(rawTid, out tidInt);

            var cmt = new QzoneComment
            {
                Uin = Convert.ToInt64(raw.GetValueOrDefault("uin") ?? 0),
                Nickname = raw.GetValueOrDefault("name")?.ToString() ?? "",
                Content = raw.GetValueOrDefault("content")?.ToString() ?? "",
                CreateTime = Convert.ToInt64(raw.GetValueOrDefault("create_time") ?? raw.GetValueOrDefault("ctime") ?? 0),
                CreateTimeStr = raw.GetValueOrDefault("createTimeStr")?.ToString() ?? "",
                Tid = tidInt,
                CommentId = commentId,
                ParentTid = raw.TryGetValue("parent_tid", out var pt) && pt != null ? Convert.ToInt32(pt) : null,
                SourceName = raw.GetValueOrDefault("source_name")?.ToString() ?? "",
                SourceUrl = raw.GetValueOrDefault("source_url")?.ToString() ?? "",
            };
            comments.Add(cmt);
        }
        return comments;
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

    /// <summary>规范化图片列表，返回字节数组列表</summary>
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
                    result.Add(await DownloadImageAsync(img, ct));
                }
                else if (File.Exists(img))
                {
                    result.Add(await File.ReadAllBytesAsync(img, ct));
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
}