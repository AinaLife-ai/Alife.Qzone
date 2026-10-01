# Alife.Qzone

QQ空间插件（完整移植并对齐 [KiraAI_qzone_plugin](https://github.com/znq19/KiraAI_qzone_plugin) 的设计，含 v1.4.8 的图片清单两条硬规则）：发布说说、查看动态、点赞、评论、回复、删除、访客统计、定时任务、Cookie 全自动获取、图片识图。

> ⚠️ **工具名说明**：Alife 框架用 **C# 方法名**作为工具名（如 `QzonePublish`），
> 参数也是 C# 原名（如 `imageIndices`）。旧版文档/描述里写的 `qzone_publish`、`image_indices`
> 是 Kira(Python) 的写法，**已全部更正**，避免 AI 按错误名字调用。

## 功能

- 发布说说到QQ空间（支持配图：URL/本地路径/近期图片清单序号 `imageIndices`）
- 查看自己或好友的说说动态（含评论、点赞人明细、已赞状态、**自己相关项标「（我）」**、**时间脏值如实显示「时间未知」**）
- 点赞/取消点赞（精易2024实测格式，unikey带.1后缀，abstime=发布时间，已赞防重复）
- 评论说说（user/H5双路径回退，本地幂等防重复，接口成功即成功，可自动点赞，**评论成功即记录「已评论」**）
- 回复评论（主评论锚定楼中线程 + QQ原生关系标记，ID+UIN 组合精确定位）
- 删除说说/删除评论（h5 delcomment_ugc 双参数变体）
- 查看访客记录（明细表格+今日/30天统计）
- 图片识图（接入 Alife AIModelUtility 视觉模型，md5 全局持久缓存，命中零模型调用）
- **近期图片清单（按需注入）**：默认只在**插件自己的定时发布任务那一轮**注入；只列**已识别**的图；也可随时主动调用 `QzoneImageManifest` 获取（详见下节）
- 图片链接过期自动续命（get_msg 换新签名 URL）
- **定时任务**：自动发布/自动评论/自动回复（cron 或 interval+抖动，黑名单时间段，60s 防抖，misfire 容错，**任务互斥防并发重复**）
  - 指令模式（推荐）：配了「任务群ID/任务私聊ID」时，由 AI 以完整人设执行，自带防重复/防复读/防挖坟/防重复工作的提示词引导
  - 后台模式：未配任务目标时由插件直接生成并操作（**无 AI 参与、无提示词层引导**，仅代码层过滤；首次运行会打一条提示日志）
- **Cookie 全自动获取**（四层机制 + 启动自愈，见下）

## 近期图片清单：两条硬规则（对齐 Kira v1.4.8）

1. **只走免费路径**：清单**只列已经有描述的图**。拿不到描述 ⇒ 这张图不进清单：不列表、不下载、更不识图。
   识图工作在**图片出现的那一刻**（收到消息、回拉历史时）就后台开始；**注入钩子纯只读**（零 await、零 IO、零后台任务），结构上不可能阻塞回复。
2. **按需注入**：`ImageManifestInjectMode` 默认 `on_demand` —— **只在插件自己的定时发布任务那一轮注入**。
   `always` = 旧行为（**每轮注入，会让清单永久挂在上下文里**，Kira v1.4.8 已修，本移植版曾复辟，现已修正）；`off` = 不注入（仍可用工具主动获取）。

清单**序号解析与注入使用同一份过滤清单**（只含已识别项），因此序号不会错位。

## 定时任务的内容护栏（防止重复、复读、挖坟、对同一目标重复工作）

| 层级 | 机制 |
|---|---|
| 提示词层 | 指令保留 Kira 原文（「严禁内容重复和复读」「优先没有评论过的内容」「时间戳不得超过7天」），并追加「已评论过的不要再评论」「同一轮不要对同一作者连评多条」「只在本轮候选里选」 |
| 候选清单 | 指令模式下先把**过滤好的候选**（时间窗内 / 未评论过 / 非黑名单）注入指令，AI 在确定候选里选（可关） |
| 代码层 | 评论前统一过滤：排除自己 / 黑名单 / **已评论过的说说（持久记录，跨重启有效）** / 超出 `CommentWindowDays`（默认 7 天）；无可评论候选则直接跳过并记日志 |
| 端点兜底 | 好友动态接口（`feeds3_html_more`）不可用时，自动改用**稳定的单目标接口**逐个拉取白名单好友的说说继续评论 |
| 并发保护 | 同一任务上一轮未结束时不再触发（避免并发两轮造成重复评论） |

## Cookie 全自动获取（四层机制）

只要保持 OneBot（NapCat/LLOneBot 等）在线，无需手动填写任何 Cookie：

1. **启动即取**：模块启动时立即从 OneBot `get_cookies(domain=user.qzone.qq.com)` 获取（5s 快速超时，OneBot 未连接时快速失败不白等）
2. **用即刷**：调用空间功能时距上次刷新超过节流间隔（默认 10m）则顺手刷新
3. **周期刷新**：默认每 2h（±10% 抖动）自动刷新
4. **失效自救**：HTTP 层检测到登录失效特征（-3000/-100/401/**返回登录页**/消息特征）时自动强制刷新并重试请求（最多 4 次）

启动失败不判死：后台按 15/30/60/120s 递增重试最多 4 次；刷新失败保留旧会话（last-good）并用保活探针验证。配置中的 Cookie 字符串仅作应急后备。

> 即使**关闭自动刷新**、只用手填 Cookie，插件也会初始化自身 QQ 号（`uin`），
> 保证「排除自己 / 标（我）」等判定有效（旧版这种情况 `uin` 恒为 0，判定全部退化）。

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
| ViewFetchDetails | `QzoneView` 是否逐条拉详情与点赞列表（关=省请求防限流） |
| LikeWhenComment / LikeDelayMin / LikeDelayJitter | 评论后自动点赞及随机延迟 |
| WriteThrottleSeconds | 写操作透明节流间隔(秒)（只延迟不拦截） |
| Timeout | HTTP请求超时(秒)（Cookie 获取固定 5s 不受影响） |
| AutoPublishSchedule / AutoCommentSchedule / AutoReplySchedule | 定时表达式：cron 5段 或 interval（如 `30m`、`2h/30m` 抖动） |
| AutoReplyEnabled | 自动回复总开关 |
| MaxCommentsPerCycle / MaxRepliesPerCycle | 每轮最大评论/回复数 |
| CommentWindowDays | **评论时间窗（默认 7 天，0=不限）**：只评论窗口内发布的说说，防挖坟 |
| TaskInjectCandidates | **指令模式注入候选清单**（排除已评论/超窗/黑名单），避免 AI 满世界找导致重复工作 |
| LegacyCommentUseWhitelist | **好友动态接口不可用时用白名单兜底**（逐个拉取该接口稳定的好友说说） |
| QzoneBlacklist / QzoneWhitelist | 黑白名单QQ，逗号分隔 |
| BlackoutSchedules | 定时任务黑名单时间段，如 `00:00-06:00`（支持跨天，只管定时任务） |
| ImageManifestEnabled / ImageManifestCount | 近期图片清单总开关 / 数量 |
| **ImageManifestInjectMode** | **on_demand（默认，仅发布任务那一轮）/ always（每轮，旧行为）/ off** |
| **ImageDescMaxChars** | 清单里每条描述的最大字数（默认 80，0=不截断） |
| **ImageDescOnManifestRequest** | AI 主动要清单时是否顺意识图（关=严格只列已识别） |
| QzoneImageDescEnabled / QzoneImageDescOwn | 图片识图开关/允许识自己图 |
| AutoCommentImageDesc | 后台自动评论前先识别对方配图 |
| AutoPublishGroupId / AutoPublishUserId | 后台直接生成模式的消息来源群号/QQ号 |
| AutoPublishImageProb / Min / Max | 自动发布配图概率/最少/最多张数（抽到0=AI自主） |
| AutoPublishImageFallback / AutoPublishImageDedupeInterval | 非法选图兜底 / 配图去重窗口（默认3d，仅成功后记录） |
| CommentVerify | 评论提交后回读确认（诊断模式，仅日志） |
| TaskGroupIds / TaskPrivateIds | 定时任务指令场合（群号/QQ号，逗号分隔，随机选一个）；**留空=后台模式，无 AI 引导** |
| TaskMessageStyle | silent=提示AI不向群/私聊发回复（无痕）；notify=不限制 |
| AutoAttachRecentImage | 吸附模式：发说说未指定图片时自动抓最近一张图（开启则清单机制关闭） |
| AllowInsecureSsl | 忽略SSL证书校验：QQ空间接口报SSL连接错误时开启（多为本机代理/VPN/抓包拦截HTTPS），有安全风险谨慎开启 |
| ImageLocalPathWhitelist | 本地图片目录白名单：允许AI读取的本地图片目录（绝对路径，英文逗号分隔，如 D:\\ComfyUI\\output）；留空=全部允许。用于 ComfyUI 等本地产图 |

## 工具函数

- `QzonePublish`：发布说说（text / images / imageIndices / wantImages）
- `QzoneView`：查看说说（targetId / num；省略 targetId = 自己）
- `QzoneLike`：点赞/取消点赞（targetId / tid / action=like|unlike）
- `QzoneComment`：评论说说（targetId / tid / content 可选）
- `QzoneReplyComment`：回复评论（targetId / commentId / commentUin / content）
- `QzoneDelete`：删除自己的说说（tid）
- `QzoneDeleteComment`：删除评论（targetId / tid / commentId / commentUin）
- `QzoneVisitors`：查看访客统计
- `QzoneDescribeImage`：查看说说配图内容（targetId / tid / index）
- `QzoneImageManifest`：获取近期图片清单（只列已识别的图）

## 更新日志

### 4.5.0

**问题①：自动评论每轮报 `获取说说失败: JSON 解析失败`（评论本身可用）**

- 根因定位：失败**只发生在好友动态列表端点** `feeds3_html_more`（评论走的是另一条链路，所以评论正常）；
  该端点返回的 JSON 里含大段 HTML，遇到未转义引号/裸控制字符/截断时，原有三级兜底（严格→宽松→宽松+引号修复）会全灭，
  随后**直接抛异常**，且**既不重试也不打诊断日志**。
- 修复：
  - **诊断**：解析失败时记录归类、正文长度与前 300 字（URL 脱敏、60s 限流），下次可直接定位
  - **重试**：「非 JSON / 截断 / 乱码」与「空响应」同级，按 1/2/3s 退避重试（此前只重试空响应）
  - **兜底排列组合**：严格 → 宽松 → 引号修复 → 宽松+修复 → 修复+宽松 → **截断补全**，首个成功即用
  - **字段级抢救**：整文档废了也能把 `msglist`（或 recent 模式的 `html` 段）单独救出来
  - **归类**：登录页/风控页/截断/乱码给出可读原因；登录页才触发 Cookie 自救
  - **任务降级**：重试后仍失败则记**一条**简明日志并跳过本轮（不再每轮刷 Error 堆栈）；
    配置了白名单时**改用稳定的单目标接口**继续评论，避免整轮报废

**问题②：定时任务缺少 Kira 那样的提示词引导（防重复/复读/挖坟/重复工作）**

- 事实核对：三条指令文本（评论/发布/回复）与 Kira **逐字一致**，但**只在配置了「任务群ID/任务私聊ID」时才会走**；
  未配置时走后台模式，**没有任何提示词引导** —— 用户正是在这条路径上（栈里是 `LegacyAutoCommentAsync`）。
- 修复：代码层护栏（默认 7 天窗口 / 排除自己 / 排除已评论 / 黑名单）+ **已评论说说持久记录（跨重启）** +
  指令模式**注入候选清单** + 指令追加两句（已评论不要再评、同轮不对同一作者连评）+ 后台模式首次运行提示日志。

**问题③：其他修复（同一轮审计）**

- **配图去重记录重启即丢**：状态文件写入用 PascalCase、读取用小写 ⇒ 改为驼峰序列化 + 大小写不敏感读取
  （旧文件也能读），去重记录容量 20 → 500（对齐 Kira：否则「3 天去重」形同虚设）
- **手动 Cookie 部署下自身 QQ 号恒为 0**：导致「排除自己 / 标（我）」等判定退化 ⇒ 补齐初始化
- **图片清单「永远挂载」复辟**（同 Kira v1.4.8 修过的问题）：旧实现**每轮注入**且在**钩子里起后台识图** ⇒
  改为按需注入 + 钩子纯只读 + 只列已识别，并新增 `wantImages` 取清单闸（清单已在上下文时忽略该参数）
- **清单序号错位**：注入端只含已识别项、解析端却用全量注册表 ⇒ 两处统一为同一份过滤清单
- **`QzoneView` 的 `QzoneView`/`QzonePublish` 参数强转崩溃**：`long.Parse(targetId)` 遇非数字抛 `FormatException` ⇒ 改 `TryParse` + 友好提示；`num` 加上限（1–20）
- **并发安全**：发送者表改 `ConcurrentDictionary`；状态集合全部加锁 + 快照式 `Save()`（此前并发写会抛「集合已修改」并被吞 ⇒ 状态静默丢失）
- **任务互斥**：同一任务上一轮未结束不再触发（避免并发两轮重复评论）
- **工具名/参数名错配**：文档与描述里的 `qzone_*`、`image_indices`、`target_id`、`comment_uin` 全部更正为真实名（`QzonePublish`、`imageIndices`、`targetId`、`commentUin`）
- **LLM 空返回告警**、**查看详情/点赞列表可关闭**（`ViewFetchDetails`，省 N+1 请求）
- **图片描述长度上限**（默认 80 字）与**清单时间脏值显示「时间未知」**

**自检**：50 项真值表全通过；**反向验证**（同一批症状断言跑在 4.4.0 上）**5/5 精确变红**，
其中包含「清单每轮挂载」的原行为复现。
