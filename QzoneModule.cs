using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Function.AIModelUtility;
using Alife.Function.FunctionCaller;
using Alife.Function.QChat;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AinaLife.Qzone;

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

    [DisplayName("自动刷新Cookie")]
    [Description("从OneBot自动获取最新Cookie")]
    public bool AutoRefreshCookie { get; set; } = true;

    [DisplayName("Cookie刷新间隔")]
    [Description("Cookie周期刷新间隔，如 2h/30m/7200")]
    public string CookieRefreshInterval { get; set; } = "2h";

    [DisplayName("用即刷节流")]
    [Description("调用空间功能时距上次刷新超过该间隔则顺手刷新，如 10m")]
    public string CookieRefreshOnUse { get; set; } = "10m";

    [DisplayName("自动发布定时")]
    [Description("自动发布说说定时表达式，cron或interval格式，如 */30 * * * * 或 30m")]
    public string AutoPublishSchedule { get; set; } = "";

    [DisplayName("自动评论定时")]
    [Description("自动评论定时表达式")]
    public string AutoCommentSchedule { get; set; } = "";

    [DisplayName("自动回复定时")]
    [Description("自动回复定时表达式")]
    public string AutoReplySchedule { get; set; } = "";

    [DisplayName("启用自动回复")]
    [Description("是否启用自动回复评论")]
    public bool AutoReplyEnabled { get; set; } = false;

    [DisplayName("每轮最大评论数")]
    [Description("每轮自动评论最多条数")]
    public int MaxCommentsPerCycle { get; set; } = 3;

    [DisplayName("每轮最大回复数")]
    [Description("每轮自动回复最多条数")]
    public int MaxRepliesPerCycle { get; set; } = 5;

    [DisplayName("黑名单QQ")]
    [Description("禁止操作的QQ号，逗号分隔")]
    public string QzoneBlacklist { get; set; } = "";

    [DisplayName("白名单QQ")]
    [Description("仅允许操作的QQ号，逗号分隔，空=不限制")]
    public string QzoneWhitelist { get; set; } = "";

    [DisplayName("黑名单时间段")]
    [Description("定时任务黑名单时间段，如 00:00-06:00，多个用逗号分隔")]
    public string BlackoutSchedules { get; set; } = "";

    [DisplayName("图片清单启用")]
    [Description("是否启用近期图片清单注入")]
    public bool ImageManifestEnabled { get; set; } = true;

    [DisplayName("图片清单数量")]
    [Description("注入LLM的近期图片最大数量")]
    public int ImageManifestCount { get; set; } = 5;

    [DisplayName("图片识图启用")]
    [Description("是否启用空间图片内容识别")]
    public bool QzoneImageDescEnabled { get; set; } = true;

    [DisplayName("允许识自己图")]
    [Description("是否允许对自己空间的说说识图")]
    public bool QzoneImageDescOwn { get; set; } = false;

    [DisplayName("自动发布群ID")]
    [Description("自动发布说说的消息来源群号")]
    public string AutoPublishGroupId { get; set; } = "";

    [DisplayName("自动发布用户ID")]
    [Description("自动发布说说的消息来源QQ号")]
    public string AutoPublishUserId { get; set; } = "";

    [DisplayName("自动发布配图概率")]
    [Description("自动发布时配图的概率(0-1)")]
    public double AutoPublishImageProb { get; set; } = 1.0;

    [DisplayName("自动发布配图最少")]
    [Description("自动发布配图最少张数")]
    public int AutoPublishImageMin { get; set; } = 0;

    [DisplayName("自动发布配图最多")]
    [Description("自动发布配图最多张数")]
    public int AutoPublishImageMax { get; set; } = 3;

    [DisplayName("任务群ID")]
    [Description("定时任务指令发送的群号，逗号分隔")]
    public string TaskGroupIds { get; set; } = "";

    [DisplayName("任务私聊ID")]
    [Description("定时任务指令发送的QQ号，逗号分隔")]
    public string TaskPrivateIds { get; set; } = "";

    [DisplayName("任务消息风格")]
    [Description("定时任务消息风格：silent=抑制群回复")]
    public string TaskMessageStyle { get; set; } = "silent";

    [DisplayName("吸附模式")]
    [Description("发说说未指定图片时自动抓最近一张图")]
    public bool AutoAttachRecentImage { get; set; } = false;
}

[Module("QQ空间",
    "提供QQ空间说说发布、查看、点赞、评论、删除、访客统计、定时任务、Cookie自动刷新等功能",
    defaultCategory: "AinaLife/社交平台")]
