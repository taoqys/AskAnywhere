# AskAnywhere

一个轻量、常驻后台的 Windows AI 助手。连按 **Shift**（可在设置中改键）唤起聊天窗口：

- 有选中文字 → 自动填入，**↑/↓** 切换功能（解释 / 回答 / 翻译 / 润色 / 自定义…），**回车**发送
- 无选中文字 → 打开空的聊天窗口，直接输入提问（回车发送，Shift+Enter 换行）
- 支持任意 **OpenAI 兼容** API（OpenAI / DeepSeek / Ollama / 各类中转服务）

技术栈：C# / WPF / .NET 8，原生窗口、无 WebView2，浅色主题，空闲内存低、唤起响应快。

## 运行库

本版本为 **framework-dependent** 发布，安装包很小，但需要系统装有 .NET 运行时：

- 下载并安装 **.NET 8 Desktop Runtime (x64)**：https://dotnet.microsoft.com/download/dotnet/8.0
- 安装后运行 `AskAnywhere.exe` 即可（首次运行若无 .NET 会提示引导安装）

## 功能

- ✅ 连按 Shift 全局唤起（触发键可选 Shift / Ctrl / Alt / 禁用，间隔 100–800ms 可调，自动排除组合键误触）
- ✅ 选中文本捕获（纯 UI Automation，不碰剪贴板、不模拟按键，未选词时零副作用）
- ✅ 没有选中文字时打开空的聊天窗口，不会误填剪贴板内容
- ✅ **失焦自动隐藏**：点击其他窗口时聊天窗口自动隐藏（可在设置中关闭）
- ✅ **↑/↓ 随时切换功能**（输入时也生效，屏蔽输入框内的上下行移动）
- ✅ **Enter 直接发送，Shift+Enter 换行**
- ✅ **临时推理开关**（输入框下方，只影响当前对话，不改全局设置）
- ✅ 推理模式（DeepSeek thinking）：全局可开关、可选强度（低/中/高/自动），思考内容以灰色显示；关闭时会显式发送 disabled
- ✅ 功能列表：回答问题 / 解释 / 翻译 / 润色，每项提示词可编辑，可自由添加自定义功能
- ✅ 自动获取模型列表（设置中「获取模型列表」）
- ✅ OpenAI 兼容接口：Base URL / API Key / Model / Temperature 全部可配置
- ✅ 流式输出（打字机效果），发送中可随时停止
- ✅ 多轮对话上下文
- ✅ **每次打开窗口自动保存旧会话到历史并开启新对话**（顶栏「历史」按钮 / 托盘菜单可查看）
- ✅ 系统托盘常驻（双击图标 / 右键菜单）
- ✅ 单实例运行（重复启动会唤起已有窗口）
- ✅ API Key 使用 Windows DPAPI 加密存储
- ✅ 开机自启（设置中开关）

## 使用

### 下载

1. 打开仓库的 **Actions** 页面
2. 选择最新的 **build** 工作流运行
3. 在运行详情底部下载 **AskAnywhere-win-x64** 压缩包
4. 先安装 .NET 8 Desktop Runtime，再解压运行 `AskAnywhere.exe`

### 快捷键

| 操作 | 说明 |
|---|---|
| 连按 Shift（默认） | 唤起/隐藏聊天窗口（设置中可改 Ctrl / Alt / 禁用） |
| ↑ / ↓ | 随时切换功能（回答问题 / 解释 / 翻译 / 润色 / 自定义…），输入时也生效 |
| 回车 | 直接发送 |
| Shift+Enter | 输入框中换行 |
| 推理（输入框下方） | 单次对话的临时推理开关，不影响全局设置 |
| Esc | 停止生成；再次按 Esc 隐藏窗口 |
| 托盘图标 | 左键双击唤起，右键打开菜单（历史记录 / 设置 / 退出） |

### 取词行为

- 使用 **UI Automation** 读取焦点应用的选中文本，不依赖剪贴板、不模拟按键
- 未选中任何文字时返回空，窗口以空状态打开，不会误填剪贴板里的旧内容

## 构建

本地构建（需要 .NET 8 SDK；framework-dependent 发布，运行时由用户自行安装）：

```powershell
dotnet publish src/AskAnywhere/AskAnywhere.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

GitHub Actions 会在每次 push 到 `main` 时自动构建并上传产物。

## 配置

配置文件位于 `%APPDATA%\AskAnywhere\settings.json`，会话历史位于 `%APPDATA%\AskAnywhere\history.json`，建议通过托盘菜单的「设置」修改。

常用 API Base URL：

| 服务 | Base URL |
|---|---|
| DeepSeek | `https://api.deepseek.com/v1` |
| OpenAI | `https://api.openai.com/v1` |
| Ollama（本地） | `http://127.0.0.1:11434/v1` |

## 许可证

本项目基于 **MIT License** 开源，详见 [LICENSE](LICENSE)。

## 路线图

- [ ] Markdown 渲染（代码高亮）
- [ ] 会话导出
- [ ] 多显示器窗口定位优化
- [ ] 图片/截图发送
