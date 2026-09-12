# MMBLAI

AI 本地模型管理器

一个基于 Windows 系统的本地 AI 模型管理工具，使用 llama.cpp 调用本地 gguf 模型，并向本地提供 OpenAI 兼容 API。

## 功能

- 管理 llama.cpp 可执行文件目录和本地模型目录
- 按名称或大小排序模型
- 配置端口、GPU 层数、上下文长度和其他启动参数
- 在 7777/7778 端口提供本地 OpenAI 兼容 API
- 控制台日志、系统托盘和运行状态管理

## 环境要求

- Windows10/11
- .NET 10 SDK

## 构建
运行
```powershell
一键打包.bat 脚本
```

## 模型资源

- [llama.cpp releases](https://github.com/ggml-org/llama.cpp/releases)
- [HF Mirror 国内模型下载站](https://hf-mirror.com/)
