using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Alife.Foundation;
using Microsoft.Extensions.Logging;

namespace AinaLife.Qzone;

/// <summary>
/// 插件状态持久化（对齐 Kira state.json + image_desc_cache）：
/// - state.json：replied_comments(≤1000) / commented_posts(≤1000) / my_posts_history(≤10) / published_image_history(≤500)
/// - image_desc_cache.json：md5 → 图片描述（含命中计数/最后命中时间），识图前先查缓存，命中零 VLM 调用
/// 存储位置：{存储目录}/PluginData/AinaLife.Qzone/
///
/// 4.4.1 加固：
/// - 全部读写走内部锁（此前是裸 HashSet/List，AI 工具线程与定时任务线程并发写会抛「集合已修改」并被吞掉 ⇒ 状态静默丢失）
/// - 新增 commented_posts（已评论过的说说 uin:tid → 时间），跨轮次/跨重启防止对同一条说说反复评论
/// - 配图去重记录容量由 20 提升到 500（对齐 Kira：一条说说配 3 张图时，20 条几小时就挤没了，"3 天去重"形同虚设）
/// - Save 先取快照再序列化，避免序列化过程中集合被并发修改
/// </summary>
public class QzoneState
{
    public const int MaxRepliedCache = 1000;
    public const int MaxCommentedCache = 1000;
    public const int MaxHistory = 10;
    public const int ImageRegistryCap = 500;
    private const int MaxDescCache = 200;

    private readonly string _dir;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private readonly HashSet<string> _repliedComments = new();
    private readonly Dictionary<string, long> _commentedPosts = new();   // "uin:tid" → unix 秒
    private readonly List<string> _myPostsHistory = new();
    private readonly List<PublishedImageRecord> _publishedImageHistory = new();
    private readonly Dictionary<string, DescCacheEntry> _descCache = new();

    public class PublishedImageRecord
    {
        public string Identity { get; set; } = "";
        public long Time { get; set; }
    }

    private class DescCacheEntry
    {
        public string Desc { get; set; } = "";
        public int Count { get; set; }
        public long LastSeen { get; set; }
    }

    public QzoneState(ILogger logger)
    {
        _logger = logger;
        _dir = Path.Combine(AlifePath.StorageFolderPath, "PluginData", "AinaLife.Qzone");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>状态文件序列化选项：驼峰命名——与读取端（identity/time）大小写保持一致。
    /// 旧版本写的是 PascalCase 而读取用小写，导致 published_image_history 重启后静默丢失（本次修复）。</summary>
    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>大小写不敏感取值（兼容历史文件两种写法）</summary>
    private static bool TryGetIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        return false;
    }

    private string StatePath => Path.Combine(_dir, "state.json");
    private string DescCachePath => Path.Combine(_dir, "image_desc_cache.json");

    // ==================== 对外（线程安全）====================

    public int RepliedCount { get { lock (_lock) return _repliedComments.Count; } }
    public int CommentedCount { get { lock (_lock) return _commentedPosts.Count; } }
    public int PublishedImageCount { get { lock (_lock) return _publishedImageHistory.Count; } }

    public bool HasReplied(string key) { lock (_lock) return _repliedComments.Contains(key); }
    public void AddReplied(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (_lock) _repliedComments.Add(key);
    }

    /// <summary>该说说是否已评论过（P2：跨轮次/跨重启防重复评论）</summary>
    public bool HasCommented(long uin, string tid)
    {
        if (uin == 0 || string.IsNullOrEmpty(tid)) return false;
        lock (_lock) return _commentedPosts.ContainsKey($"{uin}:{tid}");
    }

    public void MarkCommented(long uin, string tid)
    {
        if (uin == 0 || string.IsNullOrEmpty(tid)) return;
        var key = $"{uin}:{tid}";
        lock (_lock)
        {
            _commentedPosts[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_commentedPosts.Count > MaxCommentedCache)
            {
                foreach (var k in _commentedPosts.OrderBy(kv => kv.Value)
                             .Take(_commentedPosts.Count - MaxCommentedCache).Select(kv => kv.Key).ToList())
                    _commentedPosts.Remove(k);
            }
        }
    }

