# Alife.Qzone

QQ空间插件（完整移植并对齐 [KiraAI_qzone_plugin](https://github.com/znq19/KiraAI_qzone_plugin) v1.4.6 的全部优秀设计）：发布说说、查看动态、点赞、评论、回复、删除、访客统计、定时任务、Cookie 全自动获取、图片识图。

## 功能

- 发布说说到QQ空间（支持配图：URL/本地路径/近期图片清单序号）
- 查看自己或好友的说说动态（含评论、点赞人明细、已赞状态、自己昵称补偿）
- 点赞/取消点赞（精易2024实测格式，unikey带.1后缀，abstime=发布时间，已赞防重复）
- 评论说说（user/H5双路径回退，本地幂等防重复，接口成功即成功，可自动点赞）
- 回复评论（主评论锚定楼中线程 + QQ原生关系标记，ID+UIN 组合精确定位）
- 删除说说/删除评论（h5 delcomment_ugc 双参数变体）
- 查看访客记录（明细表格+今日/30天统计）
- 图片识图（接入 Alife AIModelUtility 视觉模型，md5 全局持久缓存，命中零模型调用）
- 近期图片清单（QQ 消息到来时自动注入给 AI，也可主动调用获取）
- 图片链接过期自动续命（get_msg 换新签名 URL）
- 定时任务：自动发布/自动评论/自动回复（cron 或 interval+抖动 表达式，黑名单时间段，60s 防抖，misfire 容错）
- **Cookie 全自动获取**（四层机制 + 启动自愈，见下）

## Cookie 全自动获取（四层机制）

只要保持 OneBot（NapCat/LLOneBot 等）在线，无需手动填写任何 Cookie：

1. **启动即取**：模块启动时立即从 OneBot `get_cookies(domain=user.qzone.qq.com)` 获取（5s 快速超时，OneBot 未连接时快速失败不白等）
2. **用即刷**：调用空间功能时距上次刷新超过节流间隔（默认 10m）则顺手刷新
3. **周期刷新**：默认每 2h（±10% 抖动）自动刷新
4. **失效自救**：HTTP 层检测到登录失效特征（-3000/-100/401/消息特征）时自动强制刷新并重试请求（最多 4 次）

启动失败不判死：后台按 15/30/60/120s 递增重试最多 4 次（自愈窗口内工具会提示"Cookie 正在后台自动获取中"）；刷新失败保留旧会话（last-good）并用保活探针验证。配置中的 Cookie 字符串仅作自动刷新失败时的应急后备。

## 安装

将插件文件夹放入 Alife 的 `Plugins` 目录，同步环境后启用模块即可。

依赖：`Alife.Function.FunctionCaller`、`Alife.Function.QChat`（自动获取Cookie需要）、`Alife.Function.AIModelUtility`（识图）。

## 权限设计

默认**不在代码层做主人检查**（MasterCheckEnabled=false），推荐在人设/提示词层控制权限（对齐 Kira 官方建议，避免拦截 AI 自主行为）。如开启主人检查，无法识别发送者时将默认拒绝（fail-closed）。

## 配置

| 配置项 | 说明 |
|---|---|
| CookiesStr | 应急后备 Cookie（自动刷新失败时才用） |
| AutoRefreshCookie / CookieRefreshInterval / CookieRefreshOnUse | Cookie 自动获取：总开关 / 周期间隔(±10%抖动) / 用即刷节流 |
| MasterIds / MasterCheckEnabled | 主人QQ号 / 代码层主人检查（默认关，推荐人设层控权） |
| VisitorLimit / LikeUsersDisplayMax / ViewCommentMax | 访客/点赞人/评论 显示上限 |
| LikeWhenComment / LikeDelayMin / LikeDelayJitter | 评论后自动点赞及随机延迟 |
| WriteThrottleSeconds | 写操作透明节流间隔(秒)（只延迟不拦截） |
| Timeout | HTTP请求超时(秒)（Cookie 获取固定 5s 不受影响） |
| AutoPublishSchedule / AutoCommentSchedule / AutoReplySchedule | 定时表达式：cron 5段 或 interval（如 `30m`、`2h/30m` 抖动） |
| AutoReplyEnabled | 自动回复总开关 |
| MaxCommentsPerCycle / MaxRepliesPerCycle | 每轮最大评论/回复数 |
| QzoneBlacklist / QzoneWhitelist | 黑白名单QQ，逗号分隔 |
| BlackoutSchedules | 定时任务黑名单时间段，如 `00:00-06:00`（支持跨天，只管定时任务） |
| ImageManifestEnabled / ImageManifestCount | 近期图片清单开关/数量 |
| QzoneImageDescEnabled / QzoneImageDescOwn | 图片识图开关/允许识自己图 |
| AutoCommentImageDesc | 后台自动评论前先识别对方配图 |
| AutoPublishGroupId / AutoPublishUserId | 后台直接生成模式的消息来源群号/QQ号 |
| AutoPublishImageProb / Min / Max | 自动发布配图概率/最少/最多张数（抽到0=AI自主） |
| AutoPublishImageFallback / AutoPublishImageDedupeInterval | 非法选图兜底 / 配图去重窗口（默认3d，仅成功后记录） |
| CommentVerify | 评论提交后回读确认（诊断模式，仅日志） |
| TaskGroupIds / TaskPrivateIds | 定时任务指令场合（群号/QQ号，逗号分隔，随机选一个） |
| TaskMessageStyle | silent=提示AI不向群/私聊发回复（无痕）；notify=不限制 |
| AutoAttachRecentImage | 吸附模式：发说说未指定图片时自动抓最近一张图 |

## 工具函数

- `qzone_publish`：发布说说（text/images/image_indices）
- `qzone_view`：查看说说（target_id/num）
- `qzone_like`：点赞/取消点赞（action=like/unlike）
- `qzone_comment`：评论说说（content可选，AI自动生成）
- `qzone_reply_comment`：回复评论（comment_id/comment_uin/content）
- `qzone_delete`：删除自己的说说
- `qzone_delete_comment`：删除评论
- `qzone_visitors`：查看访客统计
- `qzone_describe_image`：查看说说配图内容
- `qzone_image_manifest`：获取近期图片清单
