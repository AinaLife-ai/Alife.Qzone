using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

namespace AinaLife.Qzone;

public class QzoneConfig
{
    [DisplayName("Cookie字符串")]
    [Description("QQ空间登录Cookie（仅作自动刷新失败时的应急后备），格式如 uin=o123; skey=xxx; p_skey=yyy")]
    public string CookiesStr { get; set; } = "";

    [DisplayName("自动刷新Cookie")]
    [Description("从OneBot自动获取最新Cookie（推荐开启，四层机制：启动/用即刷/周期/失效自救）")]
    public bool AutoRefreshCookie { get; set; } = true;

    [DisplayName("Cookie刷新间隔")]
    [Description("Cookie周期刷新间隔（±10%抖动），如 2h/30m/7200；0=仅启动时+失效时刷新")]
    public string CookieRefreshInterval { get; set; } = "2h";

    [DisplayName("用即刷节流")]
    [Description("调用空间功能时距上次刷新超过该间隔则顺手刷新，如 10m；0=关闭")]
    public string CookieRefreshOnUse { get; set; } = "10m";

    [DisplayName("主人QQ号")]
    [Description("允许执行敏感操作的主人QQ号，多个用逗号分隔（仅当启用主人检查时生效）")]
    public string MasterIds { get; set; } = "";

    [DisplayName("启用主人检查")]
    [Description("敏感操作是否仅限主人。官方不建议开启（会拦截AI自主行为），推荐在人设/提示词层控制权限")]
    public bool MasterCheckEnabled { get; set; } = false;

    [DisplayName("访客显示上限")]
    [Description("访客列表最多显示条数")]
    public int VisitorLimit { get; set; } = 20;

    [DisplayName("点赞用户显示上限")]
    [Description("说说点赞用户最多显示人数")]
    public int LikeUsersDisplayMax { get; set; } = 5;

    [DisplayName("查看评论显示上限")]
    [Description("查看说说时每条最多显示的评论数（省token）")]
    public int ViewCommentMax { get; set; } = 10;

    [DisplayName("评论后自动点赞")]
    [Description("评论后是否自动点赞（幂等命中已存在相同评论时不赞）")]
    public bool LikeWhenComment { get; set; } = true;

    [DisplayName("自动点赞延迟(秒)")]
    [Description("评论后自动点赞的随机延迟下限")]
    public double LikeDelayMin { get; set; } = 0.5;

    [DisplayName("自动点赞延迟抖动(秒)")]
    [Description("评论后自动点赞的随机延迟抖动范围")]
    public double LikeDelayJitter { get; set; } = 1.0;

    [DisplayName("写操作节流间隔(秒)")]
    [Description("点赞/评论等写操作的最小间隔（透明节流，不拦截不失败）")]
    public double WriteThrottleSeconds { get; set; } = 1.0;

    [DisplayName("请求超时(秒)")]
    [Description("HTTP请求超时时间（OneBot获取Cookie固定5秒快速失败，不受其影响）")]
    public int Timeout { get; set; } = 30;

    [DisplayName("自动发布定时")]
    [Description("自动发布说说定时表达式，cron或interval格式，如 */30 * * * * 或 30m、2h/30m(抖动)；空=禁用")]
    public string AutoPublishSchedule { get; set; } = "";

    [DisplayName("自动评论定时")]
    [Description("自动评论定时表达式，格式同上")]
    public string AutoCommentSchedule { get; set; } = "";

    [DisplayName("自动回复定时")]
    [Description("自动回复定时表达式，格式同上")]
    public string AutoReplySchedule { get; set; } = "";

    [DisplayName("启用自动回复")]
    [Description("是否启用自动回复评论（需配合自动回复定时）")]
    public bool AutoReplyEnabled { get; set; } = false;

    [DisplayName("每轮最大评论数")]
    [Description("每轮自动评论最多条数（后台模式）")]
    public int MaxCommentsPerCycle { get; set; } = 3;

    [DisplayName("每轮最大回复数")]
    [Description("每轮自动回复最多条数（后台模式）")]
    public int MaxRepliesPerCycle { get; set; } = 5;

    [DisplayName("黑名单QQ")]
    [Description("禁止操作的QQ号，逗号分隔（含查看；优先于白名单；填自己也禁自己）")]
    public string QzoneBlacklist { get; set; } = "";

    [DisplayName("白名单QQ")]
    [Description("仅允许操作的QQ号，逗号分隔；非空时仅名单内+自己；空=除黑名单全允许")]
    public string QzoneWhitelist { get; set; } = "";

    [DisplayName("黑名单时间段")]
    [Description("定时任务黑名单时间段，如 00:00-06:00，支持跨天，多个用逗号分隔（只管定时任务）")]
    public string BlackoutSchedules { get; set; } = "";

    [DisplayName("图片清单启用")]
    [Description("是否启用近期图片清单（QQ消息到来时自动注入给AI）")]
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

    [DisplayName("自动评论前识图")]
    [Description("后台自动评论前先识别对方配图（更贴切但更费token）")]
    public bool AutoCommentImageDesc { get; set; } = false;

    [DisplayName("自动发布群ID")]
    [Description("后台直接生成模式数据源群号（未配置任务目标时生效）")]
    public string AutoPublishGroupId { get; set; } = "";

    [DisplayName("自动发布用户ID")]
    [Description("后台直接生成模式数据源QQ号（群号也未配时生效）")]
    public string AutoPublishUserId { get; set; } = "";

    [DisplayName("自动发布配图概率")]
    [Description("自动发布时进入配图候选流程的概率(0-1)")]
    public double AutoPublishImageProb { get; set; } = 1.0;

    [DisplayName("自动发布配图最少")]
    [Description("自动发布配图目标下限；抽到0时AI自主决定0~最多张")]
    public int AutoPublishImageMin { get; set; } = 0;

    [DisplayName("自动发布配图最多")]
    [Description("自动发布配图目标上限")]
    public int AutoPublishImageMax { get; set; } = 3;

    [DisplayName("配图选图兜底")]
    [Description("AI非法选图时是否兜底补足；关=降级发纯文字")]
    public bool AutoPublishImageFallback { get; set; } = false;

    [DisplayName("配图去重间隔")]
    [Description("已发布图片的去重窗口，如 3d/72h/12h；0=不去重；仅发布成功后记录")]
    public string AutoPublishImageDedupeInterval { get; set; } = "3d";

    [DisplayName("评论提交校验")]
    [Description("评论提交后回读确认（诊断模式，仅记录日志不阻断）")]
    public bool CommentVerify { get; set; } = false;

