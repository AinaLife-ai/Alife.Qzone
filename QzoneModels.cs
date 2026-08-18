using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alife.Demo.Plugin.Qzone;

/// <summary>统一接口响应结果</summary>
public class ApiResponse
{
    public bool Ok { get; }
    public int Code { get; }
    public string? Message { get; }
    public Dictionary<string, object?> Data { get; }
    public Dictionary<string, object?> Raw { get; }

    public ApiResponse(bool ok, int code, string? message, Dictionary<string, object?> data, Dictionary<string, object?> raw)
    {
        Ok = ok;
        Code = code;
        Message = message;
        Data = data;
        Raw = raw;
    }

    public static ApiResponse FromRaw(Dictionary<string, object?> raw, string codeKey = "code", string[]? msgKeys = null, string? dataKey = null, int successCode = 0)
    {
        msgKeys ??= new[] { "message", "msg" };
        int code = -1;
        if (raw.TryGetValue(codeKey, out var codeVal))
            code = Convert.ToInt32(codeVal);

        string? message = null;
        foreach (var k in msgKeys)
        {
            if (raw.TryGetValue(k, out var v) && v != null)
            {
                message = v.ToString();
                break;
            }
        }
        if (message == null && raw.TryGetValue("data", out var dataObj) && dataObj is Dictionary<string, object?> dataDict)
        {
            foreach (var k in msgKeys)
            {
                if (dataDict.TryGetValue(k, out var v) && v != null)
                {
                    message = v.ToString();
                    break;
                }
            }
        }
        message ??= code.ToString();

        if (code == successCode)
        {
            Dictionary<string, object?> data;
            if (dataKey == null)
            {
                data = new Dictionary<string, object?>(raw);
                data.Remove("__qzone_internal__");
            }
            else
            {
                data = raw.TryGetValue(dataKey, out var d) && d is Dictionary<string, object?> dd
                    ? dd
                    : new Dictionary<string, object?>();
            }
            return new ApiResponse(true, code, null, data, raw);
        }

        return new ApiResponse(false, code, message, new Dictionary<string, object?>(), raw);
    }

    public object? Get(string key, object? def = null)
    {
        if (!Ok || Data == null) return def;
        return Data.TryGetValue(key, out var v) ? v : def;
    }
}

/// <summary>说说数据</summary>
public class QzonePost
{
    public string Tid { get; set; } = "";
    public long Uin { get; set; }
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Images { get; set; } = new();
    public List<string> Videos { get; set; } = new();
    public long CreateTime { get; set; }
    public string RtCon { get; set; } = "";
    public List<QzoneComment> Comments { get; set; } = new();
    public int LikeCount { get; set; }
    public List<string> LikeUsers { get; set; } = new();
    public string LikeKey { get; set; } = "";
    public bool IsLiked { get; set; }
    public string ExtraText { get; set; } = "";

    public QzonePost() { }
    public QzonePost(long uin, string tid)
    {
        Uin = uin;
        Tid = tid;
    }
}

/// <summary>评论数据</summary>
public class QzoneComment
{
    public long Uin { get; set; }
    public string Nickname { get; set; } = "";
    public string Content { get; set; } = "";
    public long CreateTime { get; set; }
    public string CreateTimeStr { get; set; } = "";
    public int Tid { get; set; }
    public string CommentId { get; set; } = "";
    public int? ParentTid { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceUrl { get; set; } = "";

    public string PlainContent => System.Text.RegularExpressions.Regex.Replace(Content, @"\[em\].*?\[/em\]", "");
}