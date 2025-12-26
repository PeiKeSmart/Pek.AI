# Pek.AI 测试项目

本项目是一个控制台应用程序，用于测试 Pek.AI 库的功能，特别是针对阿里百炼的 QVQ-Max 模型。

## 环境配置

### 设置 API 密钥

测试需要阿里百炼的 API 密钥。请按以下方式设置：

#### Windows PowerShell
```powershell
$env:BAILIAN_API_KEY="your-api-key-here"
```

#### Windows CMD
```cmd
set BAILIAN_API_KEY=your-api-key-here
```

#### Linux/macOS
```bash
export BAILIAN_API_KEY="your-api-key-here"
```

### 永久设置（Windows）
1. 打开"系统属性" → "高级" → "环境变量"
2. 在"用户变量"或"系统变量"中新建
3. 变量名：`BAILIAN_API_KEY`
4. 变量值：您的 API 密钥

## 运行程序

### 使用 Visual Studio
1. 打开解决方案 `Pek.AI.slnx`
2. 将 `Pek.AI.Tests` 设为启动项目
3. 按 F5 或点击"运行"

### 使用命令行

```powershell
cd f:\Code\Pek.FrameWork\Pek.AI\Pek.AI.Tests
dotnet run
```

## 功能说明

程序提供交互式菜单，包含以下测试选项：

1. **简单对话测试** - 测试基本对话功能
2. **数学推理测试** - 验证数学推理能力（三角形判断）
3. **逻辑推理测试** - 验证逻辑推理能力（比较高度）
4. **视觉推理测试** - 验证空间/视觉推理能力
5. **复杂推理测试** - 经典过河问题求解
6. **自定义问题** - 输入任意问题进行测试
0. **退出** - 退出程序

## 特性

- ✅ **流式输出** - 实时显示 QVQ-Max 的推理过程
- ✅ **彩色显示** - 使用不同颜色区分输出
- ✅ **交互式菜单** - 方便选择不同测试场景
- ✅ **错误处理** - 友好的错误提示
- ✅ **分块统计** - 显示收到的分块数量

## 注意事项

- QVQ 系列模型仅支持流式输出
- 测试会调用实际的 API，可能产生费用
- 建议在稳定的网络环境下运行
- 推理过程可能需要较长时间，请耐心等待

## 目录结构

```
Pek.AI.Tests/
├── Pek.AI.Tests.csproj       # 控制台项目文件
├── Program.cs                # 主程序入口
├── BaiLianServiceTests.cs    # （可选的单元测试类）
├── appsettings.test.json     # 配置文件
└── README.md                 # 本文件
```

## 相关资源

- [阿里百炼官方文档](https://help.aliyun.com/zh/model-studio/)
- [QVQ-Max 模型说明](https://help.aliyun.com/zh/model-studio/qvq-max)
