# Steam Workshop Synchronizer

Windows 10/11 桌面工具，用于在联机玩家之间交换 Steam Workshop Mod 清单，并在本机 Steam 客户端中按玩家选择批量订阅或取消订阅。

## 当前能力

- 扫描当前 Windows 用户的 Steam 安装、多个 Steam 库、已安装游戏和 Workshop 状态。
- 以 Workshop ID 比较预设与本地订阅，显示缺少、额外、下载中和本地残留项目。
- 导入/导出带版本号的 JSON 清单；支持 `SWS1:` 压缩文本，只有压缩和编码，不加密。
- 导入先创建预设并展示差异，玩家逐项选择后才提交 Steam 请求。
- 预设绑定游戏 AppID，保存不可变版本，导入同名预设自动创建副本。
- 通过 Steam 客户端协议提交订阅/取消订阅请求，不保存密码或 Web API Key。
- 队列支持暂停、继续、停止后续请求，逐项记录结果，失败项不阻塞其他项目。
- 工具提交的取消订阅进入回收站，可批量重新订阅；操作批次支持撤回最近批次。
- 支持压缩备份，恢复前自动保存安全快照，并由玩家选择增量或覆盖恢复。

## 本地数据

工具数据保存于 `%LOCALAPPDATA%\\SteamWorkshopSynchronizer\\data.db`。备份包含游戏绑定和库路径、预设及版本、回收站、操作历史和工具设置；不包含 Steam 登录信息、Workshop 文件本体或临时执行队列。

## 开发者模式与日志

主窗口顶部的“开发者模式”默认关闭，开关状态会保存到工具设置。关闭时只记录警告和错误；开启后会额外记录启动、扫描、数据库写入、预设/备份、导入导出、Steam 协议和操作队列的详细过程。日志不会记录密码、令牌或 Steam Web API Key。

日志按天写入 `%LOCALAPPDATA%\\SteamWorkshopSynchronizer\\logs\\app-YYYYMMDD.log`，包含 UTC 时间、级别、分类、线程号、消息和异常堆栈，默认保留最近 14 天。工具中的“查看日志”可以刷新、复制当前日志、打开目录或清空当天日志。日志写入失败不会阻断扫描、导入或同步操作。

## 本地构建

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

仓库中的 GitHub Actions 工作流会在 `main` 的 PR/推送上构建；推送 `v*` 标签时生成 `win-x64` 压缩包并创建 GitHub Release。

## Steam 状态说明

Steam 协议提交表示请求已交给 Steam 客户端，并不等于 Workshop 已完成下载或取消订阅。执行后请等待 Steam 处理，再点击“重新扫描”确认实际状态。