    public void AddMyPost(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            _myPostsHistory.Add(text);
            if (_myPostsHistory.Count > MaxHistory)
                _myPostsHistory.RemoveRange(0, _myPostsHistory.Count - MaxHistory);
        }
    }

    /// <summary>最近发布过的说说文案（供提示词去重）</summary>
    public List<string> RecentMyPosts(int count)
    {
        lock (_lock) return _myPostsHistory.TakeLast(count).ToList();
    }

    public bool IsImageRecentlyPublished(string identity, long windowSeconds)
    {
        if (string.IsNullOrEmpty(identity) || windowSeconds <= 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
            return _publishedImageHistory.Any(h => h.Identity == identity && h.Time > now - windowSeconds);
    }

    public void AddPublishedImages(IEnumerable<string> identities)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            foreach (var id in identities)
            {
                if (string.IsNullOrEmpty(id)) continue;
                _publishedImageHistory.Add(new PublishedImageRecord { Identity = id, Time = now });
            }
            if (_publishedImageHistory.Count > ImageRegistryCap)
                _publishedImageHistory.RemoveRange(0, _publishedImageHistory.Count - ImageRegistryCap);
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(StatePath, Encoding.UTF8));
                var root = doc.RootElement;
                lock (_lock)
                {
                    if (root.TryGetProperty("replied_comments", out var replied))
                        foreach (var item in replied.EnumerateArray())
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrEmpty(s)) _repliedComments.Add(s);
                        }
                    if (root.TryGetProperty("commented_posts", out var commented))
                        foreach (var item in commented.EnumerateArray())
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrEmpty(s)) _commentedPosts[s] = 0;
                        }
                    if (root.TryGetProperty("my_posts_history", out var history))
                        foreach (var item in history.EnumerateArray())
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrEmpty(s)) _myPostsHistory.Add(s);
                        }
                    if (root.TryGetProperty("published_image_history", out var pubHistory))
                        foreach (var item in pubHistory.EnumerateArray())
                        {
                            var rec = new PublishedImageRecord();
                            if (TryGetIgnoreCase(item, "identity", out var id)) rec.Identity = id.GetString() ?? "";
                            if (TryGetIgnoreCase(item, "time", out var t) && t.TryGetInt64(out var tv)) rec.Time = tv;
                            if (!string.IsNullOrEmpty(rec.Identity)) _publishedImageHistory.Add(rec);
                        }
                }
                _logger.LogInformation("已加载持久化状态：历史说说 {HistoryCount} 条，已回复评论 {RepliedCount} 条，已评论说说 {CommentedCount} 条",
                    RecentMyPosts(MaxHistory).Count, RepliedCount, CommentedCount);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "加载插件状态失败");
        }

        try
        {
            if (File.Exists(DescCachePath))
            {
                var json = File.ReadAllText(DescCachePath, Encoding.UTF8);
                var dict = JsonSerializer.Deserialize<Dictionary<string, DescCacheEntry>>(json);
                if (dict != null)
                {
                    lock (_lock)
                        foreach (var (k, v) in dict)
                            if (!string.IsNullOrEmpty(v.Desc)) _descCache[k] = v;
                }
                _logger.LogInformation("已加载图片描述缓存 {Count} 条", DescCacheSize);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "加载图片描述缓存失败");
        }
    }

    private int DescCacheSize { get { lock (_lock) return _descCache.Count; } }

    public void Save()
    {
        // 快照式保存：先在锁内取副本，再在锁外序列化/写盘，避免并发修改与长锁
        Dictionary<string, object> data;
        Dictionary<string, DescCacheEntry> descSnapshot;
        lock (_lock)
        {
            data = new Dictionary<string, object>
            {
                ["replied_comments"] = _repliedComments.TakeLast(MaxRepliedCache).ToList(),
                ["commented_posts"] = _commentedPosts.OrderByDescending(kv => kv.Value)
                    .Take(MaxCommentedCache).Select(kv => kv.Key).ToList(),
                ["my_posts_history"] = _myPostsHistory.TakeLast(MaxHistory).ToList(),
                ["published_image_history"] = _publishedImageHistory.TakeLast(ImageRegistryCap).ToList()
            };
            while (_descCache.Count > MaxDescCache)
            {
                var oldest = _descCache.OrderBy(kv => kv.Value.LastSeen).First().Key;
                _descCache.Remove(oldest);
            }
            descSnapshot = new Dictionary<string, DescCacheEntry>(_descCache);
        }

        try
        {
            File.WriteAllText(StatePath,
                JsonSerializer.Serialize(data, StateJsonOptions), Encoding.UTF8);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存插件状态失败");
        }

        try
        {
            File.WriteAllText(DescCachePath,
                JsonSerializer.Serialize(descSnapshot, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存图片描述缓存失败");
        }
    }

    /// <summary>查询图片描述缓存（命中时更新计数与最后命中时间）</summary>
    public string? GetImageDesc(string md5)
    {
        if (string.IsNullOrEmpty(md5)) return null;
        lock (_lock)
        {
            if (_descCache.TryGetValue(md5, out var entry))
            {
                entry.Count++;
                entry.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return entry.Desc;
            }
        }
        return null;
    }

    public void SetImageDesc(string md5, string desc)
    {
        if (string.IsNullOrEmpty(md5) || string.IsNullOrEmpty(desc)) return;
        lock (_lock)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_descCache.TryGetValue(md5, out var entry))
            {
                entry.Desc = desc;
                entry.Count++;
                entry.LastSeen = now;
            }
            else
            {
                _descCache[md5] = new DescCacheEntry { Desc = desc, Count = 1, LastSeen = now };
            }
        }
    }

    /// <summary>配图去重身份归一（D）：去掉查询串/首尾空白与包裹引号，避免同一张图换签名后认不出</summary>
    public static string NormalizeImageIdentity(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var s = url.Trim().Trim('"', '\'');
        int q = s.IndexOf('?');
        if (q > 0) s = s[..q];
        return s.ToLowerInvariant();
    }
}
