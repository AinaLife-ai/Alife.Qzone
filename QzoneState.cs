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
/// - state.json：replied_comments(≤1000) / my_posts_history(≤10) / published_image_history(≤20)
/// - image_desc_cache.json：md5 → 图片描述（含命中计数/最后命中时间），识图前先查缓存，命中零 VLM 调用
/// 存储位置：{存储目录}/PluginData/AinaLife.Qzone/
/// </summary>
public class QzoneState
{
    public const int MaxRepliedCache = 1000;
    public const int MaxHistory = 10;
    public const int ImageRegistryCap = 20;
    private const int MaxDescCache = 200;

    private readonly string _dir;
    private readonly ILogger _logger;

    public HashSet<string> RepliedComments { get; } = new();
    public List<string> MyPostsHistory { get; } = new();
    public List<PublishedImageRecord> PublishedImageHistory { get; } = new();

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

    private string StatePath => Path.Combine(_dir, "state.json");
    private string DescCachePath => Path.Combine(_dir, "image_desc_cache.json");

    public void Load()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(StatePath, Encoding.UTF8));
                var root = doc.RootElement;
                if (root.TryGetProperty("replied_comments", out var replied))
                    foreach (var item in replied.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) RepliedComments.Add(s);
                    }
                if (root.TryGetProperty("my_posts_history", out var history))
                    foreach (var item in history.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) MyPostsHistory.Add(s);
                    }
                if (root.TryGetProperty("published_image_history", out var pubHistory))
                    foreach (var item in pubHistory.EnumerateArray())
                    {
                        var rec = new PublishedImageRecord();
                        if (item.TryGetProperty("identity", out var id)) rec.Identity = id.GetString() ?? "";
                        if (item.TryGetProperty("time", out var t) && t.TryGetInt64(out var tv)) rec.Time = tv;
                        if (!string.IsNullOrEmpty(rec.Identity)) PublishedImageHistory.Add(rec);
                    }
                _logger.LogInformation("已加载持久化状态：历史说说 {HistoryCount} 条，已回复评论 {RepliedCount} 条",
                    MyPostsHistory.Count, RepliedComments.Count);
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
                    foreach (var (k, v) in dict)
                        if (!string.IsNullOrEmpty(v.Desc)) _descCache[k] = v;
                _logger.LogInformation("已加载图片描述缓存 {Count} 条", _descCache.Count);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "加载图片描述缓存失败");
        }
    }

    public void Save()
    {
        try
        {
            var data = new Dictionary<string, object>
            {
                ["replied_comments"] = RepliedComments.TakeLast(MaxRepliedCache).ToList(),
                ["my_posts_history"] = MyPostsHistory.TakeLast(MaxHistory).ToList(),
                ["published_image_history"] = PublishedImageHistory.TakeLast(ImageRegistryCap).ToList()
            };
            File.WriteAllText(StatePath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存插件状态失败");
        }

        try
        {
            // 超出上限时淘汰最久未命中的条目
            while (_descCache.Count > MaxDescCache)
            {
                var oldest = _descCache.OrderBy(kv => kv.Value.LastSeen).First().Key;
                _descCache.Remove(oldest);
            }
            File.WriteAllText(DescCachePath,
                JsonSerializer.Serialize(_descCache, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
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
        if (_descCache.TryGetValue(md5, out var entry))
        {
            entry.Count++;
            entry.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return entry.Desc;
        }
        return null;
    }

    public void SetImageDesc(string md5, string desc)
    {
        if (string.IsNullOrEmpty(md5) || string.IsNullOrEmpty(desc)) return;
        if (_descCache.TryGetValue(md5, out var entry))
        {
            entry.Desc = desc;
            entry.Count++;
            entry.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        else
        {
            _descCache[md5] = new DescCacheEntry
            {
                Desc = desc,
                Count = 1,
                LastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }
}
