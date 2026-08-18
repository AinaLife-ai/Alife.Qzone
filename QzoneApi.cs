using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Demo.Plugin.Qzone;

/// <summary>QQ空间HTTP API封装</summary>
public class QzoneApi
{
    private const string BaseUrl = "https://user.qzone.qq.com";
    private const string UploadImageUrl = "https://up.qzone.qq.com/cgi-bin/upload/cgi_upload_image";
    private const string EmotionUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_publish_v6";
    private const string DolikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_dolike_app";
    private const string DolikeUnlikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_unlike_app";
    private const string LikeListUrl = "https://user.qzone.qq.com/proxy/domain/users.qzone.qq.com/cgi-bin/likes/get_like_list_app";
    private const string ListUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6";
    private const string CommentUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    private const string CommentH5Url = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    private const string VisitorUrl = "https://h5.qzone.qq.com/proxy/domain/g.qzone.qq.com/cgi-bin/friendshow/cgi_get_visitor_more";
    private const string DeleteUrl = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delete_v6";
    private const string DeleteCommentH5Url = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delcomment_ugc";
    private const string DetailUrl = "https://h5.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msgdetail_v6";

    private readonly HttpClient _http;
    private readonly QzoneContext _ctx;

    public QzoneApi(QzoneContext ctx, int timeout = 30)
    {
        _ctx = ctx;
        _http = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(timeout)
        };
    }

    private async Task<Dictionary<string, object?>> RequestAsync(
        HttpMethod method,
        string url,
        Dictionary<string, string>? query = null,
        Dictionary<string, string>? form = null,
        Dictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        var fullUrl = url;
        if (query != null && query.Count > 0)
        {
            var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            fullUrl += (url.Contains('?') ? "&" : "?") + qs;
        }

        using var req = new HttpRequestMessage(method, fullUrl);
        foreach (var (k, v) in _ctx.Headers())
            req.Headers.TryAddWithoutValidation(k, v);
        if (headers != null)
        {
            foreach (var (k, v) in headers)
                req.Headers.TryAddWithoutValidation(k, v);
        }
        foreach (var (k, v) in _ctx.Cookies())
            req.Headers.TryAddWithoutValidation("Cookie", $"{k}={v}");

        if (form != null && form.Count > 0)
        {
            req.Content = new FormUrlEncodedContent(form);
        }

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        return QzoneParser.ParseResponse(text);
    }

    /// <summary>上传单张图片</summary>
    public async Task<ApiResponse> UploadImageAsync(byte[] image, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["filename"] = "filename",
            ["uploadtype"] = "1",
            ["albumtype"] = "7",
            ["exttype"] = "0",
            ["refer"] = "shuoshuo",
            ["skey"] = _ctx.Skey,
            ["uin"] = _ctx.Uin.ToString(),
            ["p_uin"] = _ctx.Uin.ToString(),
            ["zzpaneluin"] = _ctx.Uin.ToString(),
            ["zzpanelkey"] = "",
            ["p_skey"] = _ctx.PSkey,
            ["output_type"] = "json",
            ["charset"] = "utf-8",
            ["output_charset"] = "utf-8",
            ["upload_hd"] = "1",
            ["hd_width"] = "2048",
            ["hd_height"] = "10000",
            ["hd_quality"] = "96",
            ["base64"] = "1",
            ["picfile"] = Convert.ToBase64String(image),
        };
        var raw = await RequestAsync(HttpMethod.Post, UploadImageUrl,
            query: new() { ["g_tk"] = _ctx.Gtk2 },
            form: form,
            headers: new() { ["referer"] = $"{BaseUrl}/{_ctx.Uin}", ["origin"] = BaseUrl },
            ct: ct);
        return ApiResponse.FromRaw(raw, codeKey: "ret", msgKeys: new[] { "msg" });
    }

    /// <summary>发布说说</summary>
    public async Task<ApiResponse> PublishAsync(string text, List<string>? images = null, bool allowImageDrop = false, CancellationToken ct = default)
    {
        var data = new Dictionary<string, string>
        {
            ["syn_tweet_verson"] = "1",
            ["paramstr"] = "1",
            ["who"] = "1",
            ["con"] = text,
            ["feedversion"] = "1",
            ["ver"] = "1",
            ["ugc_right"] = "1",
            ["to_sign"] = "0",
            ["hostuin"] = _ctx.Uin.ToString(),
            ["code_version"] = "1",
            ["format"] = "json",
            ["qzreferrer"] = $"{BaseUrl}/{_ctx.Uin}",
        };

        var downloadErrors = new List<string>();
        if (images != null && images.Count > 0)
        {
            var picBos = new List<string>();
            var richVals = new List<string>();
            var imgs = await QzoneParser.NormalizeImagesAsync(images, downloadErrors, ct);
            if (imgs.Count == 0)
            {
                if (allowImageDrop)
                {
                    downloadErrors = new List<string> { "配图获取失败（链接可能已过期），已降级为纯文字" };
                }
                else
                {
                    throw new Exception($"所有图片均获取失败（共 {images.Count} 张）: {string.Join("; ", downloadErrors)}");
                }
            }
            foreach (var img in imgs)
            {
                var uploadResp = await UploadImageAsync(img, ct);
                if (!uploadResp.Ok)
                    throw new Exception($"上传图片失败: {uploadResp.Message}");
                var (picbo, richval) = QzoneParser.ParseUploadResult(uploadResp.Data);
                picBos.Add(picbo);
                richVals.Add(richval);
            }
            if (picBos.Count > 0)
            {
                data["pic_bo"] = string.Join(",", picBos);
                data["richtype"] = "1";
                data["richval"] = string.Join("\t", richVals);
            }
        }

        var raw = await RequestAsync(HttpMethod.Post, EmotionUrl,
            query: new() { ["g_tk"] = _ctx.Gtk2, ["uin"] = _ctx.Uin.ToString() },
            form: data, ct: ct);
        var resp = ApiResponse.FromRaw(raw);
        if (resp.Ok && downloadErrors.Count > 0)
            return new ApiResponse(true, resp.Code, string.Join("; ", downloadErrors), resp.Data, resp.Raw);
        return resp;
    }

    /// <summary>获取说说列表</summary>
    public async Task<ApiResponse> GetMsgListAsync(long targetUin, int num = 10, string? pos = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["g_tk"] = _ctx.Gtk2,
            ["uin"] = targetUin.ToString(),
            ["hostuin"] = targetUin.ToString(),
            ["format"] = "json",
            ["inCharset"] = "utf-8",
            ["outCharset"] = "utf-8",
            ["notice"] = "0",
            ["sort"] = "1",
            ["pos"] = pos ?? "",
            ["num"] = num.ToString(),
            ["page"] = "1",
            ["cgi_host"] = "http://taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6",
        };
        var raw = await RequestAsync(HttpMethod.Get, ListUrl, query: query, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取说说详情</summary>
    public async Task<ApiResponse> GetDetailAsync(long uin, string tid, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["g_tk"] = _ctx.Gtk2,
            ["uin"] = uin.ToString(),
            ["hostuin"] = uin.ToString(),
            ["tid"] = tid,
            ["format"] = "json",
            ["inCharset"] = "utf-8",
            ["outCharset"] = "utf-8",
            ["notice"] = "0",
            ["sort"] = "1",
        };
        var raw = await RequestAsync(HttpMethod.Get, DetailUrl, query: query, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>点赞/取消点赞</summary>
    public async Task<ApiResponse> LikeAsync(QzonePost post, long abstime, bool unlike = false, CancellationToken ct = default)
    {
        var url = unlike ? DolikeUnlikeUrl : DolikeUrl;
        var form = new Dictionary<string, string>
        {
            ["opuin"] = post.Uin.ToString(),
            ["unikey"] = $"http://user.qzone.qq.com/{post.Uin}/mood/{post.Tid}",
            ["curkey"] = $"http://user.qzone.qq.com/{post.Uin}/mood/{post.Tid}",
            ["from"] = "1",
            ["fupdate"] = "1",
            ["format"] = "json",
            ["isSpa"] = "1",
            ["t"] = "1",
            ["g_tk"] = _ctx.Gtk2,
            ["qzonetoken"] = "",
        };
        if (abstime > 0) form["abstime"] = abstime.ToString();
        var raw = await RequestAsync(HttpMethod.Post, url, query: new() { ["g_tk"] = _ctx.Gtk2 }, form: form, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取点赞列表</summary>
    public async Task<ApiResponse> GetLikeListAsync(QzonePost post, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["g_tk"] = _ctx.Gtk2,
            ["unikey"] = $"http://user.qzone.qq.com/{post.Uin}/mood/{post.Tid}",
            ["format"] = "json",
            ["inCharset"] = "utf-8",
            ["outCharset"] = "utf-8",
            ["num"] = "20",
            ["page"] = "1",
        };
        var raw = await RequestAsync(HttpMethod.Get, LikeListUrl, query: query, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>评论说说</summary>
    public async Task<ApiResponse> CommentAsync(long targetUin, string tid, string content, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["uin"] = targetUin.ToString(),
            ["tid"] = tid,
            ["content"] = content,
            ["format"] = "json",
            ["g_tk"] = _ctx.Gtk2,
            ["qzonetoken"] = "",
        };
        var raw = await RequestAsync(HttpMethod.Post, CommentUrl, query: new() { ["g_tk"] = _ctx.Gtk2 }, form: form, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>删除说说</summary>
    public async Task<ApiResponse> DeleteAsync(string tid, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["tid"] = tid,
            ["format"] = "json",
            ["g_tk"] = _ctx.Gtk2,
        };
        var raw = await RequestAsync(HttpMethod.Post, DeleteUrl, query: new() { ["g_tk"] = _ctx.Gtk2 }, form: form, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取访客列表</summary>
    public async Task<ApiResponse> GetVisitorsAsync(int limit = 20, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["g_tk"] = _ctx.Gtk2,
            ["hostUin"] = _ctx.Uin.ToString(),
            ["uin"] = _ctx.Uin.ToString(),
            ["format"] = "json",
            ["inCharset"] = "utf-8",
            ["outCharset"] = "utf-8",
            ["offset"] = "0",
            ["num"] = limit.ToString(),
            ["sort"] = "1",
        };
        var raw = await RequestAsync(HttpMethod.Get, VisitorUrl, query: query, ct: ct);
        return ApiResponse.FromRaw(raw);
    }
}