public class QzoneModule(
    XmlFunctionCaller functionCaller,
    ILogger<QzoneModule> logger,
    Interactor<QzoneModule> interactor,
    QChatService qChatService,
    IVisionModel? visionModel = null) :
    ChatBehaviour,
    IConfigurable<QzoneConfig>
{
    public QzoneConfig Configuration { get; set; } = null!;

    // QChatService 未公开 OneBotClient，通过反射获取（不修改官方代码）
    private OneBotClient? GetClient()
    {
        var field = typeof(QChatService).GetField("oneBotClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(qChatService) as OneBotClient;
    }

    private QzoneApi? _api;
    private QzoneContext? _ctx;
    private long _myUin;
    private DateTime _lastWriteTime = DateTime.MinValue;
    private readonly HashSet<string> _masterIds = new();
    private readonly HashSet<string> _blacklist = new();
    private readonly HashSet<string> _whitelist = new();
    private readonly List<(TimeSpan Start, TimeSpan End)> _blackoutTimes = new();

    // Cookie刷新状态
    private readonly SemaphoreSlim _cookieRefreshLock = new(1, 1);
    private DateTime _lastCookieRefresh = DateTime.MinValue;
    private Timer? _cookieRefreshTimer;
    private Timer? _autoPublishTimer;
    private Timer? _autoCommentTimer;
    private Timer? _autoReplyTimer;
    private bool _initFailed;
    private bool _jobsAdded;

    // 状态持久化
    private readonly HashSet<string> _repliedComments = new();
    private readonly List<string> _myPostsHistory = new();
    private readonly List<Dictionary<string, object>> _publishedImageHistory = new();
    private string _statePath = "";

    // 图片注册表 sid -> entries
    private readonly Dictionary<string, List<ImageEntry>> _imageRegistry = new();
    private readonly Dictionary<string, string> _urlMd5 = new();
    private const int ImageRegistryCap = 20;
    private const int MaxRepliedCache = 1000;
    private const int MaxHistory = 10;

    private class ImageEntry
    {
        public string? Source { get; set; }
        public string? Url { get; set; }
        public string? Sender { get; set; }
        public long Time { get; set; }
        public string? Desc { get; set; }
        public string? MsgId { get; set; }
    }

    private bool IsMaster(string? userId)
    {
        if (!Configuration.MasterCheckEnabled) return true;
        if (string.IsNullOrEmpty(userId)) return true;
        if (userId.StartsWith("system")) return true;
        return _masterIds.Contains(userId);
    }

    private string? TargetBlockReason(string? targetId)
    {
        var target = (targetId ?? "").Trim();
        if (string.IsNullOrEmpty(target)) return null;
        if (_blacklist.Contains(target)) return "该 QQ 已被加入插件黑名单，禁止操作";
        if (_whitelist.Count > 0 && !_whitelist.Contains(target))
        {
            if (_myUin != 0 && target == _myUin.ToString()) return null;
            return "该 QQ 不在插件白名单内，禁止操作";
        }
        return null;
    }

    private bool IsInBlackout()
    {
        if (_blackoutTimes.Count == 0) return false;
        var now = DateTime.Now.TimeOfDay;
        foreach (var (start, end) in _blackoutTimes)
        {
            if (start <= end)
            {
                if (now >= start && now <= end) return true;
            }
            else
            {
                if (now >= start || now <= end) return true;
            }
        }
        return false;
    }

    private static int ParseIntervalSeconds(string? s, string defaultUnit = "m")
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim().ToLower();
        if (s is "0" or "0s" or "0m" or "0h" or "0d") return 0;
        var m = Regex.Match(s, @"^(\d+(?:\.\d+)?)([dhms]?)$");
        if (!m.Success) return 0;
        var val = double.Parse(m.Groups[1].Value);
        var unit = string.IsNullOrEmpty(m.Groups[2].Value) ? defaultUnit : m.Groups[2].Value;
        return unit switch
        {
            "d" => (int)(val * 86400),
            "h" => (int)(val * 3600),
            "m" => (int)(val * 60),
            _ => (int)val
        };
    }

    private static (double Min, double Jitter) ParseDelayRange(string? s, string def = "0.5-1.5s")
    {
        foreach (var cand in new[] { s, def })
        {
            if (string.IsNullOrWhiteSpace(cand)) continue;
            var text = cand.Trim().ToLower();
            if (text.EndsWith("s")) text = text[..^1];
            if (text.Contains('-'))
            {
                var parts = text.Split('-', 2);
                if (double.TryParse(parts[0], out var lo) && double.TryParse(parts[1], out var hi) && lo >= 0 && hi >= lo)
                    return (lo, hi - lo);
            }
        }
        return (0.5, 1.0);
    }

    private static (string Mode, string Expr, int IntervalSeconds, int JitterSeconds)? ParseSchedule(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Contains(' ') || s.Contains('*') || s.Contains('/'))
        {
            try
            {
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5) return ("cron", s, 0, 0);
            }
            catch { }
        }
        var m = Regex.Match(s, @"^(?<interval>\d+(?:\.\d+)?[hm]?)(?:/(?<jitter>\d+(?:\.\d+)?[hm]?))?$");
        if (m.Success)
        {
            int ParseTime(string t)
            {
                if (t.EndsWith("h")) return (int)(double.Parse(t[..^1]) * 3600);
                if (t.EndsWith("m")) return (int)(double.Parse(t[..^1]) * 60);
                return (int)(double.Parse(t) * 60);
            }
            var interval = ParseTime(m.Groups["interval"].Value);
            var jitter = m.Groups["jitter"].Success ? ParseTime(m.Groups["jitter"].Value) : 0;
            if (interval > 0) return ("interval", "", interval, jitter);
        }
        return null;
    }

    private static string FormatTime(long ts)
    {
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm");
        }
        catch { return "未知时间"; }
    }

    private static string NormalizeTid(string tid)
    {
        var s = tid?.Trim() ?? "";
        if (s.Contains("/mood/")) s = s.Split("/mood/", 2)[1];
        var idx = s.LastIndexOf('.');
        if (idx > 0 && s[(idx + 1)..].All(char.IsDigit)) s = s[..idx];
        return s;
    }

    private static (string Target, string Content) ParseCommentContent(string content)
    {
        var text = content ?? "";
        var m = Regex.Match(text, @"\s*@\{uin:(\d+),nick:([^,}]+)[^}]*\}\s*");
        if (!m.Success) return ("", text);
        return ($"{m.Groups[2].Value}(UIN:{m.Groups[1].Value})", text[m.Length..]);
    }

    private static string FormatCommentLine(QzoneComment cmt, string label, string indent, string timeStr)
    {
        var (target, content) = ParseCommentContent(cmt.Content);
        var relation = string.IsNullOrEmpty(target) ? "" : $" 回复 {target}";
        var cid = string.IsNullOrEmpty(cmt.CommentId) ? cmt.Tid.ToString() : cmt.CommentId;
        return $"{indent}└ [{label} ID:{cid} UIN:{cmt.Uin}] {cmt.Nickname}{relation} [{timeStr}]: {content}";
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

    // ==================== 状态持久化 ====================

    private string StatePath
    {
        get
        {
            if (string.IsNullOrEmpty(_statePath))
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "Storage", "PluginData", "Qzone");
                Directory.CreateDirectory(dir);
                _statePath = Path.Combine(dir, "state.json");
            }
            return _statePath;
        }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var json = File.ReadAllText(StatePath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("replied_comments", out var replied))
            {
                foreach (var item in replied.EnumerateArray())
                    _repliedComments.Add(item.GetString() ?? "");
            }
            if (root.TryGetProperty("my_posts_history", out var history))
            {
                foreach (var item in history.EnumerateArray())
                    _myPostsHistory.Add(item.GetString() ?? "");
            }
            if (root.TryGetProperty("published_image_history", out var pubHistory))
            {
                foreach (var item in pubHistory.EnumerateArray())
                {
                    var dict = new Dictionary<string, object>();
                    if (item.TryGetProperty("identity", out var id)) dict["identity"] = id.GetString() ?? "";
                    if (item.TryGetProperty("time", out var t)) dict["time"] = t.GetInt64();
                    _publishedImageHistory.Add(dict);
                }
            }
            logger.LogInformation("已加载持久化状态：历史说说 {HistoryCount} 条，已回复评论 {RepliedCount} 条",
                _myPostsHistory.Count, _repliedComments.Count);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "加载插件状态失败");
        }
    }

    private void SaveState()
    {
        try
        {
            var data = new Dictionary<string, object>
            {
                ["replied_comments"] = _repliedComments.TakeLast(MaxRepliedCache).ToList(),
                ["my_posts_history"] = _myPostsHistory.TakeLast(MaxHistory).ToList(),
                ["published_image_history"] = _publishedImageHistory.TakeLast(ImageRegistryCap).ToList()
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatePath, json, Encoding.UTF8);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "保存插件状态失败");
        }
    }

    // ==================== Cookie管理 ====================

    private async Task<bool> RefreshCookieAsync(bool force = false)
    {
        if (!Configuration.AutoRefreshCookie) return false;
        await _cookieRefreshLock.WaitAsync();
        try
        {
            var now = DateTime.Now;
            var minInterval = force ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10);
            if (now - _lastCookieRefresh < minInterval)
            {
                if (force && _lastCookieRefresh > DateTime.MinValue) return true;
                return false;
            }

            string? newCookie = null;
            try
            {
                var client = GetClient();
                if (client == null) return false;
                var data = await client.CallActionAsync<JsonElement>("get_cookies", new { domain = "user.qzone.qq.com" });
                if (data.TryGetProperty("data", out var d) && d.TryGetProperty("cookies", out var c))
                    newCookie = c.GetString();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "从 OneBot 获取 Cookie 失败，保留现有会话");
                _lastCookieRefresh = now;
                return false;
            }

            if (string.IsNullOrEmpty(newCookie))
            {
                _lastCookieRefresh = now;
                return false;
            }

            try
            {
                Configuration.CookiesStr = newCookie;
                _ctx = QzoneSession.BuildContext(newCookie);
                _myUin = _ctx.Uin;
                _lastCookieRefresh = now;
                _initFailed = false;
                logger.LogInformation("已从 OneBot 获取最新 Cookie 并原地更新会话");
                return true;
            }
            catch (Exception e)
            {
                _lastCookieRefresh = now;
                logger.LogError(e, "应用新 Cookie 失败");
                return false;
            }
        }
        finally
        {
            _cookieRefreshLock.Release();
        }
    }

    private async Task EnsureApiAsync()
    {
        if (_api != null && !_initFailed)
        {
            var onUse = ParseIntervalSeconds(Configuration.CookieRefreshOnUse, "m");
            if (Configuration.AutoRefreshCookie && onUse > 0 && (DateTime.Now - _lastCookieRefresh).TotalSeconds > onUse)
            {
                try { await RefreshCookieAsync(false); } catch { }
            }
            return;
        }

        if (Configuration.AutoRefreshCookie)
        {
            try
            {
                if (await RefreshCookieAsync(false)) return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "刷新 Cookie 异常");
            }
        }

        if (_api == null)
        {
            if (string.IsNullOrEmpty(Configuration.CookiesStr))
                throw new Exception("未配置QQ空间Cookie，请在插件配置中填写cookies_str");
            _ctx = QzoneSession.BuildContext(Configuration.CookiesStr);
            _myUin = _ctx.Uin;
            if (_myUin == 0)
                throw new Exception("Cookie 中未解析到有效 QQ 号（uin），会话不可用");
            _api = new QzoneApi(_ctx, Configuration.Timeout);
            _initFailed = false;
            logger.LogInformation("QQ空间登录成功 uin={Uin}", _myUin);
        }
        else if (_initFailed)
        {
            throw new Exception("QQ空间会话不可用：Cookie 失效且自动刷新失败，请检查 OneBot 连接或手动更新 Cookie");
        }
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

    // ==================== LLM 调用 ====================

    private async Task<string> CallLlmAsync(string prompt, string systemPrompt)
    {
        try
        {
            var thread = new ChatHistoryAgentThread();
            if (!string.IsNullOrEmpty(systemPrompt))
                thread.ChatHistory.AddSystemMessage(systemPrompt);
            thread.ChatHistory.AddUserMessage(prompt);
            var response = await ChatBot.LanguageModel.ChatStreamingAsync(thread);
            return response?.Trim() ?? "";
        }
        catch (Exception e)
        {
            logger.LogError(e, "LLM 调用失败");
            return "";
        }
    }

    // ==================== 发送者识别（从聊天历史解析） ====================

    private string? GetSenderId()
    {
        try
        {
            foreach (var content in ChatBot.ChatHistory.Reverse())
            {
                if (content.Role != AuthorRole.User) continue;
                var text = content.Content ?? "";
                if (text.StartsWith("[来自系统的杂项消息推送]") || text.StartsWith("[消息来源(")) continue;
                // 群聊格式：[群聊消息(群号,群名)] [QQ(昵称)]:内容
                var m = Regex.Match(text, @"\[(\d{5,})(?:\([^\)]*\))?\]");
                if (m.Success) return m.Groups[1].Value;
                // 私聊格式：[私聊消息(QQ)]
                m = Regex.Match(text, @"\[私聊消息\((\d+)\)\]");
                if (m.Success) return m.Groups[1].Value;
                return null;
            }
        }
        catch { }
        return null;
    }

    // ==================== 定时任务 ====================

    private void SetupScheduledJobs()
    {
        if (_jobsAdded) return;

        var publishSched = ParseSchedule(Configuration.AutoPublishSchedule);
        if (publishSched != null)
        {
            _autoPublishTimer = new Timer(_ => _ = AutoPublishJobAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(publishSched.Value.IntervalSeconds > 0 ? publishSched.Value.IntervalSeconds : 3600));
            logger.LogInformation("自动发布定时任务已调度: {Schedule}", Configuration.AutoPublishSchedule);
        }

        var commentSched = ParseSchedule(Configuration.AutoCommentSchedule);
        if (commentSched != null)
        {
            _autoCommentTimer = new Timer(_ => _ = AutoCommentJobAsync(), null,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(commentSched.Value.IntervalSeconds > 0 ? commentSched.Value.IntervalSeconds : 3600));
            logger.LogInformation("自动评论定时任务已调度: {Schedule}", Configuration.AutoCommentSchedule);
        }

        if (Configuration.AutoReplyEnabled)
        {
            var replySched = ParseSchedule(Configuration.AutoReplySchedule);
            if (replySched != null)
            {
                _autoReplyTimer = new Timer(_ => _ = AutoReplyJobAsync(), null,
                    TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(replySched.Value.IntervalSeconds > 0 ? replySched.Value.IntervalSeconds : 3600));
                logger.LogInformation("自动回复定时任务已调度: {Schedule}", Configuration.AutoReplySchedule);
            }
        }

        _jobsAdded = true;
    }

    private async Task AutoPublishJobAsync()
    {
        if (IsInBlackout()) return;
        try
        {
            await EnsureApiAsync();
            var targetImageCount = Random.Shared.Next(Configuration.AutoPublishImageMin, Configuration.AutoPublishImageMax + 1);
            logger.LogInformation("定时自动发布配图目标: target={Target} range={Min}-{Max}",
                targetImageCount, Configuration.AutoPublishImageMin, Configuration.AutoPublishImageMax);

            var taskGroups = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var taskPrivates = Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (taskGroups.Count > 0 || taskPrivates.Count > 0)
            {
                var instruction = "【定时任务】请根据最近聊天发布一条说说，自然一点，不要提及这是定时任务。";
                if (targetImageCount > 0)
                    instruction += $"本次必须选择恰好{targetImageCount}个不同序号；候选不足时选择全部可用候选。配图时用image_indices选择，也可用images传聊天记录里见过的图片URL或本地路径。";
                else
                    instruction += "可按内容自主选择配图；不适合配图时不要配图。配图时用image_indices选择，也可用images传聊天记录里见过的图片URL或本地路径。";
                await SendTaskInstructionAsync(instruction, true);
                return;
            }

            await LegacyAutoPublishAsync(targetImageCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, "自动发布任务失败");
        }
    }

    private async Task AutoCommentJobAsync()
    {
        if (IsInBlackout()) return;
        try
        {
            await EnsureApiAsync();
            var taskGroups = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var taskPrivates = Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (taskGroups.Count > 0 || taskPrivates.Count > 0)
            {
                var instruction = "【评论任务】请对最近的好友（不包括自己）说说进行评论，自然一点和简洁（0-15字内）。严禁内容重复和复读。注意，检查用户昵称来不要评论自己发布的QQ说说，优先没有评论过的内容，该内容时间戳与当前系统时间戳不得超过7天，否则不评论。";
                await SendTaskInstructionAsync(instruction, false);
                return;
            }
            await LegacyAutoCommentAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "自动评论任务失败");
        }
    }

    private async Task AutoReplyJobAsync()
    {
        if (IsInBlackout()) return;
        try
        {
            await EnsureApiAsync();
            var taskGroups = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var taskPrivates = Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (taskGroups.Count > 0 || taskPrivates.Count > 0)
            {
                var instruction = "【回复任务】请回复你最近说说下的新评论，使用qzone_reply_comment和评论自身的ID、UIN准确回复，target_id为自己的QQ号。自然一点和简洁（0-15字内），严禁内容重复和复读。根据评论作者UIN不回复自己，优先没有回复过的用户和新回复，否则不回复。";
                await SendTaskInstructionAsync(instruction, false);
                return;
            }
            await LegacyAutoReplyAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "自动回复任务失败");
        }
    }

    private async Task SendTaskInstructionAsync(string instruction, bool withPlace)
    {
        var targets = new List<(string Type, string Id)>();
        foreach (var gid in Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            targets.Add(("gm", gid));
        foreach (var uid in Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            targets.Add(("dm", uid));
        if (targets.Count == 0) return;

        var (type, id) = targets[Random.Shared.Next(targets.Count)];
        if (withPlace)
        {
            try
            {
                var client = GetClient();
                if (client != null)
                {
                    if (type == "gm")
                    {
                        var info = await client.CallActionAsync<JsonElement>("get_group_info", new { group_id = long.Parse(id) });
                        var name = "";
                        if (info.TryGetProperty("data", out var d) && d.TryGetProperty("group_name", out var n))
                            name = n.GetString() ?? "";
                        instruction += string.IsNullOrEmpty(name) ? $"\n（当前场合：群 {id}）" : $"\n（当前场合：群「{name}」{id}）";
                    }
                    else
                    {
                        var info = await client.CallActionAsync<JsonElement>("get_stranger_info", new { user_id = long.Parse(id) });
                        var name = "";
                        if (info.TryGetProperty("data", out var d) && d.TryGetProperty("nickname", out var n))
                            name = n.GetString() ?? "";
                        instruction += string.IsNullOrEmpty(name) ? $"\n（当前场合：与 {id} 的私聊）" : $"\n（当前场合：与「{name}」{id} 的私聊）";
                    }
                }
            }
            catch { }
        }

        interactor.Poke($"[System 定时任务指令] {instruction}");
        logger.LogInformation("已发送定时任务指令: {Instruction}", instruction[..Math.Min(30, instruction.Length)]);
        await Task.CompletedTask;
    }

    private async Task LegacyAutoPublishAsync(int targetImageCount)
    {
        var sourceId = "";
        var sourceType = "";
        if (!string.IsNullOrWhiteSpace(Configuration.AutoPublishGroupId))
        {
            sourceId = Configuration.AutoPublishGroupId.Trim();
            sourceType = "group";
        }
        else if (!string.IsNullOrWhiteSpace(Configuration.AutoPublishUserId))
        {
            sourceId = Configuration.AutoPublishUserId.Trim();
            sourceType = "private";
        }

        var contextMessages = new List<string>();
        if (!string.IsNullOrEmpty(sourceId))
        {
            try
            {
                contextMessages = await FetchChatHistoryAsync(sourceType, sourceId, 10);
                if (contextMessages.Count > 0)
                    logger.LogInformation("从 {Type} {Id} 获取到 {Count} 条消息作为上下文", sourceType, sourceId, contextMessages.Count);
            }
            catch (Exception e)
            {
                logger.LogError(e, "获取历史失败");
            }
        }

        var systemPrompt = "";
        if (_myPostsHistory.Count > 0)
        {
            var historyStr = string.Join("\n", _myPostsHistory.TakeLast(5).Select(p => $"- {p}"));
            systemPrompt += $"\n\n你最近发布的说说是：\n{historyStr}";
        }

        string prompt;
        if (contextMessages.Count > 0)
        {
            var historyText = string.Join("\n", contextMessages);
            prompt = $"根据以下最近对话，生成一条QQ空间说说（20-50字），要符合你的人设：\n{historyText}";
        }
        else
        {
            prompt = "请生成一条QQ空间说说，内容可以是心情、日常、段子，20-50字";
        }

        var imageUrls = new List<string>();
        var shouldOfferImages = Configuration.AutoPublishImageProb > 0 && Random.Shared.NextDouble() < Configuration.AutoPublishImageProb;
        if (shouldOfferImages)
        {
            try
            {
                var candidates = await FetchRecentImagesAsync(sourceType, sourceId, Math.Max(Configuration.AutoPublishImageMax, targetImageCount));
                candidates = candidates.Where(u => !IsRecentlyPublishedImage(u)).ToList();
                if (candidates.Count > 0)
                {
                    var descLines = new List<string>();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var desc = await DescribeImageUrlAsync(candidates[i]);
                        descLines.Add($"{i + 1}. {desc}");
                    }
                    if (descLines.Count > 0)
                    {
                        var choiceRule = targetImageCount > 0
                            ? $"本次必须选择恰好{targetImageCount}个不同序号；候选不足时选择全部可用候选。"
                            : $"可按内容自主选择0至{Configuration.AutoPublishImageMax}个不同序号；不适合配图时不要输出 IMG 行。";
                        prompt += "\n\n以下是最近聊天中出现的图片及内容描述：\n" + string.Join("\n", descLines) + "\n" + choiceRule + "正文后另起一行输出 IMG:序号 或 IMG:序号,序号。";
                        var textWithChoice = await CallLlmAsync(prompt, systemPrompt);
                        var (text, chosen) = SplitImgChoices(textWithChoice);
                        imageUrls = ResolveDescribedSources(candidates, chosen, targetImageCount, Configuration.AutoPublishImageMax);
                        if (!string.IsNullOrEmpty(text)) prompt = text;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "自动发布配图候选获取失败");
            }
        }

        var finalText = await CallLlmAsync(prompt, systemPrompt);
        if (string.IsNullOrEmpty(finalText))
        {
            logger.LogWarning("LLM生成内容为空，跳过自动发布");
            return;
        }

        var result = await _api!.PublishAsync(finalText, imageUrls, allowImageDrop: true);
        if (result.Ok)
        {
            _myPostsHistory.Add(finalText);
            SaveState();
            logger.LogInformation("自动发布说说成功: {Text} (图片数: {Count})", finalText, imageUrls.Count);
        }
        else
        {
            logger.LogWarning("自动发布说说失败: {Message}", result.Message);
        }
    }

    private async Task LegacyAutoCommentAsync()
    {
        try
        {
            var posts = await GetFeedsAsync(null, 20);
            if (posts.Count == 0) return;
            posts = posts.Where(p => _myUin == 0 || p.Uin != _myUin).ToList();
            posts = posts.Where(p => TargetBlockReason(p.Uin.ToString()) == null).ToList();
            if (posts.Count == 0) return;

            var selected = posts.OrderBy(_ => Random.Shared.Next()).Take(Math.Min(Configuration.MaxCommentsPerCycle, posts.Count)).ToList();
            foreach (var post in selected)
            {
                var prompt = $"根据以下说说内容，生成一条简洁评论（0-15字）：\n{post.Text}";
                var commentText = await CallLlmAsync(prompt, "");
                if (string.IsNullOrEmpty(commentText)) continue;
                try
                {
                    var result = await CommentAsync(post, commentText);
                    logger.LogInformation("自动评论成功: {Tid} -> {Text}", post.Tid, commentText);
                    if (Configuration.LikeWhenComment && !result.Contains("未重复提交"))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * Configuration.LikeDelayJitter + Configuration.LikeDelayMin));
                        var likeResp = await _api!.LikeAsync(post, post.CreateTime);
                        if (likeResp.Ok) logger.LogInformation("自动点赞成功: {Tid}", post.Tid);
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "自动评论失败: {Tid}", post.Tid);
                    continue;
                }
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * 1.0 + 0.5));
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "自动评论任务失败");
        }
    }

    private async Task LegacyAutoReplyAsync()
    {
        try
        {
            if (_myUin == 0) return;
            var posts = await GetFeedsAsync(_myUin.ToString(), 10);
            if (posts.Count == 0) return;

            var newReplies = 0;
            foreach (var post in posts)
            {
                var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
                if (!detailResp.Ok) continue;
                var parsedPosts = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                if (parsedPosts.Count == 0) continue;
                var fullPost = parsedPosts[0];

                foreach (var comment in fullPost.Comments)
                {
                    if (comment.Uin == _myUin) continue;
                    var replyKey = $"{fullPost.Tid}:{comment.Tid}:{comment.Uin}";
                    if (_repliedComments.Contains(replyKey)) continue;

                    var (_, promptContent) = ParseCommentContent(comment.Content);
                    var prompt = $"用户 {comment.Nickname} 评论了你的说说：{promptContent}，请生成一条简洁回复（0-15字）。";
                    var replyText = await CallLlmAsync(prompt, "");
                    if (string.IsNullOrEmpty(replyText)) continue;

                    var rootComment = FindRootComment(fullPost.Comments, comment);
                    var resp = await _api!.ReplyAsync(fullPost, comment, replyText, rootComment);
                    if (!resp.Ok)
                    {
                        logger.LogWarning("自动回复失败: {Tid}/{Uin} -> {Message}", comment.Tid, comment.Uin, resp.Message);
                        continue;
                    }
                    logger.LogInformation("自动回复成功: {Tid}/{Uin} -> {Text}", comment.Tid, comment.Uin, replyText);
                    _repliedComments.Add(replyKey);
                    SaveState();
                    newReplies++;
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * 1.0 + 0.5));
                    if (newReplies >= Configuration.MaxRepliesPerCycle) break;
                }
                if (newReplies >= Configuration.MaxRepliesPerCycle) break;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "自动回复任务失败");
        }
    }

    // ==================== 图片相关 ====================

    private bool IsRecentlyPublishedImage(string url)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dedupeInterval = ParseIntervalSeconds("3d", "h");
        return _publishedImageHistory.Any(h =>
            h.TryGetValue("identity", out var id) && id?.ToString() == url &&
            h.TryGetValue("time", out var t) && Convert.ToInt64(t) > now - dedupeInterval);
    }

    private async Task<List<string>> FetchChatHistoryAsync(string sourceType, string sourceId, int count)
    {
        var messages = await FetchHistoryMessagesAsync(sourceType, sourceId, Math.Max(count, 20));
        return messages.TakeLast(count).Select(msg =>
        {
            var sender = "";
            if (msg.TryGetProperty("sender", out var s) && s.TryGetProperty("nickname", out var n))
                sender = n.GetString() ?? "";
            return $"{sender}: {ExtractTextSimple(msg)}";
        }).ToList();
    }

    private async Task<List<JsonElement>> FetchHistoryMessagesAsync(string sourceType, string sourceId, int count)
    {
        var client = GetClient();
        if (client == null) return new();
        var action = sourceType == "group" ? "get_group_msg_history" : "get_friend_msg_history";
        var key = sourceType == "group" ? "group_id" : "user_id";
        var result = await client.CallActionAsync<JsonElement>(action, new Dictionary<string, object> { [key] = long.Parse(sourceId), ["count"] = count });
        var messages = new List<JsonElement>();
        if (result.TryGetProperty("data", out var d) && d.TryGetProperty("messages", out var msgs))
        {
            foreach (var m in msgs.EnumerateArray()) messages.Add(m);
        }

        if (Configuration.ImageManifestEnabled)
        {
            var sid = $"qq:{(sourceType == "group" ? "gm" : "dm")}:{sourceId}";
            if (!_imageRegistry.TryGetValue(sid, out var registry))
            {
                registry = new List<ImageEntry>();
                _imageRegistry[sid] = registry;
            }
            foreach (var msg in messages)
            {
                var sender = "";
                if (msg.TryGetProperty("sender", out var s) && s.TryGetProperty("nickname", out var n))
                    sender = n.GetString() ?? "";
                var msgId = msg.TryGetProperty("message_id", out var mid) ? mid.ToString() : "";
                var timestamp = msg.TryGetProperty("time", out var t) ? t.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (msg.TryGetProperty("message", out var segs))
                {
                    foreach (var seg in segs.EnumerateArray())
                    {
                        if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                            seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                        {
                            var u = System.Net.WebUtility.HtmlDecode(url.GetString() ?? "").Trim().Trim('"').Trim('\'');
                            if (!string.IsNullOrEmpty(u) && !registry.Any(e => e.Url == u))
                            {
                                registry.Add(new ImageEntry { Source = "url", Url = u, Sender = sender, Time = timestamp, MsgId = msgId });
                            }
                        }
                    }
                }
            }
            if (registry.Count > ImageRegistryCap) registry.RemoveRange(0, registry.Count - ImageRegistryCap);
        }
        return messages;
    }

    private async Task<List<string>> FetchRecentImagesAsync(string sourceType, string sourceId, int maxCount)
    {
        var messages = await FetchHistoryMessagesAsync(sourceType, sourceId, 20);
        var urls = new List<string>();
        foreach (var msg in messages.AsEnumerable().Reverse())
        {
            if (msg.TryGetProperty("message", out var segs))
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                        seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                    {
                        var u = System.Net.WebUtility.HtmlDecode(url.GetString() ?? "").Trim().Trim('"').Trim('\'');
                        if (!string.IsNullOrEmpty(u))
                        {
                            urls.Add(u);
                            if (urls.Count >= maxCount) return urls;
                        }
                    }
                }
            }
        }
        return urls;
    }

    private static string ExtractTextSimple(JsonElement msg)
    {
        var texts = new List<string>();
        if (msg.TryGetProperty("message", out var segs))
        {
            foreach (var seg in segs.EnumerateArray())
            {
                if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "text" &&
                    seg.TryGetProperty("data", out var data) && data.TryGetProperty("text", out var text))
                {
                    texts.Add(text.GetString() ?? "");
                }
            }
        }
        return string.Join(" ", texts);
    }

    private async Task<string> DescribeImageUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var bytes = await http.GetByteArrayAsync(url);
            if (bytes.Length == 0) return "";
            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLower();
            _urlMd5[url] = md5;
            if (visionModel == null)
                return $"[图片 {md5[..8]}]";
            var tempPath = Path.Combine(Path.GetTempPath(), $"qzone_img_{md5[..8]}.jpg");
            await File.WriteAllBytesAsync(tempPath, bytes);
            var desc = await visionModel.QueryAsync(tempPath, "请精简的描述一下图片大体内容，避免输出过多的文本", 64);
            return string.IsNullOrEmpty(desc) ? $"[图片 {md5[..8]}]" : desc;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "图片描述失败: {Url}", url);
            return "";
        }
    }

    private static (string Text, List<int> Chosen) SplitImgChoices(string text)
    {
        if (string.IsNullOrEmpty(text)) return ("", new List<int>());
        var matches = Regex.Matches(text, @"^\s*IMG\s*[:：]\s*([\d\s,，]+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (matches.Count == 0) return (text.Trim(), new List<int>());
        var chosen = new List<int>();
        foreach (var part in Regex.Split(matches[^1].Groups[1].Value.Trim(), @"[,，\s]+"))
        {
            if (int.TryParse(part, out var n)) chosen.Add(n);
        }
        var cleaned = Regex.Replace(text, @"^\s*IMG\s*[:：]\s*[\d\s,，]+\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
        return (cleaned, chosen);
    }

    private static List<string> ResolveDescribedSources(List<string> candidates, List<int> chosen, int target, int max)
    {
        var result = new List<string>();
        if (target > 0)
        {
            foreach (var idx in chosen)
            {
                if (idx >= 1 && idx <= candidates.Count && !result.Contains(candidates[idx - 1]))
                    result.Add(candidates[idx - 1]);
                if (result.Count >= target) break;
            }
            if (result.Count < target)
            {
                foreach (var c in candidates)
                {
                    if (!result.Contains(c)) result.Add(c);
                    if (result.Count >= target) break;
                }
            }
        }
        else
        {
            foreach (var idx in chosen)
            {
                if (idx >= 1 && idx <= candidates.Count && !result.Contains(candidates[idx - 1]))
                    result.Add(candidates[idx - 1]);
                if (result.Count >= max) break;
            }
        }
        return result;
    }

    // ==================== 数据获取 ====================

    private async Task<List<QzonePost>> GetFeedsAsync(string? targetId, int num)
    {
        await EnsureApiAsync();
        ApiResponse resp;
        if (!string.IsNullOrEmpty(targetId))
            resp = await _api!.GetMsgListAsync(long.Parse(targetId), num);
        else
            resp = await _api!.GetRecentFeedsAsync();
        if (!resp.Ok) throw new Exception($"获取说说失败: {resp.Message}");
        var msgList = resp.Data.GetValueOrDefault("msglist") as List<object?> ?? new();
        var posts = QzoneParser.ParseFeeds(msgList);
        if (posts.Count == 0 && string.IsNullOrEmpty(targetId))
        {
            // 回退：feeds3_html_more 返回的 HTML 格式
            posts = QzoneParser.ParseRecentFeeds(resp.Data);
        }
        return posts.Take(num).ToList();
    }

    private async Task<string> CommentAsync(QzonePost post, string content)
    {
        await EnsureApiAsync();
        if (_myUin == 0) throw new Exception("无法确认当前登录 QQ，已取消评论");

        // 幂等检查
        try
        {
            var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
            if (detailResp.Ok)
            {
                var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                if (parsed.Count > 0)
                {
                    var expected = Regex.Replace(content ?? "", @"\s+", "");
                    var own = parsed[0].Comments.Count(c =>
                        c.Uin == _myUin && Regex.Replace(c.Content ?? "", @"\s+", "") == expected);
                    if (own > 0) return "评论成功（该说说已存在相同内容的评论，未重复提交）";
                }
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "评论前幂等检查失败（继续提交）");
        }

        var resp = await _api!.CommentAsync(post.Uin, post.Tid, content);
        if (!resp.Ok) throw new Exception($"评论接口失败: {resp.Message}");
        return "评论成功";
    }

    // ==================== 生命周期 ====================

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "提供QQ空间说说发布、查看、点赞、评论、删除、访客统计、定时任务等功能"
        };
        functionCaller.RegisterHandler(xmlHandler, DocumentMode.Implicit, DestroyCancellationToken);

        foreach (var id in Configuration.MasterIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _masterIds.Add(id);
        foreach (var id in Configuration.QzoneBlacklist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _blacklist.Add(id);
        foreach (var id in Configuration.QzoneWhitelist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _whitelist.Add(id);
        foreach (var sched in Configuration.BlackoutSchedules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = sched.Split('-', 2);
            if (parts.Length == 2 && TimeSpan.TryParse(parts[0].Trim(), out var start) && TimeSpan.TryParse(parts[1].Trim(), out var end))
                _blackoutTimes.Add((start, end));
        }

        LoadState();
        SetupScheduledJobs();

        var refreshInterval = ParseIntervalSeconds(Configuration.CookieRefreshInterval, "h");
        if (Configuration.AutoRefreshCookie && refreshInterval > 0)
        {
            _cookieRefreshTimer = new Timer(_ => _ = RefreshCookieAsync(false), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(Math.Max(300, refreshInterval)));
        }

        return Task.CompletedTask;
    }

    protected override Task OnDestroy()
    {
        _cookieRefreshTimer?.Dispose();
        _autoPublishTimer?.Dispose();
        _autoCommentTimer?.Dispose();
        _autoReplyTimer?.Dispose();
        SaveState();
        return Task.CompletedTask;
    }

    // ==================== 工具函数 ====================

    [XmlFunction(FunctionMode.OneShot)]
    [Description("发布一条说说到自己的QQ空间。配图方式：1) images参数传图片URL或本地路径；2) 先调用qzone_image_manifest获取[近期图片]清单，再用image_indices引用序号。都不传时默认纯文字发布。")]
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

            // 清单序号配图
            if (!string.IsNullOrEmpty(imageIndices))
            {
                var resolved = await ResolveManifestImagesAsync(imageIndices);
                if (resolved.Count == 0)
                {
                    interactor.Poke($"未能从[近期图片]清单解析出图片（清单为空或序号超出范围），说说未发布。如确认发纯文字，请不带image_indices重试；如想配图，可改用images参数传图片URL或本地路径。");
                    return;
                }
                imgList.AddRange(resolved);
            }

            var result = await _api!.PublishAsync(text, imgList, allowImageDrop: false);
            if (result.Ok)
            {
                _myPostsHistory.Add(text);
                SaveState();
                interactor.Poke($"发布成功 tid={result.Data.GetValueOrDefault("tid")}");
            }
            else
            {
                interactor.Poke($"发布失败：{result.Message}");
            }
        }
        catch (Exception e)
        {
            interactor.Poke($"发布失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看QQ空间说说。不提供target_id默认查看自己的空间；要查看好友动态请提供好友QQ号。返回每条说说的ID、发布时间、配图数量和最新评论。如果说说有配图且你需要了解图片内容，可调用qzone_describe_image。")]
    public async Task QzoneView(
        [Description("目标QQ号(可选)")] string? targetId = null,
        [Description("查看条数，默认1")] int num = 1)
    {
        try
        {
            await EnsureApiAsync();
            var target = string.IsNullOrEmpty(targetId) ? _myUin.ToString() : targetId;
            var block = TargetBlockReason(target);
            if (block != null)
            {
                interactor.Poke($"查看被拒绝：{block}");
                return;
            }
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
                var timeStr = FormatTime(p.CreateTime);
                var line = $"【{p.Name}】(ID:{p.Tid}) [{timeStr}]: {p.Text}";
                if (p.Images.Count > 0)
                {
                    var isOwn = _myUin != 0 && p.Uin == _myUin;
                    if (Configuration.QzoneImageDescEnabled && (!isOwn || Configuration.QzoneImageDescOwn))
                        line += $"\n配图x{p.Images.Count}（调用 qzone_describe_image(target_id='{p.Uin}', tid='{p.Tid}', index=第几张) 可查看图片内容）";
                    else
                        line += $"\n配图x{p.Images.Count}";
                }

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
                        var timeStr2 = FormatTime(cmt.CreateTime);
                        lines.Add(FormatCommentLine(cmt, "评", "  ", timeStr2));
                    }
                    if (p.Comments.Count > shown.Count)
                        lines.Add($"  ...等{p.Comments.Count}条评论");
                }
                if (p.LikeCount > 0)
                {
                    var shownLikes = p.LikeUsers.Take(Configuration.LikeUsersDisplayMax).ToList();
                    if (shownLikes.Count > 0)
                    {
                        if (p.LikeCount == 1 && shownLikes.Count == 1)
                            lines.Add($"  已赞1人：{shownLikes[0]} 觉得很赞");
                        else if (shownLikes.Count >= p.LikeCount)
                            lines.Add($"  已赞{p.LikeCount}人：{string.Join("、", shownLikes)} 觉得很赞");
                        else
                            lines.Add($"  已赞{p.LikeCount}人：{string.Join("、", shownLikes)} 等人 觉得很赞");
                    }
                    else
                    {
                        lines.Add($"  已赞{p.LikeCount}人");
                    }
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
    [Description("给指定说说点赞，或取消已点的赞。用户要求点赞用默认action=like；用户要求取消点赞时必须传action=unlike。")]
    public async Task QzoneLike(
        [Description("目标QQ号")] string targetId,
        [Description("说说ID")] string tid,
        [Description("操作类型：like=点赞（默认），unlike=取消点赞")] string action = "like")
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            var block = TargetBlockReason(targetId);
            if (block != null)
            {
                interactor.Poke($"点赞被拒绝：{block}");
                return;
            }
            await EnsureApiAsync();
            await ThrottleWriteAsync();
            var post = new QzonePost { Uin = long.Parse(targetId), Tid = NormalizeTid(tid) };

            if (action == "unlike")
            {
                var resp = await _api!.LikeAsync(post, post.CreateTime, unlike: true);
                interactor.Poke(resp.Ok ? "取消点赞成功" : $"取消点赞失败：{resp.Message}");
                return;
            }

            var result = await _api!.LikeAsync(post, post.CreateTime);
            interactor.Poke(result.Ok ? "点赞成功" : $"点赞失败：{result.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"点赞失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("评论指定的说说。")]
    public async Task QzoneComment(
        [Description("目标QQ号")] string targetId,
        [Description("说说ID")] string tid,
        [Description("评论内容(可选，不传则AI自动生成)")] string? content = null)
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            var block = TargetBlockReason(targetId);
            if (block != null)
            {
                interactor.Poke($"评论被拒绝：{block}");
                return;
            }
            await EnsureApiAsync();
            await ThrottleWriteAsync();
            var post = new QzonePost { Uin = long.Parse(targetId), Tid = NormalizeTid(tid) };
            var result = await CommentAsync(post, content ?? "");
            if (Configuration.LikeWhenComment && !result.Contains("未重复提交"))
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * Configuration.LikeDelayJitter + Configuration.LikeDelayMin));
                var likeResp = await _api!.LikeAsync(post, post.CreateTime);
                if (likeResp.Ok) result += "，已同时点赞";
            }
            interactor.Poke(result);
        }
        catch (Exception e)
        {
            interactor.Poke($"评论失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除自己的一条说说")]
    public async Task QzoneDelete(
        [Description("要删除的说说的ID")] string tid)
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            await EnsureApiAsync();
            var resp = await _api!.DeleteAsync(NormalizeTid(tid));
            interactor.Poke(resp.Ok ? $"说说 {tid} 删除成功" : $"删除失败：{resp.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"删除失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("删除指定评论（主评论或楼中回复均可）。评论ID和作者UIN从qzone_view获取；楼中回复建议同时传comment_uin精确定位。")]
    public async Task QzoneDeleteComment(
        [Description("说说作者的QQ号")] string targetId,
        [Description("说说ID")] string tid,
        [Description("要删除的评论ID")] string commentId,
        [Description("评论作者QQ号(可选)")] string? commentUin = null)
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            var block = TargetBlockReason(targetId);
            if (block != null)
            {
                interactor.Poke($"删除评论被拒绝：{block}");
                return;
            }
            await EnsureApiAsync();
            var realCid = commentId;
            try
            {
                var detailResp = await _api!.GetDetailAsync(long.Parse(targetId), NormalizeTid(tid));
                if (detailResp.Ok)
                {
                    var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                    if (parsed.Count > 0)
                    {
                        var matched = MatchComment(parsed[0].Comments, commentId, commentUin ?? "");
                        if (matched != null && !string.IsNullOrEmpty(matched.CommentId))
                        {
                            realCid = matched.CommentId;
                            logger.LogInformation("删除评论反查真实ID: {Old} -> {New}", commentId, realCid);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "删除评论反查失败（继续用原ID）");
            }
            var resp = await _api!.DeleteCommentAsync(targetId, NormalizeTid(tid), realCid, commentUin ?? "");
            interactor.Poke(resp.Ok ? "评论删除成功" : $"删除评论失败：{resp.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"删除评论失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("回复指定评论。先从qzone_view获取评论ID和UIN；当同一说说内ID重复时必须同时传comment_uin，避免回复错人。")]
    public async Task QzoneReplyComment(
        [Description("说说作者的QQ号")] string targetId,
        [Description("说说ID")] string tid,
        [Description("要回复的评论ID")] string commentId,
        [Description("评论作者QQ号(可选)")] string? commentUin = null,
        [Description("回复内容(可选)")] string? content = null)
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            var block = TargetBlockReason(targetId);
            if (block != null)
            {
                interactor.Poke($"回复被拒绝：{block}");
                return;
            }
            await EnsureApiAsync();
            await ThrottleWriteAsync();
            var post = new QzonePost { Uin = long.Parse(targetId), Tid = NormalizeTid(tid) };
            var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
            if (!detailResp.Ok)
            {
                interactor.Poke("获取说说详情失败，无法获取评论者信息");
                return;
            }
            var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
            if (parsed.Count == 0)
            {
                interactor.Poke("解析说说详情失败");
                return;
            }
            var fullPost = parsed[0];
            var matches = fullPost.Comments.Where(c => c.Tid.ToString() == commentId).ToList();
            if (!string.IsNullOrEmpty(commentUin))
                matches = matches.Where(c => c.Uin.ToString() == commentUin).ToList();
            if (matches.Count == 0)
            {
                interactor.Poke($"未找到指定的评论 ID: {commentId}");
                return;
            }
            if (matches.Count > 1)
            {
                var options = string.Join("，", matches.Select(c => $"{c.Nickname}(UIN:{c.Uin})"));
                interactor.Poke($"评论 ID {commentId} 不唯一，请补充 comment_uin。可选目标：{options}");
                return;
            }
            var targetComment = matches[0];
            var finalContent = content ?? "";
            if (string.IsNullOrEmpty(finalContent))
            {
                var (_, promptContent) = ParseCommentContent(targetComment.Content);
                var prompt = $"用户 {targetComment.Nickname} 评论了你的说说：{promptContent}，请生成一条简洁回复（0-15字）。";
                finalContent = await CallLlmAsync(prompt, "");
                if (string.IsNullOrEmpty(finalContent)) finalContent = "谢谢支持！";
            }
            var rootComment = FindRootComment(fullPost.Comments, targetComment);
            var resp = await _api!.ReplyAsync(fullPost, targetComment, finalContent, rootComment);
            interactor.Poke(resp.Ok ? $"回复成功: {finalContent}" : $"回复失败：{resp.Message}");
        }
        catch (Exception e)
        {
            interactor.Poke($"回复失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看自己QQ空间最近访客和访客统计。返回最近访客明细、来源、隐身/黄钻状态，以及今日和最近30天访客数。")]
    public async Task QzoneVisitors()
    {
        try
        {
            if (!IsMaster(GetSenderId()))
            {
                interactor.Poke("抱歉，只有主人才能使用此功能");
                return;
            }
            await EnsureApiAsync();
            var resp = await _api!.GetVisitorAsync(Configuration.VisitorLimit);
            if (!resp.Ok)
            {
                interactor.Poke($"获取访客失败：{resp.Message}");
                return;
            }
            var text = QzoneParser.ParseVisitors(resp.Raw, Configuration.VisitorLimit);
            interactor.Poke(text);
        }
        catch (Exception e)
        {
            interactor.Poke($"获取访客失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("查看说说中某张配图的实际内容。他人的说说：当文字暗示图片很重要或你打算评论前想了解图片内容时调用。自己的说说：一般不要调用。")]
    public async Task QzoneDescribeImage(
        [Description("说说作者的QQ号")] string targetId,
        [Description("说说ID")] string tid,
        [Description("第几张图片（从1开始），默认1")] int index = 1)
    {
        try
        {
            if (!Configuration.QzoneImageDescEnabled)
            {
                interactor.Poke("空间图片识别功能未启用");
                return;
            }
            var block = TargetBlockReason(targetId);
            if (block != null)
            {
                interactor.Poke($"识图被拒绝：{block}");
                return;
            }
            if (!Configuration.QzoneImageDescOwn && _myUin != 0 && targetId == _myUin.ToString())
            {
                interactor.Poke("这是你自己发布的说说，一般不需要识图（配图本来就是你自己选的）");
                return;
            }
            await EnsureApiAsync();
            var post = new QzonePost { Uin = long.Parse(targetId), Tid = NormalizeTid(tid) };
            var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
            if (!detailResp.Ok)
            {
                interactor.Poke($"获取说说详情失败: {detailResp.Message}");
                return;
            }
            var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
            if (parsed.Count == 0)
            {
                interactor.Poke("解析说说详情失败");
                return;
            }
            var fullPost = parsed[0];
            if (fullPost.Images.Count == 0)
            {
                interactor.Poke("这条说说没有配图");
                return;
            }
            if (index < 1 || index > fullPost.Images.Count)
            {
                interactor.Poke($"图片序号超出范围，这条说说共 {fullPost.Images.Count} 张图");
                return;
            }
            var url = fullPost.Images[index - 1];
            var desc = await DescribeImageUrlAsync(url);
            if (string.IsNullOrEmpty(desc))
            {
                interactor.Poke("图片识别失败（识图模型不可用或缓存未命中）");
                return;
            }
            interactor.Poke($"第{index}张图片内容（共{fullPost.Images.Count}张）：{desc}");
        }
        catch (Exception e)
        {
            interactor.Poke($"识别失败：{e.Message}");
        }
    }

    [XmlFunction(FunctionMode.OneShot)]
    [Description("获取[近期图片]清单：当前会话最近出现的图片及内容描述。发布说说需要配图时调用此函数获取清单，然后用qzone_publish的image_indices参数引用序号配图。")]
    public async Task QzoneImageManifest()
    {
        try
        {
            if (!Configuration.ImageManifestEnabled)
            {
                interactor.Poke("近期图片清单功能未启用");
                return;
            }
            var sid = GetCurrentSessionId();
            if (string.IsNullOrEmpty(sid))
            {
                interactor.Poke("无法确定当前会话，请直接使用images参数传图片URL");
                return;
            }
            if (!_imageRegistry.TryGetValue(sid, out var registry) || registry.Count == 0)
            {
                // 尝试从历史消息拉取
                try
                {
                    var (type, id) = ParseSessionId(sid);
                    if (!string.IsNullOrEmpty(id))
                        await FetchHistoryMessagesAsync(type, id, 20);
                }
                catch { }
                registry = _imageRegistry.GetValueOrDefault(sid) ?? new();
            }
            if (registry.Count == 0)
            {
                interactor.Poke("当前会话暂无近期图片，可改用images参数传图片URL或本地路径");
                return;
            }
            var entries = registry.TakeLast(Configuration.ImageManifestCount).ToList();
            var lines = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var desc = entry.Desc;
                if (string.IsNullOrEmpty(desc))
                {
                    desc = await DescribeImageUrlAsync(entry.Url ?? "");
                    if (!string.IsNullOrEmpty(desc)) entry.Desc = desc;
                }
                var timeStr = DateTimeOffset.FromUnixTimeSeconds(entry.Time).LocalDateTime.ToString("MM-dd HH:mm");
                var sender = string.IsNullOrEmpty(entry.Sender) ? "未知" : entry.Sender;
                lines.Add($"{i + 1}. [{timeStr} {sender}] {desc}");
            }
            interactor.Poke("[近期图片] 本会话最近出现的图片及内容描述，调用 qzone_publish 发说说时可用 image_indices 参数引用序号配图：\n" + string.Join("\n", lines));
        }
        catch (Exception e)
        {
            interactor.Poke($"获取图片清单失败：{e.Message}");
        }
    }

    private async Task<List<string>> ResolveManifestImagesAsync(string imageIndices)
    {
        var result = new List<string>();
        var sid = GetCurrentSessionId();
        if (string.IsNullOrEmpty(sid) || !_imageRegistry.TryGetValue(sid, out var registry))
            return result;
        var entries = registry.TakeLast(Configuration.ImageManifestCount).ToList();
        foreach (var part in imageIndices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var idx)) continue;
            if (idx < 1 || idx > entries.Count) continue;
            var url = entries[idx - 1].Url;
            if (!string.IsNullOrEmpty(url) && !result.Contains(url))
                result.Add(url);
        }
        return result;
    }

    private string? GetCurrentSessionId()
    {
        try
        {
            foreach (var content in ChatBot.ChatHistory.Reverse())
            {
                if (content.Role != AuthorRole.User) continue;
                var text = content.Content ?? "";
                if (text.StartsWith("[来自系统的杂项消息推送]") || text.StartsWith("[消息来源(")) continue;
                var m = Regex.Match(text, @"\[群聊消息\((\d+)");
                if (m.Success) return $"qq:gm:{m.Groups[1].Value}";
                m = Regex.Match(text, @"\[私聊消息\((\d+)\)\]");
                if (m.Success) return $"qq:dm:{m.Groups[1].Value}";
                return null;
            }
        }
        catch { }
        return null;
    }

    private static (string Type, string Id) ParseSessionId(string sid)
    {
        var parts = sid.Split(':');
        if (parts.Length >= 3)
            return (parts[1] == "gm" ? "group" : "private", parts[2]);
        return ("", "");
    }
}
