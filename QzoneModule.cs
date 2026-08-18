using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging;

namespace Alife.Demo.Plugin.Qzone;

public class QzoneConfig
{
    [DisplayName("Cookie字符串")]
    [Description("QQ空间登录Cookie，格式如 uin=o123; skey=xxx; p_skey=yyy")]
    public string CookiesStr { get; set; } = "";

    [DisplayName("主人QQ号")]
    [Description("允许执行敏感操作的主人QQ号，多个用逗号分隔")]
    public string MasterIds { get; set; } = "";

    [DisplayName("启用主人检查")]
    [Description("敏感操作是否仅限主人")]
    public bool MasterCheckEnabled { get; set; } = true;

    [DisplayName("访客显示上限")]
    [Description("访客列表最多显示条数")]
    public int VisitorLimit { get; set; } = 20;

    [DisplayName("点赞用户显示上限")]
    [Description("说说点赞用户最多显示人数")]
    public int LikeUsersDisplayMax { get; set; } = 5;

    [DisplayName("评论后自动点赞")]
    [Description("评论后是否自动点赞")]
    public bool LikeWhenComment { get; set; } = true;

    [DisplayName("自动点赞延迟(秒)")]
    [Description("评论后自动点赞的随机延迟下限")]
    public double LikeDelayMin { get; set; } = 0.5;

    [DisplayName("自动点赞延迟抖动(秒)")]
    [Description("评论后自动点赞的随机延迟抖动范围")]
    public double LikeDelayJitter { get; set; } = 1.0;

    [DisplayName("写操作节流间隔(秒)")]
    [Description("点赞/评论等写操作的最小间隔")]
    public double WriteThrottleSeconds { get; set; } = 1.0;

    [DisplayName("请求超时(秒)")]
    [Description("HTTP请求超时时间")]
    public int Timeout { get; set; } = 30;
}

[Module("QQ空间",
    "提供QQ空间说说发布、查看、点赞、评论、删除、访客统计等功能",
    defaultCategory: "Alife 官方/社交平台")]