    [DisplayName("任务群ID")]
    [Description("定时任务指令发送的群号，逗号分隔，随机选一个作为场合上下文")]
    public string TaskGroupIds { get; set; } = "";

    [DisplayName("任务私聊ID")]
    [Description("定时任务指令发送的QQ号，逗号分隔")]
    public string TaskPrivateIds { get; set; } = "";

    [DisplayName("任务消息风格")]
    [Description("定时任务消息风格：silent=提示AI不要向群/私聊发送回复（无痕）；notify=不做限制")]
    public string TaskMessageStyle { get; set; } = "silent";

    [DisplayName("吸附模式")]
    [Description("发说说未指定图片时自动抓最近一张图（开启则关闭清单机制，失败降级纯文字）")]
    public bool AutoAttachRecentImage { get; set; } = false;
}

[Module("QQ空间",
    "提供QQ空间说说发布、查看、点赞、评论、回复、删除、访客统计、图片识图、定时任务、Cookie自动获取与刷新等功能",
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

    // QChatService 未公开 OneBotClient（官方源码字段名 oneBotClient），通过反射获取（不修改官方代码）
    private OneBotClient? GetClient()
    {
        var field = typeof(QChatService).GetField("oneBotClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(qChatService) as OneBotClient;
    }

    private QzoneSession _session = null!;
    private QzoneHttpClient _httpClient = null!;
    private QzoneApi? _api;
    private QzoneState _state = null!;
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
    private bool _initFailed;
    private bool _cookieHealing;      // 启动自愈窗口
    private int _healAttempts;

    // 定时任务
    private readonly List<ScheduledJob> _jobs = new();
    private Timer? _scheduleTimer;
    private readonly SemaphoreSlim _jobLock = new(1, 1);

    // 图片注册表 sid -> entries
    private readonly Dictionary<string, List<ImageEntry>> _imageRegistry = new();
    private readonly Dictionary<string, string> _urlMd5 = new();

    // 实时消息上下文（结构化发送者识别，来自 OneBot 事件）
    private readonly Dictionary<string, (long UserId, DateTime Time)> _lastSenderBySource = new();
    private bool _eventSubscribed;

    private class ImageEntry
    {
        public string? Source { get; set; }
        public string? Url { get; set; }
        public string? Sender { get; set; }
        public long Time { get; set; }
        public string? Desc { get; set; }
        public string? MsgId { get; set; }
        public bool DescPending { get; set; }
    }

    private class ScheduledJob
    {
        public required string Name { get; set; }
        public required Func<Task> Run { get; set; }
        public required Func<DateTime, DateTime> NextAfter { get; set; }
        public DateTime NextRun;
        public DateTime LastRun = DateTime.MinValue;
        public DateTime LastExecuted = DateTime.MinValue;
    }

    // ==================== 权限与名单 ====================

    // 人设层控权设计：默认不做代码层主人检查；开启后严格 fail-closed
    private bool IsMaster(string? userId)
    {
        if (!Configuration.MasterCheckEnabled) return true;
        if (string.IsNullOrEmpty(userId)) return false; // 无法识别发送者 = 拒绝（fail-closed）
        if (userId.StartsWith("system")) return true;   // 系统/定时任务内部调用放行
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

    // ==================== 表达式解析 ====================

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

    /// <summary>解析定时表达式：cron（5段）或 interval（30m、2h/30m 抖动）。</summary>
    private static Func<DateTime, DateTime>? BuildSchedule(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        // cron：5 段
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5)
        {
            var fields = new HashSet<int>?[5];
            try
            {
                int[][] ranges = { new[] { 0, 59 }, new[] { 0, 23 }, new[] { 1, 31 }, new[] { 1, 12 }, new[] { 0, 7 } };
                for (int i = 0; i < 5; i++)
                    fields[i] = ParseCronField(parts[i], ranges[i][0], ranges[i][1]);
            }
            catch { return null; }
            if (fields.Any(f => f == null)) return null;
            return after => NextCronTime(fields!, after);
        }

        // interval：数字[hm](/数字[hm])，无单位默认分钟
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
            if (interval <= 0) return null;
            return after =>
            {
                var delay = interval + (jitter > 0 ? Random.Shared.Next(0, jitter + 1) : 0);
                return after.AddSeconds(delay);
            };
        }
        return null;
    }

    private static HashSet<int> ParseCronField(string field, int min, int max)
    {
        var set = new HashSet<int>();
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int step = 1;
            var body = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                step = int.Parse(part[(slash + 1)..]);
                body = part[..slash];
                if (step <= 0) step = 1;
            }
            int lo, hi;
            if (body == "*")
            {
                lo = min; hi = max;
            }
            else if (body.Contains('-'))
            {
                var p = body.Split('-', 2);
                lo = int.Parse(p[0]); hi = int.Parse(p[1]);
            }
            else
            {
                lo = hi = int.Parse(body);
                if (slash < 0) { set.Add(Norm(lo)); continue; }
                // 形如 n/m：从 n 到 max 按步长
                hi = max;
            }
            for (int v = lo; v <= hi; v += step)
                set.Add(Norm(v));
        }
        int Norm(int v)
        {
            // 周日 7 → 0
            if (max == 7 && v == 7) return 0;
            if (v < min || v > max) throw new FormatException($"cron 字段越界: {v}");
            return v;
        }
        return set;
    }

    /// <summary>计算下一个 cron 触发时间（分钟粒度，最多向前找 366 天）</summary>
    private static DateTime NextCronTime(HashSet<int>?[] fields, DateTime after)
    {
        var t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0).AddMinutes(1);
        var limit = after.AddDays(366);
        while (t <= limit)
        {
            if (fields[3]!.Contains(t.Month) &&
                fields[4]!.Contains((int)t.DayOfWeek) &&
                fields[2]!.Contains(t.Day) &&
                fields[1]!.Contains(t.Hour) &&
                fields[0]!.Contains(t.Minute))
                return t;
            t = t.AddMinutes(1);
        }
        return after.AddDays(1);
    }

    // ==================== 文本工具 ====================

    private static string FormatTime(long ts) => QzoneParser.FormatTime(ts);

    private static string NormalizeTid(string tid) => QzoneParser.NormalizeTid(tid);

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

    // ==================== Cookie 管理（四层机制，对齐 Kira v1.40+） ====================

    /// <summary>
    /// 从 OneBot 获取最新 Cookie 并原地更新会话。
    /// 节流：常规冷却 10s，force 最小 3s；失败也推进时间戳；失败保留现有会话（last-good）。
    /// </summary>
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

            // 登录预检：OneBot 未连接时快速失败，避免每次白等
            var client = GetClient();
            if (client == null || !client.IsConnected)
            {
                _lastCookieRefresh = now;
                logger.LogDebug("OneBot 未连接，跳过 Cookie 刷新");
                return false;
            }

            string? newCookie = null;
            try
            {
                // 注意：Alife 的 CallActionAsync<T> 返回的是 OneBot 响应的 data 字段本身（已解包），
                // 且内置固定 10s 超时快速失败。domain 对齐 Kira：user.qzone.qq.com
                var data = await client.CallActionAsync<JsonElement>("get_cookies", new { domain = "user.qzone.qq.com" });
                if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("cookies", out var c))
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
                // 原地更新会话（不写回配置；配置的 Cookie 仅作应急后备）
                _session.UpdateCookies(newCookie);
                _myUin = _session.GetCtx().Uin;
                _lastCookieRefresh = now;
                _initFailed = false;
                _cookieHealing = false;
                logger.LogInformation("已从 OneBot 获取最新 Cookie 并原地更新会话 (uin={Uin})", _myUin);
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

    /// <summary>会话保活探针：用轻量访客接口验证旧会话是否仍然可用</summary>
    private async Task<bool> CheckSessionAliveAsync()
    {
        try
        {
            if (_api == null || !_session.HasContext) return false;
            var resp = await _api.GetVisitorAsync(1);
            return resp.Ok;
        }
        catch { return false; }
    }

    /// <summary>启动自愈：初始 Cookie 获取失败不判死，后台按 15/30/60/120s 递增重试最多 4 次</summary>
    private void StartCookieSelfHeal()
    {
        if (!Configuration.AutoRefreshCookie) return;
        _cookieHealing = true;
        _healAttempts = 0;
        int[] delays = { 15, 30, 60, 120 };
        _ = Task.Run(async () =>
        {
            while (_healAttempts < delays.Length && _cookieHealing)
            {
                await Task.Delay(TimeSpan.FromSeconds(delays[_healAttempts]));
                if (!_cookieHealing) return;
                _healAttempts++;
                try
                {
                    if (await RefreshCookieAsync(force: true))
                    {
                        logger.LogInformation("Cookie 后台自愈成功（第 {N} 次尝试）", _healAttempts);
                        return;
                    }
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "Cookie 自愈尝试失败（第 {N} 次）", _healAttempts);
                }
            }
            if (_cookieHealing)
            {
                _cookieHealing = false;
                _initFailed = true;
                logger.LogWarning("Cookie 后台自愈已耗尽重试次数，空间功能暂不可用（将在用即刷/周期刷新/失效自救时继续尝试）");
            }
        });
    }

    private async Task EnsureApiAsync()
    {
        if (_api != null && !_initFailed)
        {
            // 用即刷：距上次刷新超过节流间隔则顺手刷新
            var onUse = ParseIntervalSeconds(Configuration.CookieRefreshOnUse, "m");
            if (Configuration.AutoRefreshCookie && onUse > 0 && (DateTime.Now - _lastCookieRefresh).TotalSeconds > onUse)
            {
                try { await RefreshCookieAsync(false); } catch { }
            }
            return;
        }

        if (_cookieHealing)
            throw new Exception("QQ空间 Cookie 正在后台自动获取中，请稍后再试");

        if (Configuration.AutoRefreshCookie)
        {
            try
            {
                if (await RefreshCookieAsync(false)) return;
                // 刷新失败但存在旧会话：用保活探针验证旧会话仍可用则继续
                if (_session.HasContext && await CheckSessionAliveAsync())
                {
                    _initFailed = false;
                    logger.LogInformation("Cookie 刷新失败但旧会话仍然可用，继续工作");
                    return;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "刷新 Cookie 异常");
            }
        }

        if (!_session.HasContext)
        {
            try { _session.GetCtx(); }
            catch (Exception e) { throw new Exception($"QQ空间会话不可用：{e.Message}"); }
        }
        _myUin = _session.GetCtx().Uin;
        if (_myUin == 0)
            throw new Exception("Cookie 中未解析到有效 QQ 号（uin），会话不可用");
        _initFailed = false;
        logger.LogInformation("QQ空间会话就绪 uin={Uin}", _myUin);
    }

    private async Task ThrottleWriteAsync()
    {
        // 透明节流：只延迟不拦截，相邻写间隔 = 配置值 + 抖动
        var min = Configuration.WriteThrottleSeconds;
        var elapsed = DateTime.Now - _lastWriteTime;
        var wait = min + Random.Shared.NextDouble() * Configuration.LikeDelayJitter - elapsed.TotalSeconds;
        if (wait > 0)
            await Task.Delay(TimeSpan.FromSeconds(wait));
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

    // ==================== 发送者/场合识别（结构化优先，历史正则兜底） ====================

    private string? GetCurrentSessionId()
    {
        try
        {
            foreach (var content in ChatBot.ChatHistory.Reverse())
            {
                if (content.Role != Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User) continue;
                var text = content.Content ?? "";
                if (text.StartsWith(ChatBot.PokeMessageTag) || text.StartsWith("[消息来源(")) continue;
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

    private string? GetSenderId()
    {
        try
        {
            // 结构化优先：当前会话最近一条真实消息的发送者
            var sid = GetCurrentSessionId();
            if (sid != null && _lastSenderBySource.TryGetValue(sid, out var sender)
                && (DateTime.Now - sender.Time).TotalMinutes < 30)
                return sender.UserId.ToString();

            // 历史正则兜底
            foreach (var content in ChatBot.ChatHistory.Reverse())
            {
                if (content.Role != Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User) continue;
                var text = content.Content ?? "";
                // 系统消息/定时任务指令：内部调用
                if (text.StartsWith(ChatBot.PokeMessageTag) || text.StartsWith("[System 定时任务指令]"))
                    return "system";
                if (text.StartsWith("[消息来源(")) continue;
                // 群聊消息体格式：[群聊消息(群号,群名)] [QQ(昵称)]:内容
                if (text.Contains("[群聊消息("))
                {
                    var m = Regex.Match(text, @"\]\s*\[(\d{5,})(?:\([^\)]*\))?\]\s*[:：]");
                    if (m.Success) return m.Groups[1].Value;
                    return null;
                }
                // 私聊：源 tag 即发送者
                var mp = Regex.Match(text, @"\[私聊消息\((\d+)\)\]");
                if (mp.Success) return mp.Groups[1].Value;
                return null;
            }
        }
        catch { }
        return null;
    }

    // ==================== OneBot 实时事件（图片登记 + 发送者识别） ====================

    private void TrySubscribeOneBotEvents()
    {
        if (_eventSubscribed) return;
        try
        {
            var client = GetClient();
            if (client == null) return;
            client.EventReceived += OnOneBotEvent;
            _eventSubscribed = true;
            logger.LogInformation("已订阅 OneBot 实时事件（图片登记/发送者识别）");
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "订阅 OneBot 事件失败");
        }
    }

    private void OnOneBotEvent(OneBotBaseEvent oneBotEvent)
    {
        try
        {
            if (oneBotEvent is not OneBotMessageEvent msg) return;
            var sid = msg.MessageType == OneBotMessageType.Group
                ? $"qq:gm:{msg.GroupId}"
                : $"qq:dm:{msg.UserId}";
            _lastSenderBySource[sid] = (msg.UserId, DateTime.Now);

            if (!Configuration.ImageManifestEnabled || Configuration.AutoAttachRecentImage) return;
            var sender = msg.Sender?.Nickname ?? msg.Sender?.Card ?? "";
            foreach (var url in ExtractImageUrls(msg))
                RegisterImage(sid, url, sender, msg.Time, "");
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "处理 OneBot 事件异常");
        }
    }

    /// <summary>从消息中提取图片 URL（CQ 码字符串 / 消息段数组两种形态）</summary>
    private static List<string> ExtractImageUrls(OneBotMessageEvent msg)
    {
        var urls = new List<string>();
        try
        {
            if (msg.Message is string cq)
            {
                foreach (Match m in Regex.Matches(cq, @"\[CQ:image[^\]]*?url=([^\],\]]+)"))
                    urls.Add(QzoneParser.CleanUrl(m.Groups[1].Value));
            }
            else if (msg.Message is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in je.EnumerateArray())
                {
                    if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                        seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                    {
                        var u = QzoneParser.CleanUrl(url.GetString() ?? "");
                        if (!string.IsNullOrEmpty(u)) urls.Add(u);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(msg.RawMessage))
            {
                foreach (Match m in Regex.Matches(msg.RawMessage, @"\[CQ:image[^\]]*?url=([^\],\]]+)"))
                    urls.Add(QzoneParser.CleanUrl(m.Groups[1].Value));
            }
        }
        catch { }
        return urls;
    }

    private void RegisterImage(string sid, string url, string? sender, long time, string? msgId)
    {
        if (string.IsNullOrEmpty(url)) return;
        lock (_imageRegistry)
        {
            if (!_imageRegistry.TryGetValue(sid, out var registry))
            {
                registry = new List<ImageEntry>();
                _imageRegistry[sid] = registry;
            }
            if (registry.Any(e => e.Url == url)) return;
            registry.Add(new ImageEntry { Source = "url", Url = url, Sender = sender, Time = time, MsgId = msgId });
            if (registry.Count > QzoneState.ImageRegistryCap)
                registry.RemoveRange(0, registry.Count - QzoneState.ImageRegistryCap);
        }
    }

    // ==================== 近期图片清单自动注入（PokeSend 过滤器） ====================

    private string OnPokeSendFilter(string message)
    {
        try
        {
            if (!Configuration.ImageManifestEnabled || Configuration.AutoAttachRecentImage) return message;
            string? sid = null;
            var mg = Regex.Match(message, @"\[群聊消息\((\d+)");
            if (mg.Success) sid = $"qq:gm:{mg.Groups[1].Value}";
            else
            {
                var mp = Regex.Match(message, @"\[私聊消息\((\d+)\)\]");
                if (mp.Success) sid = $"qq:dm:{mp.Groups[1].Value}";
            }
            if (sid == null) return message;

            List<ImageEntry> entries;
            lock (_imageRegistry)
            {
                if (!_imageRegistry.TryGetValue(sid, out var registry) || registry.Count == 0)
                    return message;
                entries = registry.TakeLast(Configuration.ImageManifestCount).ToList();
            }

            var lines = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                // 惰性识图：无描述时起后台任务，本轮占位、下轮生效（识图资格与候选资格解耦）
                if (string.IsNullOrEmpty(entry.Desc) && !entry.DescPending)
                {
                    entry.DescPending = true;
                    var e1 = entry;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var desc = await DescribeImageUrlAsync(e1.Url ?? "");
                            if (!string.IsNullOrEmpty(desc)) e1.Desc = desc;
                        }
                        catch { }
                        finally { e1.DescPending = false; }
                    });
                }
                var timeStr = DateTimeOffset.FromUnixTimeSeconds(entry.Time).LocalDateTime.ToString("MM-dd HH:mm");
                var sender = string.IsNullOrEmpty(entry.Sender) ? "未知" : entry.Sender;
                lines.Add($"{i + 1}. [{timeStr} {sender}] {QzoneImagePolicy.CandidateLabel(entry.Desc)}");
            }

            return message + "\n\n[近期图片] 本会话最近出现的图片及内容描述，调用 qzone_publish 发说说时可用 image_indices 参数引用序号配图：\n"
                + string.Join("\n", lines);
        }
        catch { return message; }
    }

    // ==================== 定时任务（cron/interval+抖动，60s防抖，作息黑名单） ====================

    private void SetupScheduledJobs()
    {
        void AddJob(string name, string? expr, Func<Task> run)
        {
            var next = BuildSchedule(expr);
            if (next == null) return;
            var job = new ScheduledJob { Name = name, Run = run, NextAfter = next };
            job.NextRun = next(DateTime.Now);
            _jobs.Add(job);
            logger.LogInformation("定时任务已调度: {Name} ({Expr})，下次执行 {Next}", name, expr, job.NextRun);
        }

        AddJob("自动发布", Configuration.AutoPublishSchedule, AutoPublishJobAsync);
        AddJob("自动评论", Configuration.AutoCommentSchedule, AutoCommentJobAsync);
        if (Configuration.AutoReplyEnabled)
            AddJob("自动回复", Configuration.AutoReplySchedule, AutoReplyJobAsync);

        if (_jobs.Count > 0)
            _scheduleTimer = new Timer(_ => _ = ScheduleTickAsync(), null,
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(15));
    }

    private async Task ScheduleTickAsync()
    {
        var now = DateTime.Now;
        foreach (var job in _jobs)
        {
            if (now < job.NextRun) continue;
            // misfire 容错：错过 300s 以内补跑，超过则跳到下一轮
            if ((now - job.NextRun).TotalSeconds > 300)
            {
                job.NextRun = job.NextAfter(now);
                continue;
            }
            // 60 秒防抖
            if ((now - job.LastExecuted).TotalSeconds < 60) continue;
            job.LastExecuted = now;
            job.NextRun = job.NextAfter(now);

            if (IsInBlackout())
            {
                logger.LogInformation("定时任务 {Name} 处于黑名单时间段，跳过本轮", job.Name);
                continue;
            }
            try
            {
                await job.Run();
            }
            catch (Exception e)
            {
                logger.LogError(e, "定时任务 {Name} 执行失败", job.Name);
            }
        }
    }

    /// <summary>任务指令：注入会话让 AI 以完整人设/记忆自主执行（对齐 Kira 群聊指令模式）</summary>
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
            // 附带场合信息（群名/昵称）
            try
            {
                var client = GetClient();
                if (client != null)
                {
                    if (type == "gm")
                    {
                        var info = await client.CallActionAsync<JsonElement>("get_group_info", new { group_id = long.Parse(id) });
                        var name = "";
                        if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("group_name", out var n))
                            name = n.GetString() ?? "";
                        instruction += string.IsNullOrEmpty(name) ? $"\n（当前场合：群 {id}）" : $"\n（当前场合：群「{name}」{id}）";
                    }
                    else
                    {
                        var info = await client.CallActionAsync<JsonElement>("get_stranger_info", new { user_id = long.Parse(id) });
                        var name = "";
                        if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("nickname", out var n))
                            name = n.GetString() ?? "";
                        instruction += string.IsNullOrEmpty(name) ? $"\n（当前场合：与 {id} 的私聊）" : $"\n（当前场合：与「{name}」{id} 的私聊）";
                    }
                }
            }
            catch { }
        }

        // silent：提示 AI 不要向群/私聊发送回复（Alife 无会话管线事件，用人设层提示实现无痕）
        if (Configuration.TaskMessageStyle.Equals("silent", StringComparison.OrdinalIgnoreCase))
            instruction += "\n（静默任务：直接执行空间操作即可，不要通过QChat/QImage向任何群聊或私聊发送消息）";

        interactor.Poke($"[System 定时任务指令] {instruction}");
        logger.LogInformation("已发送定时任务指令: {Instruction}", instruction[..Math.Min(30, instruction.Length)]);
    }

    private async Task AutoPublishJobAsync()
    {
        try
        {
            await EnsureApiAsync();
            var targetImageCount = QzoneImagePolicy.DrawTarget(Configuration.AutoPublishImageMin, Configuration.AutoPublishImageMax);
            logger.LogInformation("定时自动发布配图目标: target={Target} range={Min}-{Max}",
                targetImageCount, Configuration.AutoPublishImageMin, Configuration.AutoPublishImageMax);

            var hasTargets = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0
                || Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0;
            if (hasTargets)
            {
                var instruction = "【定时任务】请根据最近聊天发布一条说说，自然一点，不要提及这是定时任务。"
                    + QzoneImagePolicy.BuildInstruction(targetImageCount, Configuration.AutoPublishImageMax)
                    + "配图时用image_indices选择，也可用images传聊天记录里见过的图片URL或本地路径。";
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
        try
        {
            await EnsureApiAsync();
            var hasTargets = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0
                || Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0;
            if (hasTargets)
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
        try
        {
            await EnsureApiAsync();
            var hasTargets = Configuration.TaskGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0
                || Configuration.TaskPrivateIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0;
            if (hasTargets)
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

    // ==================== 后台直接生成模式（Legacy） ====================

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
        if (_state.MyPostsHistory.Count > 0)
        {
            var historyStr = string.Join("\n", _state.MyPostsHistory.TakeLast(5).Select(p => $"- {p}"));
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
            prompt = "请生成一条QQ空间说说，内容可以是心情、日常、段子，20-50字，要符合你的人设。";
        }

        var imageUrls = new List<string>();
        var shouldOfferImages = Configuration.AutoPublishImageProb > 0 && Random.Shared.NextDouble() < Configuration.AutoPublishImageProb;
        if (shouldOfferImages && !string.IsNullOrEmpty(sourceId))
        {
            try
            {
                var candidates = await FetchRecentImagesAsync(sourceType, sourceId, Math.Max(Configuration.AutoPublishImageMax, targetImageCount));
                candidates = QzoneImagePolicy.DedupeSources(candidates).Where(u => !IsRecentlyPublishedImage(u)).ToList();
                if (candidates.Count > 0)
                {
                    var descLines = new List<string>();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var desc = await DescribeImageUrlAsync(candidates[i]);
                        descLines.Add($"{i + 1}. {QzoneImagePolicy.CandidateLabel(desc)}");
                    }
                    if (descLines.Count > 0)
                    {
                        var choiceRule = targetImageCount > 0
                            ? $"本次必须选择恰好{targetImageCount}个不同序号；候选不足时选择全部可用候选。"
                            : $"可按内容自主选择0至{Configuration.AutoPublishImageMax}个不同序号；不适合配图时不要输出 IMG 行。";
                        prompt += "\n\n以下是最近聊天中出现的图片及内容描述：\n" + string.Join("\n", descLines) + "\n" + choiceRule + "正文后另起一行输出 IMG:序号 或 IMG:序号,序号。";
                        var textWithChoice = await CallLlmAsync(prompt, systemPrompt);
                        var (text, chosen) = SplitImgChoices(textWithChoice);
                        if (chosen.Count > 0 || targetImageCount == 0 || Configuration.AutoPublishImageFallback)
                            imageUrls = QzoneImagePolicy.ResolveDescribedSources(candidates, chosen, targetImageCount, Configuration.AutoPublishImageMax);
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
            _state.MyPostsHistory.Add(finalText);
            RecordPublishedImages(imageUrls);
            _state.Save();
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
                // 可选：评论前先识图
                if (Configuration.AutoCommentImageDesc && post.Images.Count > 0)
                {
                    var desc = await DescribeImageUrlAsync(post.Images[0]);
                    if (!string.IsNullOrEmpty(desc))
                        prompt += $"\n该说说配图内容：{desc}";
                }
                var commentText = await CallLlmAsync(prompt, "");
                if (string.IsNullOrEmpty(commentText)) continue;
                try
                {
                    var result = await CommentAsync(post, commentText);
                    logger.LogInformation("自动评论成功: {Tid} -> {Text}", post.Tid, commentText);
                    if (Configuration.LikeWhenComment && !result.Contains("未重复提交"))
                        await AutoLikeAfterCommentAsync(post);
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
                    // 组合键去重：说说+评论ID+作者UIN（主评论与楼中回复 ID 可重复）
                    var replyKey = $"{fullPost.Tid}:{comment.Tid}:{comment.Uin}";
                    if (_state.RepliedComments.Contains(replyKey)) continue;

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
                    _state.RepliedComments.Add(replyKey);
                    _state.Save();
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
        var dedupeInterval = ParseIntervalSeconds(Configuration.AutoPublishImageDedupeInterval, "h");
        if (dedupeInterval <= 0) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return _state.PublishedImageHistory.Any(h => h.Identity == url && h.Time > now - dedupeInterval);
    }

    /// <summary>仅发布成功后记录配图指纹（对齐 Kira：去重仅成功后记录）</summary>
    private void RecordPublishedImages(List<string> urls)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var url in urls)
        {
            var identity = _urlMd5.GetValueOrDefault(url) ?? url;
            _state.PublishedImageHistory.Add(new QzoneState.PublishedImageRecord { Identity = identity, Time = now });
        }
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
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("messages", out var msgs))
        {
            foreach (var m in msgs.EnumerateArray()) messages.Add(m);
        }

        if (Configuration.ImageManifestEnabled && !Configuration.AutoAttachRecentImage)
        {
            var sid = $"qq:{(sourceType == "group" ? "gm" : "dm")}:{sourceId}";
            foreach (var msg in messages)
            {
                var sender = "";
                if (msg.TryGetProperty("sender", out var s) && s.TryGetProperty("nickname", out var n))
                    sender = n.GetString() ?? "";
                var msgId = msg.TryGetProperty("message_id", out var mid) ? mid.ToString() : "";
                var timestamp = msg.TryGetProperty("time", out var t) && t.TryGetInt64(out var tv)
                    ? tv : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (msg.TryGetProperty("message", out var segs) && segs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in segs.EnumerateArray())
                    {
                        if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                            seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                        {
                            RegisterImage(sid, QzoneParser.CleanUrl(url.GetString() ?? ""), sender, timestamp, msgId);
                        }
                    }
                }
            }
        }
        return messages;
    }

    private async Task<List<string>> FetchRecentImagesAsync(string sourceType, string sourceId, int maxCount)
    {
        var messages = await FetchHistoryMessagesAsync(sourceType, sourceId, 20);
        var urls = new List<string>();
        foreach (var msg in messages.AsEnumerable().Reverse())
        {
            if (msg.TryGetProperty("message", out var segs) && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                        seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                    {
                        var u = QzoneParser.CleanUrl(url.GetString() ?? "");
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
        if (msg.TryGetProperty("message", out var segs) && segs.ValueKind == JsonValueKind.Array)
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

    /// <summary>
    /// 描述图片内容：md5 全局缓存优先（命中零 VLM 调用，省 token）；
    /// 未命中下载 → VLM 识图 → 写缓存。临时文件即用即删。
    /// </summary>
    private async Task<string> DescribeImageUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        try
        {
            var bytes = await QzoneParser.DownloadImageAsync(url);
            if (bytes == null || bytes.Length == 0) return "";
            var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLower();
            _urlMd5[url] = md5;

            // 全局 md5 缓存：命中直接返回
            var cached = _state.GetImageDesc(md5);
            if (!string.IsNullOrEmpty(cached)) return cached;

            if (visionModel == null)
                return $"[图片 {md5[..8]}]";
            var tempPath = Path.Combine(Path.GetTempPath(), $"qzone_img_{md5[..8]}.jpg");
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes);
                var desc = await visionModel.QueryAsync(tempPath, "请精简的描述一下图片大体内容，避免输出过多的文本", 64);
                if (!string.IsNullOrEmpty(desc))
                {
                    _state.SetImageDesc(md5, desc);
                    _state.Save();
                    return desc;
                }
                return $"[图片 {md5[..8]}]";
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "图片描述失败: {Url}", url);
            return "";
        }
    }

    /// <summary>URL 过期续命：QQ 多媒体 rkey 约 1 小时过期，按 msg_id 调 get_msg 换新签名 URL</summary>
    private async Task<bool> TryRefreshImageUrlAsync(ImageEntry entry)
    {
        if (string.IsNullOrEmpty(entry.MsgId)) return false;
        try
        {
            var client = GetClient();
            if (client == null) return false;
            var result = await client.CallActionAsync<JsonElement>("get_msg", new { message_id = long.Parse(entry.MsgId) });
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message", out var segs)
                && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    if (seg.TryGetProperty("type", out var tp) && tp.GetString() == "image" &&
                        seg.TryGetProperty("data", out var data) && data.TryGetProperty("url", out var url))
                    {
                        var u = QzoneParser.CleanUrl(url.GetString() ?? "");
                        if (!string.IsNullOrEmpty(u) && u != entry.Url)
                        {
                            entry.Url = u;
                            logger.LogInformation("图片 URL 已通过 get_msg 续命 (msgId={MsgId})", entry.MsgId);
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "图片 URL 续命失败 (msgId={MsgId})", entry.MsgId);
        }
        return false;
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

    // ==================== 数据获取与评论 ====================

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

    /// <summary>评论说说：提交前本地幂等检查（零 token），接口成功即成功，不做回读误判</summary>
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

        var resp = await _api!.CommentAsync(post.Uin, post.Tid, content ?? "");
        if (!resp.Ok) throw new Exception($"评论接口失败: {resp.Message}");

        // 诊断模式：提交后回读确认（仅日志，不阻断）
        if (Configuration.CommentVerify)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    var verify = await _api!.GetDetailAsync(post.Uin, post.Tid);
                    if (verify.Ok)
                    {
                        var parsed = QzoneParser.ParseFeeds(new List<object?> { verify.Data });
                        var found = parsed.Count > 0 && parsed[0].Comments.Any(c =>
                            c.Uin == _myUin && Regex.Replace(c.Content ?? "", @"\s+", "") == Regex.Replace(content ?? "", @"\s+", ""));
                        if (!found)
                            logger.LogWarning("评论回读未找到（可能被风控，通常约24h恢复）: {Tid} code={Code} msg={Msg}", post.Tid, resp.Code, resp.Message);
                    }
                }
                catch { }
            });
        }
        return "评论成功";
    }

    /// <summary>评论后自动点赞：随机延迟错开特征；已赞跳过；详情缺 uin 时回填 target 防 unikey 拼错</summary>
    private async Task AutoLikeAfterCommentAsync(QzonePost post)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.NextDouble() * Configuration.LikeDelayJitter + Configuration.LikeDelayMin));
            var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
            if (detailResp.Ok)
            {
                var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                if (parsed.Count > 0)
                {
                    if (parsed[0].IsLiked)
                    {
                        logger.LogDebug("说说已赞过，跳过自动点赞: {Tid}", post.Tid);
                        return;
                    }
                    if (parsed[0].CreateTime > 0) post.CreateTime = parsed[0].CreateTime;
                }
            }
            var likeResp = await _api!.LikeAsync(post, post.CreateTime);
            if (likeResp.Ok) logger.LogInformation("自动点赞成功: {Tid}", post.Tid);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "评论后自动点赞失败: {Tid}", post.Tid);
        }
    }

    // ==================== 生命周期 ====================

    protected override Task OnAwake()
    {
        XmlHandler xmlHandler = new(this) {
            Description = "提供QQ空间说说发布、查看、点赞、评论、回复、删除、访客统计、图片识图、定时任务等功能"
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

        _state = new QzoneState(logger);
        _state.Load();

        // 会话与传输层：Cookie 原地刷新，失效自救回调挂传输层
        _session = new QzoneSession(() => Configuration.CookiesStr);
        _httpClient = new QzoneHttpClient(Configuration.Timeout, logger, () => _session.GetCtx());
        _httpClient.OnAuthExpired = () => RefreshCookieAsync(force: true);
        _api = new QzoneApi(_session, _httpClient);

        // 近期图片清单注入（PokeSend 过滤器，随 QQ 消息一并送达 AI）
        ChatBot.PokeSend += OnPokeSendFilter;

        return Task.CompletedTask;
    }

    protected override async Task OnStart()
    {
        // 全部模块 Awake 完毕（QChat 的 OneBotClient 已创建），订阅实时事件
        TrySubscribeOneBotEvents();

        SetupScheduledJobs();

        // Cookie 周期刷新（±10% 抖动，最小 300s）
        var refreshInterval = ParseIntervalSeconds(Configuration.CookieRefreshInterval, "h");
        if (Configuration.AutoRefreshCookie && refreshInterval > 0)
        {
            var interval = Math.Max(300, refreshInterval);
            _cookieRefreshTimer = new Timer(_ =>
            {
                _ = RefreshCookieAsync(false);
                // 每次触发后重排下次间隔（±10% 抖动）
                try
                {
                    var jitter = interval * (0.9 + Random.Shared.NextDouble() * 0.2);
                    _cookieRefreshTimer?.Change(TimeSpan.FromSeconds(jitter), TimeSpan.FromSeconds(jitter));
                }
                catch { }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(interval));
        }

        // 启动即尝试获取 Cookie；失败进入后台自愈（15/30/60/120s 递增重试，不判死）
        if (Configuration.AutoRefreshCookie)
        {
            try
            {
                if (!await RefreshCookieAsync(force: true))
                    StartCookieSelfHeal();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "启动时获取 Cookie 失败，进入后台自愈");
                StartCookieSelfHeal();
            }
        }
        else if (string.IsNullOrEmpty(Configuration.CookiesStr))
        {
            logger.LogWarning("未启用自动刷新且未配置 Cookie 字符串，空间功能不可用");
        }
    }

    protected override Task OnDestroy()
    {
        ChatBot.PokeSend -= OnPokeSendFilter;
        try
        {
            var client = GetClient();
            if (client != null && _eventSubscribed)
                client.EventReceived -= OnOneBotEvent;
        }
        catch { }
        _cookieRefreshTimer?.Dispose();
        _scheduleTimer?.Dispose();
        _httpClient.Dispose();
        _state?.Save();
        return Task.CompletedTask;
    }

    // ==================== 工具函数 ====================

    [XmlFunction(FunctionMode.OneShot)]
    [Description("发布一条说说到自己的QQ空间。配图方式：1) images参数传聊天中出现过的图片URL或本地路径；2) 先调用qzone_image_manifest获取[近期图片]清单，再用image_indices引用序号（QQ消息到来时清单也会随消息自动附上）。优先使用你真正了解内容的方式配图；都不传时默认纯文字发布。")]
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
                    interactor.Poke("未能从[近期图片]清单解析出图片（清单为空或序号超出范围），说说未发布。如确认发纯文字，请不带image_indices重试；如想配图，可改用images参数传图片URL或本地路径。");
                    return;
                }
                imgList.AddRange(resolved);
            }

            // 吸附模式：未指定图片时盲抓最近一张图
            if (imgList.Count == 0 && Configuration.AutoAttachRecentImage)
            {
                var sid = GetCurrentSessionId();
                if (sid != null)
                {
                    lock (_imageRegistry)
                    {
                        if (_imageRegistry.TryGetValue(sid, out var registry) && registry.Count > 0)
                        {
                            var last = registry[^1].Url;
                            if (!string.IsNullOrEmpty(last)) imgList.Add(last);
                        }
                    }
                }
            }

            var result = await _api!.PublishAsync(text, imgList, allowImageDrop: imgList.Count > 0);
            if (result.Ok)
            {
                _state.MyPostsHistory.Add(text);
                RecordPublishedImages(imgList);
                _state.Save();
                var msg = $"发布成功 tid={result.Data.GetValueOrDefault("tid")}";
                if (!string.IsNullOrEmpty(result.Message)) msg += $"（{result.Message}）";
                interactor.Poke(msg);
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

                // 拉点赞列表（含 is_dolike 与自己昵称补偿）
                try
                {
                    var likeResp = await _api!.GetLikeListAsync(p);
                    if (likeResp.Ok)
                    {
                        var likeData = likeResp.Data;
                        var likeUsers = new List<string>();
                        var likeUins = new List<string>();
                        if (likeData.GetValueOrDefault("like_uin_info") is List<object?> uinList)
                        {
                            foreach (var u in uinList)
                            {
                                if (u is Dictionary<string, object?> uinDict)
                                {
                                    var nick = uinDict.GetValueOrDefault("nick")?.ToString() ?? uinDict.GetValueOrDefault("fuin")?.ToString();
                                    var fuin = uinDict.GetValueOrDefault("fuin")?.ToString() ?? uinDict.GetValueOrDefault("uin")?.ToString() ?? "";
                                    if (!string.IsNullOrEmpty(nick)) likeUsers.Add(nick);
                                    if (!string.IsNullOrEmpty(fuin)) likeUins.Add(fuin);
                                }
                            }
                        }
                        var total = Convert.ToInt32(likeData.GetValueOrDefault("total_number") ?? likeUsers.Count);
                        var isDolike = likeData.GetValueOrDefault("is_dolike") is 1 or true or "1" or 1L;
                        // 自己已赞但列表不含自己时，补真实昵称（我）：OneBot get_stranger_info 优先，cgi_personal_card 回退
                        if ((isDolike || p.IsLiked) && _myUin != 0 && !likeUins.Contains(_myUin.ToString()))
                        {
                            var myNick = await GetMyNicknameAsync();
                            if (!string.IsNullOrEmpty(myNick)) likeUsers.Insert(0, $"{myNick}（我）");
                        }
                        if (total > 0 || likeUsers.Count > 0)
                        {
                            p.LikeCount = Math.Max(total, likeUsers.Count);
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
                    var shown = p.Comments.Take(Math.Max(1, Configuration.ViewCommentMax)).ToList();
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

    private string _myNickname = "";

    /// <summary>获取自己真实昵称（防「我」昵称诈骗）：OneBot get_stranger_info 优先，cgi_personal_card 回退</summary>
    private async Task<string> GetMyNicknameAsync()
    {
        if (!string.IsNullOrEmpty(_myNickname)) return _myNickname;
        if (_myUin == 0) return "";
        try
        {
            var client = GetClient();
            if (client != null)
            {
                var info = await client.CallActionAsync<JsonElement>("get_stranger_info", new { user_id = _myUin });
                if (info.ValueKind == JsonValueKind.Object && info.TryGetProperty("nickname", out var n))
                {
                    _myNickname = n.GetString() ?? "";
                    if (!string.IsNullOrEmpty(_myNickname)) return _myNickname;
                }
            }
        }
        catch { }
        try
        {
            var resp = await _api!.GetUserInfoAsync(_myUin);
            if (resp.Ok)
                _myNickname = resp.Data.GetValueOrDefault("nickname")?.ToString()
                    ?? resp.Data.GetValueOrDefault("nick")?.ToString()
                    ?? resp.Data.GetValueOrDefault("name")?.ToString() ?? "";
        }
        catch { }
        return _myNickname;
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

            // 先拉详情：取发布时间（abstime 必需）与已赞状态（防重复点赞）
            try
            {
                var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
                if (detailResp.Ok)
                {
                    var parsed = QzoneParser.ParseFeeds(new List<object?> { detailResp.Data });
                    if (parsed.Count > 0)
                    {
                        if (parsed[0].CreateTime > 0) post.CreateTime = parsed[0].CreateTime;
                        post.IsLiked = parsed[0].IsLiked;
                        if (!string.IsNullOrEmpty(parsed[0].LikeKey)) post.LikeKey = parsed[0].LikeKey;
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "点赞前拉详情失败（继续提交）");
            }

            if (action == "unlike")
            {
                // 取消不做拦截（isliked 滞后，幂等无害）
                var resp = await _api!.LikeAsync(post, post.CreateTime, unlike: true);
                interactor.Poke(resp.Ok ? "取消点赞成功" : $"取消点赞失败：{resp.Message}");
                return;
            }

            if (post.IsLiked)
            {
                interactor.Poke("这条说说已经赞过了。");
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
    [Description("评论指定的说说。content 不传时根据说说内容自动生成一条简洁评论。")]
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

            var finalContent = content ?? "";
            if (string.IsNullOrEmpty(finalContent))
            {
                // 拉详情生成评论，失败兜底
                string prompt;
                try
                {
                    var detailResp = await _api!.GetDetailAsync(post.Uin, post.Tid);
                    var parsed = detailResp.Ok ? QzoneParser.ParseFeeds(new List<object?> { detailResp.Data }) : new List<QzonePost>();
                    prompt = parsed.Count > 0
                        ? $"根据以下说说内容，生成一条简洁评论（0-15字）：\n{parsed[0].Text}"
                        : "为这条说说生成一条简洁评论（0-15字）";
                }
                catch { prompt = "为这条说说生成一条简洁评论（0-15字）"; }
                finalContent = await CallLlmAsync(prompt, "");
                if (string.IsNullOrEmpty(finalContent)) finalContent = "赞一个！";
            }

            var result = await CommentAsync(post, finalContent);
            if (Configuration.LikeWhenComment && !result.Contains("未重复提交"))
            {
                await AutoLikeAfterCommentAsync(post);
                result += "（已尝试顺带点赞）";
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
            var matches = fullPost.Comments.Where(c => c.Tid.ToString() == commentId || c.CommentId == commentId).ToList();
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
    [Description("查看自己QQ空间最近访客和访客统计。返回最近访客明细、来源、隐身/黄钻状态，以及今日和最近30天访客数。仅支持查看自己的空间。")]
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
                interactor.Poke("图片识别失败（下载失败或识图模型不可用，链接可能已过期）");
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
            List<ImageEntry> entries;
            lock (_imageRegistry)
            {
                if (!_imageRegistry.TryGetValue(sid, out var registry) || registry.Count == 0)
                {
                    // 尝试从历史消息拉取
                    try
                    {
                        var (type, id) = ParseSessionId(sid);
                        if (!string.IsNullOrEmpty(id))
                            FetchHistoryMessagesAsync(type, id, 20).Wait(5000);
                    }
                    catch { }
                    registry = _imageRegistry.GetValueOrDefault(sid) ?? new();
                }
                entries = registry.TakeLast(Configuration.ImageManifestCount).ToList();
            }
            if (entries.Count == 0)
            {
                interactor.Poke("当前会话暂无近期图片，可改用images参数传图片URL或本地路径");
                return;
            }
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
                lines.Add($"{i + 1}. [{timeStr} {sender}] {QzoneImagePolicy.CandidateLabel(desc)}");
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
        if (string.IsNullOrEmpty(sid)) return result;
        List<ImageEntry> entries;
        lock (_imageRegistry)
        {
            if (!_imageRegistry.TryGetValue(sid, out var registry)) return result;
            entries = registry.TakeLast(Configuration.ImageManifestCount).ToList();
        }
        foreach (var part in imageIndices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var idx)) continue;
            if (idx < 1 || idx > entries.Count) continue;
            var url = entries[idx - 1].Url;
            if (string.IsNullOrEmpty(url) || result.Contains(url)) continue;
            result.Add(url);
        }
        await Task.CompletedTask;
        return result;
    }

    private static (string Type, string Id) ParseSessionId(string sid)
    {
        var parts = sid.Split(':');
        if (parts.Length >= 3)
            return (parts[1] == "gm" ? "group" : "private", parts[2]);
        return ("", "");
    }
}
