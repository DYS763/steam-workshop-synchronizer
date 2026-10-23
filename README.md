# Steam Workshop Synchronizer

Steam Workshop Synchronizer 是一个面向联机玩家的 Windows 桌面工具，用于扫描本机 Steam 游戏与 Workshop Mod，交换 Mod 清单，并由玩家选择批量订阅或取消订阅。工具使用 Steam 客户端协议提交操作，不保存 Steam 密码或 Web API Key。

项目地址：<https://github.com/DYS763/steam-workshop-synchronizer>

## 功能

- 自动发现 Steam 安装目录、多个 Steam 库、已安装游戏和 Workshop 内容。
- 按游戏查看本地 Mod，左侧支持名称或 AppID 模糊搜索，也可以只显示有 Mod 的游戏。
- 导出当前订阅或预设清单，支持 JSON 文件和 `SWS1:` 压缩文本。压缩文本只压缩和编码，不加密，适合通过聊天软件发送。
- 导入清单时自动创建预设，先显示缺少、额外、下载中、本地残留和一致项目，再由玩家决定是否同步。
- 预设支持编辑、不可变版本、版本回滚和导出指定历史版本；历史版本默认不参与普通导出。
- Mod 列表支持右键复制 Workshop 地址。订阅或取消订阅都可以进入操作历史并撤回；工具发起的取消订阅会进入回收站，可从回收站重新订阅。
- Steam 请求失败时提供失败 Mod 的名称、Workshop 地址、ID 和错误原因，方便手动处理。
- 工具数据支持压缩备份，以及由玩家选择的增量恢复或覆盖恢复。恢复前会自动保存安全快照。
- 开发者模式提供详细运行和错误日志。

## 使用流程

1. 启动工具，等待 Steam 扫描完成；必要时点击“重新扫描”。
2. 选择游戏并新建预设，或导入其他玩家的 JSON/压缩文本清单。
3. 导入后查看差异，选择是否同步。勾选的项目才会提交 Steam 操作。
4. 等待 Steam 客户端完成下载或取消订阅，再重新扫描确认最终状态。
5. 预设可以随时切换；编辑会自动创建新版本，旧版本可在“预设版本”中查看或回滚。

## 状态说明

- **一致**：清单中存在、本地已订阅且已下载。
- **清单缺少**：清单要求该 Mod，但本地没有有效订阅；可选择订阅。
- **本地多出**：本地已订阅，但不在当前预设；可选择取消订阅。
- **下载中**：已订阅，Steam 尚未完成下载。
- **本地残留**：Steam 明确提供订阅节点，且本地仍有内容目录，但当前订阅列表中没有该 Mod。
- **状态未知**：Steam 文件缺少足够的订阅信息，工具不会将其强行判断为本地残留。

Workshop 名称优先从本地 Mod 元数据读取；读取不到时尝试请求 Steam Workshop 详情。网络不可用或项目已被删除时，界面会保留 `Workshop Mod <ID>` 作为可识别的回退名称。

## 本地数据与备份

工具数据保存于 `%LOCALAPPDATA%\\SteamWorkshopSynchronizer\\data.db`，日志保存于同目录的 `logs` 文件夹。备份包含：游戏绑定和 Steam 库路径、预设及全部版本、回收站记录、操作历史、工具设置。备份不包含：Steam 登录信息、密码、Web API Key、Workshop 文件本体、临时操作队列和运行中的 Steam 状态。

恢复模式由用户选择：

- **增量恢复**：保留当前数据，按备份内容追加或更新可合并记录。
- **覆盖恢复**：替换工具管理的数据；恢复前自动生成安全快照，便于再次恢复。

## 开发者模式

开发者模式默认关闭。开启后会记录启动、扫描、数据库、预设、备份、导入导出、Steam 协议和操作队列的详细过程；警告和错误始终记录。日志按天写入 `%LOCALAPPDATA%\\SteamWorkshopSynchronizer\\logs\\app-YYYYMMDD.log`，默认保留最近 14 天。日志不会记录密码、令牌或 Web API Key。

## 构建

需要 .NET 8 SDK 和 Windows Desktop Runtime：

```powershell
dotnet restore SteamWorkshopSynchronizer.sln
dotnet build SteamWorkshopSynchronizer.sln --configuration Release
dotnet publish SteamWorkshopSynchronizer/SteamWorkshopSynchronizer.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output publish `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

GitHub Actions 会在 `main` 的推送和 Pull Request 上构建；推送 `v*` 标签时构建 `win-x64` 自包含压缩包并创建 GitHub Release。发布输出目录 `publish/` 已被 Git 忽略。

## 许可证

本项目使用 MIT License，完整文本见根目录 [`LICENSE`](LICENSE)。
