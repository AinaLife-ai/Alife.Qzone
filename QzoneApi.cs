using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AinaLife.Qzone;

/// <summary>
/// QQ空间HTTP API封装（完整移植自 KiraAI_qzone_plugin qzone/api.py）。
/// 持有可变 Session 与传输层，Cookie 原地刷新后所有接口自动用上新凭证。
/// </summary>
public class QzoneApi
{
    private const string BaseUrl = "https://user.qzone.qq.com";
    private const string UploadImageUrl = "https://up.qzone.qq.com/cgi-bin/upload/cgi_upload_image";
    private const string EmotionUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_publish_v6";
    private const string DolikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_dolike_app";
    private const string DolikeUnlikeUrl = "https://user.qzone.qq.com/proxy/domain/w.qzone.qq.com/cgi-bin/likes/internal_unlike_app";
    private const string LikeListUrl = "https://user.qzone.qq.com/proxy/domain/users.qzone.qq.com/cgi-bin/likes/get_like_list_app";
    private const string PersonalCardUrl = "https://user.qzone.qq.com/proxy/domain/r.qzone.qq.com/cgi-bin/user/cgi_personal_card";
    private const string ListUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msglist_v6";
    private const string CommentUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    private const string CommentH5Url = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    private const string ZoneListUrl = "https://user.qzone.qq.com/proxy/domain/ic2.qzone.qq.com/cgi-bin/feeds/feeds3_html_more";
    private const string VisitorUrl = "https://h5.qzone.qq.com/proxy/domain/g.qzone.qq.com/cgi-bin/friendshow/cgi_get_visitor_more";
    private const string ReplyUrl = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_re_feeds";
    private const string DeleteUrl = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delete_v6";
    private const string DeleteCommentH5Url = "https://h5.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_delcomment_ugc";
    private const string DetailUrl = "https://h5.qzone.qq.com/proxy/domain/taotao.qq.com/cgi-bin/emotion_cgi_msgdetail_v6";
    private const string DetailPcUrl = "https://user.qzone.qq.com/proxy/domain/taotao.qzone.qq.com/cgi-bin/emotion_cgi_getdetailv6";
    private const string DetailMobileUrl = "https://mobile.qzone.qq.com/detail";
    private const string MobileUa = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    private readonly QzoneHttpClient _client;
    private readonly QzoneSession _session;

    public QzoneApi(QzoneSession session, QzoneHttpClient client)
    {
        _session = session;
        _client = client;
    }

    private QzoneContext Ctx => _session.GetCtx();

    private static bool IsZero(object? v)
    {
        if (v == null) return false;
        try { return Convert.ToInt64(v) == 0; } catch { return false; }
    }

    /// <summary>上传单张图片（本接口较为脆弱，给足 60s 超时）</summary>
    public async Task<ApiResponse> UploadImageAsync(byte[] image, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["filename"] = "filename",
            ["uploadtype"] = "1",
            ["albumtype"] = "7",
            ["exttype"] = "0",
            ["refer"] = "shuoshuo",
            ["skey"] = Ctx.Skey,
            ["uin"] = Ctx.Uin.ToString(),
            ["p_uin"] = Ctx.Uin.ToString(),
            ["zzpaneluin"] = Ctx.Uin.ToString(),
            ["zzpanelkey"] = "",
            ["p_skey"] = Ctx.PSkey,
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
        var raw = await _client.RequestAsync(HttpMethod.Post, UploadImageUrl,
            query: new() { ["g_tk"] = Ctx.Gtk2 },
            form: form,
            headers: new() { ["referer"] = $"{BaseUrl}/{Ctx.Uin}", ["origin"] = BaseUrl },
            timeoutSeconds: 60,
            ct: ct);
        return ApiResponse.FromRaw(raw, codeKey: "ret", msgKeys: new[] { "msg" });
    }

    /// <summary>发布说说。allowImageDrop=true 时配图全部获取失败降级为纯文字发布而不是整个失败。</summary>
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
            ["hostuin"] = Ctx.Uin.ToString(),
            ["code_version"] = "1",
            ["format"] = "json",
            ["qzreferrer"] = $"{BaseUrl}/{Ctx.Uin}",
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
                    downloadErrors = new List<string> { "配图获取失败（链接可能已过期），已降级为纯文字" };
                else
                    throw new Exception($"所有图片均获取失败（共 {images.Count} 张）: {string.Join("; ", downloadErrors)}");
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

