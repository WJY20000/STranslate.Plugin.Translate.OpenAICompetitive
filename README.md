# OpenAI Competitive

STranslate 的 OpenAI Chat Completions 兼容翻译插件。支持为不同服务设置名称、手动输入模型、获取模型列表、编辑提示词，以及流式和非流式翻译。

## 安装

在 STranslate 插件管理中导入 `STranslate.Plugin.Translate.OpenAICompetitive.spkg`，按宿主提示重启，再添加翻译服务。此版本使用 .NET 10 和 `STranslate.Plugin 1.0.15`，需要支持该 SDK 的 STranslate 版本。

填写供应商名称、API URL、API Key 和模型名。无鉴权的本地服务可留空 API Key。点击“获取模型”后可从下拉框选择模型，也可直接输入；列表刷新保留手动输入的当前模型。失去焦点时，手填模型加入本地列表，右侧 `−` 删除当前模型。

## API URL

| 输入 | 翻译请求 | 模型列表请求 |
| --- | --- | --- |
| `https://example.com` | `/v1/chat/completions` | `/v1/models` |
| `https://example.com/v1/` | `/v1/chat/completions` | `/v1/models` |
| `https://example.com/api/v1` | `/api/v1/chat/completions` | `/api/v1/models` |
| `https://example.com/v1/chat/completions` | `/v1/chat/completions` | `/v1/models` |

表中请求路径均相对于输入的域名，查询参数保留。自定义完整接口路径沿用宿主行为；末尾加 `#` 可强制原样使用该翻译地址。任意自定义接口无法可靠推导模型列表地址，获取模型时会提示改用根地址、版本地址或标准完整接口。

## 翻译设置

- **Stream**：启用流式输出，关闭则发送普通 JSON 请求。
- **MaxTokens**：输出 token 上限，`0` 表示交给服务端决定。达到上限会明确报错，避免误认为译文完整。
- **Temperature**：普通模型取值 `0–2`。GPT-5 / o1 / o3 / o4 系列使用 `max_completion_tokens`，省略不兼容的采样参数。其他兼容服务仍需遵循服务商的模型要求。
- 提示词支持 `$source`、`$target`、`$content`，用户原文中的同名字符串不会被再次替换。
- 原有配置项 `TopP`、`N`、`MaxRetries` 和 `RetryDelayMilliseconds` 生效；`N > 1` 时仅展示第一个候选。默认最多重试三次，仅重试暂时性网络/HTTP 错误；已输出部分译文后不重试。
- 推理字段与跨分片 `<think>…</think>` 内容被过滤。空响应、API 错误、无效 JSON、未完成的流及内容过滤都会报告失败。连接验证使用同一翻译链路。

## 从源码构建

在仓库根目录安装 .NET 10 SDK 和 Python 3，然后运行：

```powershell
dotnet restore STranslate.Plugin.Translate.OpenAICompetitive.csproj
dotnet run --project tests/ProtocolTests/ProtocolTests.csproj -c Release
python -m unittest discover -s tests -p "test_*.py" -v
dotnet publish STranslate.Plugin.Translate.OpenAICompetitive.csproj -c Release --no-restore -o artifacts/publish
python scripts/package.py --source artifacts/publish --output artifacts/STranslate.Plugin.Translate.OpenAICompetitive.spkg
```

Windows 上另运行宿主接口与设置页测试：

```powershell
dotnet run --project tests/PluginTests/PluginTests.csproj -c Release
```

Linux 可编译 Windows 目标并运行协议及打包测试，但不能运行 WPF 界面。GitHub Actions 在 Windows 上执行全部上述步骤，产出包含 `.spkg` 和 SHA-256 校验文件的 Actions artifact；工作流不自动创建 Release。

## 修复说明

参见 [CHANGELOG.md](CHANGELOG.md)。图标、界面资源恢复自 [sean908 的上游插件](https://github.com/sean908/STranslate.Plugin.Translate.OpenAICompetitive)，保留原 MIT 许可。原 `PluginID` 不变，以便覆盖升级已有安装。