public class QzoneModule(
    XmlFunctionCaller functionCaller,
    ILogger<QzoneModule> logger,
    Interactor<QzoneModule> interactor,
    QChatService qChatService) :
    ChatBehaviour,
    IConfigurable<QzoneConfig>
{
    public QzoneConfig Configuration { get; set; } = null!;

    private QzoneApi? _api;
    private QzoneContext? _ctx;
    private long _myUin;
    private DateTime _lastWriteTime = DateTime.MinValue;
    private readonly HashSet<string> _masterIds = new();

    private bool IsMaster(string? userId)
    {
        if (!Configuration.MasterCheckEnabled) return true;
        if (string.IsNullOrEmpty(userId)) return true;
        return _masterIds.Contains(userId);
    }

    private async Task EnsureApiAsync()
    {
        if (_api != null) return;
        if (string.IsNullOrEmpty(Configuration.CookiesStr))
            throw new Exception("未配置QQ空间Cookie，请在插件配置中填写cookies_str");

        _ctx = QzoneSession.BuildContext(Configuration.CookiesStr);
        _myUin = _ctx.Uin;
        _api = new QzoneApi(_ctx, Configuration.Timeout);
        logger.LogInformation("QQ空间登录成功 uin={Uin}", _myUin);
        await Task.CompletedTask;
    }

    private async Task ThrottleWriteAsync()
    {
        var elapsed = DateTime.Now - _lastWriteTime;
        if (elapsed.TotalSeconds < Configuration.WriteThrottleSeconds)
        {
            await Task.Delay(TimeSpan.FromSeconds(Configuration.WriteThrottleSeconds) - elapsed);
        }
        _lastWriteTime = DateTime.Now;
    }

    private static string NormalizeTid(string tid)
    {
        var s = tid?.Trim() ?? "";
        if (s.Contains("/mood/"))
            s = s.Split("/mood/", 2)[1];
        var idx = s.LastIndexOf('.');
        if (idx > 0 && s[(idx + 1)..].All(char.IsDigit))
            s = s[..idx];
        return s;
    }

    private static string FormatCommentLine(QzoneComment cmt, string label, string indent, string timeStr)
    {
        var (target, content) = ParseCommentContent(cmt.Content);
        var relation = string.IsNullOrEmpty(target) ? "" : $" 回复 {target}";
        var cid = string.IsNullOrEmpty(cmt.CommentId) ? cmt.Tid.ToString() : cmt.CommentId;
        return $"{indent}└ [{label} ID:{cid} UIN:{cmt.Uin}] {cmt.Nickname}{relation} [{timeStr}]: {content}";
    }

    private static (string Target, string Content) ParseCommentContent(string content)
    {
        var text = content ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(text, @"\s*@\{uin:(\d+),nick:([^,}]+)[^}]*\}\s*");
        if (!m.Success) return ("", text);
        return ($"{m.Groups[2].Value}(UIN:{m.Groups[1].Value})", text[m.Length..]);
    }

    private QzoneComment? MatchComment(List<QzoneComment> comments, string commentId, string commentUin = "")
    {
        var matches = comments.Where(c =>
            c.CommentId == commentId || c.Tid.ToString() == commentId).ToList();
        if (!string.IsNullOrEmpty(commentUin))
            matches = matches.Where(c => c.Uin.ToString() == commentUin).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private QzoneComment? FindRootComment(List<QzoneComment> comments, QzoneComment target)
    {
        if (target.ParentTid == null) return target;
        return comments.FirstOrDefault(c => c.Tid == target.ParentTid) ?? target;
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("发布一条说说到自己的QQ空间。配图方式：1) images参数传图片URL或本地路径；2) image_indices引用[近期图片]清单中的序号。都不传时默认纯文字发布。")]
    public async Task QzonePublish(
        [Description("说说内容")] string text,
        [Description("图片URL或本地路径列表(可选)")] string? images = null,
        [Description("图片序号列表(从1开始，可选)")] string? imageIndices = null)
    {
        try
        {
            await EnsureApiAsync();
            var imgList = string.IsNullOrEmpty(images)
                ? new List<string>()
                : images.Split(',').Where(s => !string.IsNullOrWhiteSpace(s) && !s.Contains("example.com")).ToList();
            var result = await _api!.PublishAsync(text, imgList, allowImageDrop: false);
            interactor.Poke(result.Ok ? $"发布成功 tid={result.Data.GetValueOrDefault("tid")}" : $"发布失败：{result.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"发布失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看QQ空间说说。不提供target_id默认查看自己的空间；要查看好友动态请提供好友QQ号。返回每条说说的ID、发布时间、配图数量和最新评论。")]
    public async Task QzoneView(
        [Description("目标QQ号(可选)")] string? targetId = null,
        [Description("查看条数，默认1")] int num = 1)
    {
        try
        {
            await EnsureApiAsync();
            var target = string.IsNullOrEmpty(targetId) ? _myUin.ToString() : targetId;
            var resp = await _api!.GetMsgListAsync(long.Parse(target), num);
            if (!resp.Ok)
            {
                interactor.Poke($"查看失败：{resp.Message}");
                return;
            }
            var msgList = resp.Data.GetValueOrDefault("msglist") as List<object?> ?? new();
            var posts = QzoneParser.ParseFeeds(msgList);
            if (posts.Count == 0)
            {
                interactor.Poke("没有找到说说");
                return;
            }

            var lines = new List<string>();
            foreach (var p in posts)
            {
                var timeStr = QzoneParser.FormatTime(p.CreateTime);
                var line = $"【{p.Name}】(ID:{p.Tid}) [{timeStr}]: {p.Text}";
                if (p.Images.Count > 0)
                    line += $"\n配图x{p.Images.Count}";

                // 拉详情获取评论
                try
                {
                    var detailResp = await _api!.GetDetailAsync(p.Uin, p.Tid);
                    if (detailResp.Ok)
                    {
                        var detailPosts = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                        if (detailPosts.Count > 0 && detailPosts[0].Comments.Count > 0)
                            p.Comments = detailPosts[0].Comments;
                    }
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "拉取详情评论失败");
                }

                // 拉点赞列表
                try
                {
                    var likeResp = await _api!.GetLikeListAsync(p);
                    if (likeResp.Ok)
                    {
                        var likeData = likeResp.Data;
                        var likeUsers = new List<string>();
                        if (likeData.GetValueOrDefault("like_uin_info") is List<object?> uinList)
                        {
                            foreach (var u in uinList)
                            {
                                if (u is Dictionary<string, object?> uinDict)
                                {
                                    var nick = uinDict.GetValueOrDefault("nick")?.ToString() ?? uinDict.GetValueOrDefault("fuin")?.ToString();
                                    if (!string.IsNullOrEmpty(nick)) likeUsers.Add(nick);
                                }
                            }
                        }
                        var total = Convert.ToInt32(likeData.GetValueOrDefault("total_number") ?? likeUsers.Count);
                        if (total > 0 || likeUsers.Count > 0)
                        {
                            p.LikeCount = total;
                            p.LikeUsers = likeUsers;
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "拉取点赞列表失败");
                }

                lines.Add(line);
                if (p.Comments.Count > 0)
                {
                    var shown = p.Comments.Take(3).ToList();
                    foreach (var cmt in shown)
                    {
                        var timeStr2 = QzoneParser.FormatTime(cmt.CreateTime);
                        lines.Add(FormatCommentLine(cmt, "评", "  ", timeStr2));
                    }
                    if (p.Comments.Count > shown.Count)
                        lines.Add($"  ...等{p.Comments.Count}条评论");
                }
                if (p.LikeCount > 0)
                {
                    var likeStr = string.Join("、", p.LikeUsers.Take(Configuration.LikeUsersDisplayMax));
                    if (p.LikeUsers.Count > Configuration.LikeUsersDisplayMax)
                        likeStr += $"等{p.LikeCount}人";
                    lines.Add($"  👍 {likeStr}");
                }
            }
            interactor.Poke(string.Join("\n", lines));
        }
        catch (Exception e)
        {
            interactor.Poke($"查看失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("点赞或取消点赞QQ空间说说")]
    public async Task QzoneLike(
        [Description("说说ID")] string tid,
        [Description("目标QQ号(可选，默认自己)")] string? targetId = null,
        [Description("是否取消点赞")] bool unlike = false)
    {
        try
        {
            await EnsureApiAsync();
            var target = string.IsNullOrEmpty(targetId) ? _myUin.ToString() : targetId;
            var post = new QzonePost(long.Parse(target), NormalizeTid(tid));
            var resp = await _api!.LikeAsync(post, 0, unlike);
            interactor.Poke(resp.Ok ? (unlike ? "取消点赞成功" : "点赞成功") : $"操作失败：{resp.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"操作失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("评论QQ空间说说")]
    public async Task QzoneComment(
        [Description("说说ID")] string tid,
        [Description("评论内容")] string content,
        [Description("目标QQ号(可选，默认自己)")] string? targetId = null)
    {
        try
        {
            await EnsureApiAsync();
            var target = string.IsNullOrEmpty(targetId) ? _myUin.ToString() : targetId;
            var resp = await _api!.CommentAsync(long.Parse(target), NormalizeTid(tid), content);
            if (!resp.Ok)
            {
                interactor.Poke($"评论失败：{resp.Message}");
                return;
            }
            var msg = "评论成功";
            if (Configuration.LikeWhenComment)
            {
                try
                {
                    await ThrottleWriteAsync();
                    var delay = TimeSpan.FromSeconds(Configuration.LikeDelayMin + Random.Shared.NextDouble() * Configuration.LikeDelayJitter);
                    await Task.Delay(delay);
                    var likeResp = await _api!.LikeAsync(new QzonePost(long.Parse(target), NormalizeTid(tid)), 0);
                    if (likeResp.Ok) msg += "，已自动点赞";
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "评论后自动点赞失败");
                }
            }
            interactor.Poke(msg);
        }
        catch (Exception e)
        {
            interactor.Poke($"评论失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除QQ空间说说")]
    public async Task QzoneDelete(
        [Description("说说ID")] string tid)
    {
        try
        {
            await EnsureApiAsync();
            var resp = await _api!.DeleteAsync(NormalizeTid(tid));
            interactor.Poke(resp.Ok ? "删除成功" : $"删除失败：{resp.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"删除失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看QQ空间访客记录")]
    public async Task QzoneVisitors()
    {
        try
        {
            await EnsureApiAsync();
            var resp = await _api!.GetVisitorsAsync(Configuration.VisitorLimit);
            if (!resp.Ok)
            {
                interactor.Poke($"获取访客失败：{resp.Message}");
                return;
            }
            var visitors = resp.Data.GetValueOrDefault("visitors") as List<object?> ?? new();
            if (visitors.Count == 0)
            {
                interactor.Poke("暂无访客记录");
                return;
            }
            var lines = visitors.Select(v =>
            {
                if (v is not Dictionary<string, object?> d) return "";
                var nick = d.GetValueOrDefault("nick")?.ToString() ?? d.GetValueOrDefault("name")?.ToString() ?? "未知";
                var time = Convert.ToInt64(d.GetValueOrDefault("visit_time") ?? 0);
                return $"{nick} [{QzoneParser.FormatTime(time)}]";
            }).Where(s => !string.IsNullOrEmpty(s));
            interactor.Poke($"最近{visitors.Count}位访客：\n" + string.Join("\n", lines));
        }
        catch (Exception e)
        {
            interactor.Poke($"获取访客失败：{e.Message}");
        }
    }
}