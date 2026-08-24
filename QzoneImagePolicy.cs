using System;
using System.Collections.Generic;
using System.Linq;

namespace AinaLife.Qzone;

/// <summary>配图策略（照搬 KiraAI qzone/image_policy.py）</summary>
public static class QzoneImagePolicy
{
    /// <summary>在 [min, max] 内随机抽取配图目标张数（0 = AI 自主决定）</summary>
    public static int DrawTarget(int minimum, int maximum)
    {
        minimum = Math.Max(0, minimum);
        maximum = Math.Max(minimum, maximum);
        return Random.Shared.Next(minimum, maximum + 1);
    }

    /// <summary>生成给 AI 的配图指令文案</summary>
    public static string BuildInstruction(int target, int maximum)
    {
        target = Math.Max(0, target);
        maximum = Math.Max(target, maximum);
        if (target == 0)
            return $"本次随机配图目标为0：由你根据内容自主决定是否配图，可选0至{maximum}张。";
        return $"本次随机配图目标为{target}张：优先从[近期图片]清单选择恰好{target}张，" +
               "并用image_indices传入 qzone_publish；只有可用候选不足时才允许少图或纯文字发布。";
    }

    /// <summary>按序去重并剔除空项</summary>
    public static List<string> DedupeSources(IEnumerable<string> sources)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var s in sources)
        {
            if (!string.IsNullOrEmpty(s) && seen.Add(s)) result.Add(s);
        }
        return result;
    }

    /// <summary>候选标签：描述为空时给占位文案（识图资格与候选资格解耦，未识别也可被选择）</summary>
    public static string CandidateLabel(string? description)
        => string.IsNullOrEmpty(description) ? "图片内容暂未识别（仍可作为配图选择）" : description!;

    /// <summary>
    /// 按 AI 选择的序号解析配图。
    /// target > 0：不足目标时按候选顺序补足；target = 0：仅采用 AI 所选，最多 maximum 张。
    /// </summary>
    public static List<string> ResolveDescribedSources(List<string> describedSources, List<int> chosenIndices, int target, int maximum)
    {
        var selected = DedupeSources(chosenIndices
            .Where(i => i >= 1 && i <= describedSources.Count)
            .Select(i => describedSources[i - 1]));
        if (target > 0)
        {
            foreach (var source in describedSources)
            {
                if (selected.Count >= target) break;
                if (!selected.Contains(source)) selected.Add(source);
            }
            return selected.Take(target).ToList();
        }
        return selected.Take(maximum).ToList();
    }
}