        var raw = await _client.RequestAsync(HttpMethod.Post, EmotionUrl,
            query: new() { ["g_tk"] = Ctx.Gtk2, ["uin"] = Ctx.Uin.ToString() },
            form: data, ct: ct);
        var resp = ApiResponse.FromRaw(raw);
        if (resp.Ok && downloadErrors.Count > 0)
            return new ApiResponse(true, resp.Code, string.Join("; ", downloadErrors), resp.Data, resp.Raw);
        return resp;
    }

    /// <summary>获取指定QQ号说说列表</summary>
    public async Task<ApiResponse> GetMsgListAsync(long targetUin, int num = 10, string? pos = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>
        {
            ["g_tk"] = Ctx.Gtk2,
            ["uin"] = targetUin.ToString(),
            ["ftype"] = "0",
            ["sort"] = "0",
            ["pos"] = pos ?? "",
            ["num"] = num.ToString(),
            ["replynum"] = "100",
            ["callback"] = "_preloadCallback",
            ["code_version"] = "1",
            ["format"] = "json",
            ["need_comment"] = "1",
            ["need_private_comment"] = "1",
        };
        var raw = await _client.RequestAsync(HttpMethod.Get, ListUrl, query: query, ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取说说详情（h5 → pc → mobile 三路回退，pc/mobile 空响应只试 1 次快速兜底）</summary>
    public async Task<ApiResponse> GetDetailAsync(long uin, string tid, CancellationToken ct = default)
    {
        var errors = new List<string>();
        // 方法1: h5 msgdetail_v6（实测唯一稳定路径）
        try
        {
            var raw = await _client.RequestAsync(HttpMethod.Get, DetailUrl,
                query: new() { ["uin"] = uin.ToString(), ["tid"] = tid, ["format"] = "jsonp", ["g_tk"] = Ctx.Gtk2 }, ct: ct);
            if (IsZero(raw.GetValueOrDefault("code")) || raw.ContainsKey("msglist") || raw.ContainsKey("data"))
                return ApiResponse.FromRaw(raw);
            errors.Add($"h5: {raw.GetValueOrDefault("message") ?? raw.GetValueOrDefault("msg")}");
        }
        catch (Exception e) { errors.Add($"h5: {e.Message}"); }
        // 方法2: PC getdetailv6
        try
        {
            var raw = await _client.RequestAsync(HttpMethod.Post, DetailPcUrl,
                query: new() { ["g_tk"] = Ctx.Gtk2 },
                form: new() { ["uin"] = uin.ToString(), ["tid"] = tid, ["format"] = "json", ["hostuin"] = Ctx.Uin.ToString(), ["qzreferrer"] = $"{BaseUrl}/{Ctx.Uin}/main" },
                headers: new() { ["Content-Type"] = "application/x-www-form-urlencoded;charset=UTF-8", ["Referer"] = $"{BaseUrl}/{uin}", ["Origin"] = BaseUrl },
                emptyRetryLimit: 1,
                ct: ct);
            if (IsZero(raw.GetValueOrDefault("code")) || IsZero(raw.GetValueOrDefault("ret")) || raw.ContainsKey("msglist") || raw.ContainsKey("data"))
                return ApiResponse.FromRaw(raw);
            errors.Add($"pc: {raw.GetValueOrDefault("message") ?? raw.GetValueOrDefault("msg")}");
        }
        catch (Exception e) { errors.Add($"pc: {e.Message}"); }
        // 方法3: mobile detail
        try
        {
            var raw = await _client.RequestAsync(HttpMethod.Get, DetailMobileUrl,
                query: new() { ["g_tk"] = Ctx.Gtk2, ["uin"] = uin.ToString(), ["cellid"] = tid, ["format"] = "json" },
                headers: new() { ["User-Agent"] = MobileUa, ["Referer"] = "https://mobile.qzone.qq.com", ["Accept"] = "application/json, text/plain, */*", ["X-Requested-With"] = "XMLHttpRequest" },
                emptyRetryLimit: 1,
                ct: ct);
            if (IsZero(raw.GetValueOrDefault("code")) || raw.ContainsKey("data"))
                return ApiResponse.FromRaw(raw);
            errors.Add($"mobile: {raw.GetValueOrDefault("message") ?? raw.GetValueOrDefault("msg")}");
        }
        catch (Exception e) { errors.Add($"mobile: {e.Message}"); }
        return new ApiResponse(false, -1, string.Join("; ", errors), new(), new());
    }

    /// <summary>
    /// 点赞/取消点赞（精易2024实测格式：unikey带.1后缀，from=-100，face=0。
    /// 旧格式 from=1 / unikey 无 .1 均假成功，不再使用）。
    /// abstime 必须传说说发布时间。
    /// </summary>
    public async Task<ApiResponse> LikeAsync(QzonePost post, long abstime, bool unlike = false, CancellationToken ct = default)
    {
        var url = unlike ? DolikeUnlikeUrl : DolikeUrl;
        var tid = QzoneParser.NormalizeTid(post.Tid);
        var unikey = $"http://user.qzone.qq.com/{post.Uin}/mood/{tid}.1";
        var form = new Dictionary<string, string>
        {
            ["qzreferrer"] = $"https://user.qzone.qq.com/{post.Uin}",
            ["opuin"] = post.Uin.ToString(),
            ["unikey"] = unikey,
            ["curkey"] = unikey,
            ["from"] = "-100",
            ["fupdate"] = "1",
            ["face"] = "0",
            ["format"] = "json",
        };
        if (abstime > 0) form["abstime"] = abstime.ToString();
        var raw = await _client.RequestAsync(HttpMethod.Post, url, query: new() { ["g_tk"] = Ctx.Gtk2 }, form: form, ct: ct);
        if (IsZero(raw.GetValueOrDefault("ret")) || IsZero(raw.GetValueOrDefault("code")))
            raw["code"] = 0L;
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>
    /// 获取点赞列表（点赞人 + 总数 + is_dolike）。
    /// unikey 优先用说说的真实 like key（curlikekey/orglikekey），手拼 mood/{tid}.1 仅作回退。
    /// </summary>
    public async Task<ApiResponse> GetLikeListAsync(QzonePost post, int queryCount = 20, CancellationToken ct = default)
    {
        var tid = QzoneParser.NormalizeTid(post.Tid);
        var likeKey = post.LikeKey;
        var unikey = !string.IsNullOrEmpty(likeKey)
            ? likeKey
            : $"http://user.qzone.qq.com/{post.Uin}/mood/{tid}.1";
        var query = new Dictionary<string, string>
        {
            ["uin"] = Ctx.Uin.ToString(),
            ["unikey"] = unikey,
            ["begin_uin"] = "0",
            ["query_count"] = queryCount.ToString(),
            ["if_first_page"] = "1",
            ["g_tk"] = Ctx.Gtk2,
            ["format"] = "json",
        };
        var raw = await _client.RequestAsync(HttpMethod.Get, LikeListUrl, query: query,
            headers: new() { ["Referer"] = $"{BaseUrl}/{post.Uin}" },
            emptyRetryLimit: 1,
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取用户基本资料（昵称等），用于展示自己昵称（防「我」昵称诈骗）</summary>
    public async Task<ApiResponse> GetUserInfoAsync(long uin, CancellationToken ct = default)
    {
        var raw = await _client.RequestAsync(HttpMethod.Get, PersonalCardUrl,
            query: new() { ["uin"] = uin.ToString(), ["g_tk"] = Ctx.Gtk2 },
            headers: new() { ["Referer"] = $"{BaseUrl}/{uin}" },
            emptyRetryLimit: 1,
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>评论说说（user JSON路径优先，失败回退H5表单路径；ret=0 兼容防误判后 H5 双发）</summary>
    public async Task<ApiResponse> CommentAsync(long targetUin, string tid, string content, CancellationToken ct = default)
    {
        var qzreferrer = $"https://user.qzone.qq.com/{targetUin}/main";
        // user 路径
        var rawUser = await _client.RequestAsync(HttpMethod.Post, CommentUrl,
            query: new() { ["g_tk"] = Ctx.Gtk2 },
            form: new() { ["hostUin"] = targetUin.ToString(), ["topicId"] = $"{targetUin}_{tid}", ["content"] = content, ["format"] = "json", ["qzreferrer"] = qzreferrer },
            ct: ct);
        var userResp = ApiResponse.FromRaw(rawUser);
        if (userResp.Ok || IsZero(rawUser.GetValueOrDefault("ret")))
        {
            if (!userResp.Ok)
                userResp = new ApiResponse(true, 0, null, new Dictionary<string, object?>(rawUser), rawUser);
            return userResp;
        }
        // H5 回退
        var rawH5 = await _client.RequestAsync(HttpMethod.Post, CommentH5Url,
            query: new() { ["g_tk"] = Ctx.Gtk2 },
            form: new()
            {
                ["topicId"] = $"{targetUin}_{tid}__1",
                ["uin"] = Ctx.Uin.ToString(),
                ["hostUin"] = targetUin.ToString(),
                ["feedsType"] = "100",
                ["inCharset"] = "utf-8",
                ["outCharset"] = "utf-8",
                ["plat"] = "qzone",
                ["source"] = "ic",
                ["isSignIn"] = "",
                ["platformid"] = "50",
                ["format"] = "fs",
                ["ref"] = "feeds",
                ["content"] = content,
                ["richval"] = "",
                ["richtype"] = "",
                ["private"] = "0",
                ["paramstr"] = "1",
                ["qzreferrer"] = qzreferrer,
            },
            headers: new() { ["Content-Type"] = "application/x-www-form-urlencoded;charset=UTF-8", ["Referer"] = qzreferrer, ["Origin"] = BaseUrl },
            ct: ct);
        var h5Resp = ApiResponse.FromRaw(rawH5);
        if (h5Resp.Ok || IsZero(rawH5.GetValueOrDefault("ret")))
        {
            if (!h5Resp.Ok)
                h5Resp = new ApiResponse(true, 0, null, new Dictionary<string, object?>(rawH5), rawH5);
            return h5Resp;
        }
        return new ApiResponse(false, h5Resp.Code,
            $"user: {userResp.Message ?? userResp.Code.ToString()}; h5: {h5Resp.Message ?? h5Resp.Code.ToString()}",
            new(), new());
    }

    /// <summary>回复评论（API锚定主评论，楼中回复写入QQ原生关系标记 @{uin,nick,who:1,auto:1}）</summary>
    public async Task<ApiResponse> ReplyAsync(QzonePost post, QzoneComment comment, string content, QzoneComment? rootComment = null, CancellationToken ct = default)
    {
        var root = rootComment ?? comment;
        if (comment.ParentTid != null && root.Tid != comment.ParentTid)
            throw new Exception($"楼中回复所属主评论不匹配: target={comment.Tid} parent={comment.ParentTid} root={root.Tid}");

        var replyContent = content;
        if (comment.ParentTid != null)
            replyContent = $"@{{uin:{comment.Uin},nick:{comment.Nickname},who:1,auto:1}}{content}";

        var raw = await _client.RequestAsync(HttpMethod.Post, ReplyUrl,
            query: new() { ["g_tk"] = Ctx.Gtk2 },
            form: new()
            {
                ["topicId"] = $"{post.Uin}_{post.Tid}__1",
                ["uin"] = Ctx.Uin.ToString(),
                ["hostUin"] = post.Uin.ToString(),
                ["feedsType"] = "100",
                ["inCharset"] = "utf-8",
                ["outCharset"] = "utf-8",
                ["plat"] = "qzone",
                ["source"] = "ic",
                ["platformid"] = "52",
                ["format"] = "fs",
                ["ref"] = "feeds",
                ["content"] = replyContent,
                ["commentId"] = root.Tid.ToString(),
                ["commentUin"] = root.Uin.ToString(),
                ["richval"] = "",
                ["richtype"] = "",
                ["private"] = "0",
                ["paramstr"] = "2",
                ["qzreferrer"] = $"{BaseUrl}/{Ctx.Uin}/main",
            },
            headers: new()
            {
                ["Content-Type"] = "application/x-www-form-urlencoded;charset=UTF-8",
                ["Sec-Fetch-Dest"] = "empty",
                ["Sec-Fetch-Mode"] = "cors",
                ["Sec-Fetch-Site"] = "same-site",
                ["TE"] = "trailers",
                ["Referer"] = $"{BaseUrl}/",
                ["Origin"] = BaseUrl,
            },
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>删除说说</summary>
    public async Task<ApiResponse> DeleteAsync(string tid, CancellationToken ct = default)
    {
        var raw = await _client.RequestAsync(HttpMethod.Post, DeleteUrl,
            query: new() { ["g_tk"] = Ctx.Gtk2 },
            form: new()
            {
                ["uin"] = Ctx.Uin.ToString(),
                ["topicId"] = $"{Ctx.Uin}_{tid}__1",
                ["feedsType"] = "0",
                ["feedsFlag"] = "0",
                ["feedsKey"] = tid,
                ["feedsAppid"] = "311",
                ["feedsTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ["fupdate"] = "1",
                ["ref"] = "feeds",
                ["qzreferrer"] = $"{BaseUrl}/proxy/domain/ic2.qzone.qq.com/cgi-bin/feeds/feeds_html_module?g_iframeUser=1&i_uin={Ctx.Uin}&i_login_uin={Ctx.Uin}&mode=4&previewV8=1&style=35&version=8&needDelOpr=true",
            },
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>
    /// 删除评论（唯一路径：h5 代理域 emotion_cgi_delcomment_ugc，双参数变体）。
    /// 成功判定兼容顶层与嵌套 data 里的 ret/code（注意不能用 or -1：code=0 是 falsy 会被吞）。
    /// </summary>
    public async Task<ApiResponse> DeleteCommentAsync(string uin, string tid, string commentId, string commentUin = "", CancellationToken ct = default)
    {
        var topicId = $"{uin}_{tid}";
        var qzreferrer = $"{BaseUrl}/{Ctx.Uin}/main";
        var errors = new List<string>();

        var variants = new[]
        {
            new Dictionary<string, string>
            {
                ["uin"] = Ctx.Uin.ToString(),
                ["hostUin"] = uin,
                ["topicId"] = topicId,
                ["commentId"] = commentId,
                ["inCharset"] = "utf-8",
                ["outCharset"] = "utf-8",
                ["ref"] = "",
                ["hostuin"] = Ctx.Uin.ToString(),
                ["code_version"] = "1",
                ["format"] = "fs",
                ["qzreferrer"] = $"{BaseUrl}/{Ctx.Uin}",
            },
            new Dictionary<string, string>
            {
                ["hostuin"] = Ctx.Uin.ToString(),
                ["uin"] = uin,
                ["tid"] = tid,
                ["comment_id"] = commentId,
                ["format"] = "json",
                ["qzreferrer"] = qzreferrer,
            },
        };

        for (int idx = 0; idx < variants.Length; idx++)
        {
            try
            {
                var raw = await _client.RequestAsync(HttpMethod.Post, DeleteCommentH5Url,
                    query: new() { ["g_tk"] = Ctx.Gtk2 },
                    form: variants[idx],
                    headers: new() { ["Content-Type"] = "application/x-www-form-urlencoded;charset=UTF-8", ["Referer"] = "https://h5.qzone.qq.com/", ["Origin"] = "https://h5.qzone.qq.com", ["Accept"] = "*/*" },
                    emptyRetryLimit: 1,
                    ct: ct);
                if (IsZero(raw.GetValueOrDefault("ret")) || IsZero(raw.GetValueOrDefault("code")) ||
                    (raw.GetValueOrDefault("data") is Dictionary<string, object?> dd && (IsZero(dd.GetValueOrDefault("ret")) || IsZero(dd.GetValueOrDefault("code")))))
                    return new ApiResponse(true, 0, null, new Dictionary<string, object?>(raw), raw);
                errors.Add($"h5[v{idx}]: code={raw.GetValueOrDefault("code")} ret={raw.GetValueOrDefault("ret")} msg={raw.GetValueOrDefault("msg") ?? raw.GetValueOrDefault("message")}");
            }
            catch (Exception e)
            {
                errors.Add($"h5[v{idx}]: {e.Message}");
            }
        }
        return new ApiResponse(false, -1, string.Join(" | ", errors), new(), new());
    }

    /// <summary>获取最近访客和统计（轻量接口，兼作会话保活探针）</summary>
    public async Task<ApiResponse> GetVisitorAsync(int limit = 20, CancellationToken ct = default)
    {
        var raw = await _client.RequestAsync(HttpMethod.Get, VisitorUrl,
            query: new()
            {
                ["uin"] = Ctx.Uin.ToString(),
                ["mask"] = "7",
                ["g_tk"] = Ctx.Gtk2,
                ["format"] = "json",
                ["page"] = "1",
                ["fupdate"] = "1",
                ["clear"] = "0",
            },
            headers: new() { ["Referer"] = $"{BaseUrl}/{Ctx.Uin}", ["Accept"] = "application/json, text/javascript, */*; q=0.01", ["X-Requested-With"] = "XMLHttpRequest" },
            emptyRetryLimit: 1,
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }

    /// <summary>获取好友最近说说（feeds3_html_more，HTML解析）</summary>
    public async Task<ApiResponse> GetRecentFeedsAsync(int page = 1, CancellationToken ct = default)
    {
        var raw = await _client.RequestAsync(HttpMethod.Get, ZoneListUrl,
            query: new()
            {
                ["uin"] = Ctx.Uin.ToString(),
                ["scope"] = "0",
                ["view"] = "1",
                ["filter"] = "all",
                ["flag"] = "1",
                ["applist"] = "all",
                ["pagenum"] = page.ToString(),
                ["aisortEndTime"] = "0",
                ["aisortOffset"] = "0",
                ["aisortBeginTime"] = "0",
                ["begintime"] = "0",
                ["format"] = "json",
                ["g_tk"] = Ctx.Gtk2,
                ["useutf8"] = "1",
                ["outputhtmlfeed"] = "1",
            },
            ct: ct);
        return ApiResponse.FromRaw(raw);
    }
}
