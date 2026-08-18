# Alife.Qzone

QQ空间插件（完整移植自 [KiraAI_qzone_plugin](https://github.com/znq19/KiraAI_qzone_plugin)）：发布说说、查看动态、点赞、评论、回复、删除、访客统计、定时任务、Cookie自动刷新、图片识图。

## 功能

- 发布说说到QQ空间（支持配图：URL/本地路径/近期图片清单序号）
- 查看自己或好友的说说动态（含评论、点赞人明细、已赞状态）
- 点赞/取消点赞（精易2024实测格式，unikey带.1后缀）
- 评论说说（user/H5双路径回退，幂等防重复，可自动点赞）
- 回复评论（楼中楼原生关系标记）
- 删除说说/删除评论（h5 delcomment_ugc 双参数变体）
- 查看访客记录（明细表格+今日/30天统计）
- 图片识图（接入 Alife IVisionModel，md5缓存）
- 近期图片清单（发布说说时按序号引用配图）
- 定时任务：自动发布/自动评论/自动回复（cron或interval表达式，黑名单时间段）
- Cookie自动刷新（从OneBot get_cookies，周期刷新+用即刷+启动重试）

## 安装

将插件文件夹放入 Alife 的 `Plugins` 目录，同步环境后启用模块即可。

依赖：`Alife.Function.FunctionCaller`、`Alife.Function.QChat`（自动刷新Cookie需要）、`Alife.Function.AIModelUtility`（识图，可选）。

## 配置

| 配置项 | 说明 |
|---|---|
| CookiesStr | QQ空间登录Cookie，格式如 `uin=o123; skey=xxx; p_skey=yyy` |
| MasterIds | 允许执行敏感操作的主人QQ号，多个用逗号分隔 |
| MasterCheckEnabled | 敏感操作是否仅限主人 |
| VisitorLimit | 访客列表最多显示条数 |
| LikeUsersDisplayMax | 说说点赞用户最多显示人数 |
| LikeWhenComment | 评论后是否自动点赞 |
| LikeDelayMin / LikeDelayJitter | 评论后自动点赞的随机延迟(秒) |
| WriteThrottleSeconds | 点赞/评论等写操作的最小间隔(秒) |
| Timeout | HTTP请求超时时间(秒) |
| AutoRefreshCookie | 从OneBot自动获取最新Cookie |
| CookieRefreshInterval | Cookie周期刷新间隔，如 2h/30m/7200 |
| CookieRefreshOnUse | 用即刷节流，如 10m |
| AutoPublishSchedule | 自动发布定时表达式，cron或interval，如 `*/30 * * * *` 或 `30m` |
| AutoCommentSchedule | 自动评论定时表达式 |
| AutoReplySchedule | 自动回复定时表达式 |
| AutoReplyEnabled | 是否启用自动回复评论 |
| MaxCommentsPerCycle / MaxRepliesPerCycle | 每轮最大评论/回复数 |
| QzoneBlacklist / QzoneWhitelist | 黑白名单QQ，逗号分隔 |
| BlackoutSchedules | 定时任务黑名单时间段，如 `00:00-06:00` |
| ImageManifestEnabled / ImageManifestCount | 近期图片清单开关/数量 |
| QzoneImageDescEnabled / QzoneImageDescOwn | 图片识图开关/允许识自己图 |
| AutoPublishGroupId / AutoPublishUserId | 自动发布说说的消息来源群号/QQ号 |
| AutoPublishImageProb / Min / Max | 自动发布配图概率/最少/最多张数 |
| TaskGroupIds / TaskPrivateIds | 定时任务指令发送的群号/QQ号，逗号分隔 |
| TaskMessageStyle | 定时任务消息风格：silent=抑制群回复 |
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
