# Alife.Qzone

QQ空间插件：发布说说、查看动态、点赞、评论、删除、访客统计

## 功能

- 发布说说到QQ空间（支持配图）
- 查看自己或好友的说说动态
- 点赞/取消点赞说说
- 评论说说（可自动点赞）
- 删除说说
- 查看访客记录

## 安装

将 `Qzone` 文件夹放入 Alife 的 `Plugins` 目录，同步环境后启用模块即可。

## 配置

| 配置项 | 说明 |
|---|---|
| CookiesStr | QQ空间登录Cookie，格式如 `uin=o123; skey=xxx; p_skey=yyy` |
| MasterIds | 允许执行敏感操作的主人QQ号，多个用逗号分隔 |
| MasterCheckEnabled | 敏感操作是否仅限主人 |
| VisitorLimit | 访客列表最多显示条数 |
| LikeUsersDisplayMax | 说说点赞用户最多显示人数 |
| LikeWhenComment | 评论后是否自动点赞 |
| LikeDelayMin | 评论后自动点赞的随机延迟下限(秒) |
| LikeDelayJitter | 评论后自动点赞的随机延迟抖动范围(秒) |
| WriteThrottleSeconds | 点赞/评论等写操作的最小间隔(秒) |
| Timeout | HTTP请求超时时间(秒) |