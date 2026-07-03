# 电影时间戳文本框

一个 Windows 10/11 x64 悬浮记录工具，用独立时间轴记录观看外部播放器中电影时的即时感想。应用不会播放、控制或读取电影。

## 使用

1. 解压便携包并运行 `电影时间戳文本框.exe`。
2. 点击“新建记录”，选择一个 `.txt` 文件并设置电影总时长。
3. 在电影开始时点击播放按钮，或按 `Ctrl+Alt+Space` 同步开始计时；电影暂停时再次触发。
4. 输入文字后按 Enter 保存，Shift+Enter 换行。
5. 按住 `Ctrl+Alt+V` 说话，松开后保存；或按 `Ctrl+Alt+B` 开始/结束开关式语音输入。
6. 如果工具晚于电影启动，在展开面板输入当前电影时间并点击“校准”。

“按住说话”支持设置为单独一个键（例如 `V`）。应用运行期间，这个键会被注册为全局快捷键，因此不会再输入到其他程序中。

语音识别完全离线，首次启动加载内置中文模型可能需要数秒。电影声音可能被麦克风一并识别，使用耳机可降低干扰。

## 开发与构建

需要 .NET 10 SDK。执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-portable.ps1
```

脚本会下载官方 `vosk-model-small-cn-0.22`、运行测试、生成自包含便携目录和：

`artifacts\MovieTimestampNotes-win-x64.zip`

记录文件是 UTF-8 文本；窗口位置、快捷键和麦克风偏好保存在 `%LOCALAPPDATA%\电影时间戳文本框\settings.json`。
