# MMBLAI

猫猫布灵 AI 模型管理器

一个基于 Windows Forms 的本地 AI 模型管理工具，用于配置和运行 llama.cpp，并向本地提供 OpenAI 兼容 API。

## 功能

- 管理 llama.cpp 可执行文件和本地模型目录
- 按名称或大小排序模型
- 配置端口、GPU 层数、上下文长度和其他启动参数
- 在 7777/7778 端口提供本地 OpenAI 兼容 API
- 控制台日志、系统托盘和运行状态管理

## 环境要求

- Windows
- .NET 10 SDK

## 构建

```powershell
dotnet build -c Release
```

单文件发布：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 模型资源

- [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases)
- [HF Mirror 国内模型下载站](https://hf-mirror.com/)
