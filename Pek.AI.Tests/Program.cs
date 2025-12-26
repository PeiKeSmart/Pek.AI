using Pek.AI;

namespace Pek.AI.Tests;

/// <summary>
/// QVQ-Max 模型测试程序
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== QVQ-Max 模型测试程序 ===\n");

        // 从环境变量读取 API 密钥
        var apiKey = Environment.GetEnvironmentVariable("BAILIAN_API_KEY");
        
        if (String.IsNullOrEmpty(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("未检测到 BAILIAN_API_KEY 环境变量");
            Console.ResetColor();
            Console.Write("请输入您的阿里百炼 API Key: ");
            apiKey = Console.ReadLine()?.Trim();
            
            if (String.IsNullOrEmpty(apiKey))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n错误: API Key 不能为空");
                Console.WriteLine("提示: 您也可以设置环境变量 $env:BAILIAN_API_KEY=\"your-api-key\"");
                Console.ResetColor();
                return;
            }
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ API Key 已设置");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ 已从环境变量读取 API Key (长度: {apiKey.Length})");
            Console.ResetColor();
        }

        // 创建服务实例
        var service = new BaiLianService(apiKey, "qvq-max");
        Console.WriteLine("✓ QVQ-Max 服务已初始化\n");
        
        // 显示菜单
        while (true)
        {
            Console.WriteLine("\n请选择测试:");
            Console.WriteLine("1. 快速测试 R.jpg 手相识别");
            Console.WriteLine("2. 简单对话测试");
            Console.WriteLine("3. 数学推理测试");
            Console.WriteLine("4. 逻辑推理测试");
            Console.WriteLine("5. 视觉推理测试");
            Console.WriteLine("6. 复杂推理测试（过河问题）");
            Console.WriteLine("7. 手相识别测试（本地图片）");
            Console.WriteLine("8. 手相识别测试（图片URL）");
            Console.WriteLine("9. 自定义问题");
            Console.WriteLine("0. 退出");
            Console.Write("\n请输入选项: ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await TestQuickPalmReading(service);
                    break;
                case "2":
                    await TestSimpleChat(service);
                    break;
                case "3":
                    await TestMathReasoning(service);
                    break;
                case "4":
                    await TestLogicalReasoning(service);
                    break;
                case "5":
                    await TestVisualReasoning(service);
                    break;
                case "6":
                    await TestComplexReasoning(service);
                    break;
                case "7":
                    await TestPalmReadingLocal(service);
                    break;
                case "8":
                    await TestPalmReadingUrl(service);
                    break;
                case "9":
                    await TestCustomQuestion(service);
                    break;
                case "0":
                    Console.WriteLine("\n再见！");
                    return;
                default:
                    Console.WriteLine("\n无效选项，请重新选择");
                    break;
            }
        }
    }

    static async Task TestSimpleChat(BaiLianService service)
    {
        Console.WriteLine("\n=== 简单对话测试 ===");
        var question = "你好，请用一句话介绍一下你自己。";
        await AskQuestion(service, question);
    }

    static async Task TestQuickPalmReading(BaiLianService service)
    {
        Console.WriteLine("\n=== 快速手相识别测试 ===");
        
        // 使用项目目录下的 R.jpg
        var imagePath = Path.Combine(AppContext.BaseDirectory, "R.jpg");
        
        if (!File.Exists(imagePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 找不到图片文件 R.jpg");
            Console.WriteLine($"请确保 R.jpg 文件在以下目录中: {AppContext.BaseDirectory}");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"使用图片: {imagePath}");
        
        var question = @"请仔细观察这张手掌图片，进行专业的手相分析：

1. 生命线：位置、长度、深浅、有无断裂
2. 智慧线：走向、长度、深浅、特征
3. 感情线：位置、形态、分支情况
4. 事业线：是否清晰、走向如何
5. 财运线：有无、位置、特征
6. 手掌整体特征：手型、肤色、纹路清晰度

请给出详细的分析和建议。";

        await AskQuestionWithLocalImage(service, question, imagePath);
    }

    static async Task TestMathReasoning(BaiLianService service)
    {
        Console.WriteLine("\n=== 数学推理测试 ===");
        var question = "请计算: 如果一个三角形的三条边长分别是3、4、5，这是什么三角形？请说明理由。";
        await AskQuestion(service, question);
    }

    static async Task TestLogicalReasoning(BaiLianService service)
    {
        Console.WriteLine("\n=== 逻辑推理测试 ===");
        var question = "张三比李四高，李四比王五高，那么张三和王五谁更高？请给出推理过程。";
        await AskQuestion(service, question);
    }

    static async Task TestVisualReasoning(BaiLianService service)
    {
        Console.WriteLine("\n=== 视觉推理测试 ===");
        var question = "想象有一个红色的圆形和一个蓝色的正方形重叠在一起，重叠部分是什么颜色？";
        await AskQuestion(service, question);
    }

    static async Task TestComplexReasoning(BaiLianService service)
    {
        Console.WriteLine("\n=== 复杂推理测试（过河问题）===");
        var question = "一个农夫需要把狼、羊和白菜运过河，但船一次只能载农夫和其中一样东西。如果没有农夫看管，狼会吃羊，羊会吃白菜。请给出详细的过河方案。";
        await AskQuestion(service, question);
    }

    static async Task TestCustomQuestion(BaiLianService service)
    {
        Console.WriteLine("\n=== 自定义问题 ===");
        Console.Write("请输入你的问题: ");
        var question = Console.ReadLine();
        
        if (String.IsNullOrWhiteSpace(question))
        {
            Console.WriteLine("问题不能为空");
            return;
        }

        await AskQuestion(service, question);
    }

    static async Task TestPalmReadingLocal(BaiLianService service)
    {
        Console.WriteLine("\n=== 手相识别测试（本地图片）===");
        Console.Write("请输入手掌图片的完整路径: ");
        var imagePath = Console.ReadLine()?.Trim().Trim('"');
        
        if (String.IsNullOrWhiteSpace(imagePath))
        {
            Console.WriteLine("图片路径不能为空");
            return;
        }

        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"错误: 图片文件不存在 - {imagePath}");
            return;
        }

        var question = @"请仔细观察这张手掌图片，进行专业的手相分析：

1. 生命线：位置、长度、深浅、有无断裂
2. 智慧线：走向、长度、深浅、特征
3. 感情线：位置、形态、分支情况
4. 事业线：是否清晰、走向如何
5. 财运线：有无、位置、特征
6. 手掌整体特征：手型、肤色、纹路清晰度

请给出详细的分析和建议。";

        await AskQuestionWithLocalImage(service, question, imagePath);
    }

    static async Task TestPalmReadingUrl(BaiLianService service)
    {
        Console.WriteLine("\n=== 手相识别测试（图片URL）===");
        Console.Write("请输入手掌图片的URL: ");
        var imageUrl = Console.ReadLine()?.Trim();
        
        if (String.IsNullOrWhiteSpace(imageUrl))
        {
            Console.WriteLine("图片URL不能为空");
            return;
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            Console.WriteLine("错误: 无效的URL格式");
            return;
        }

        var question = @"请仔细观察这张手掌图片，进行专业的手相分析：

1. 生命线：位置、长度、深浅、有无断裂
2. 智慧线：走向、长度、深浅、特征
3. 感情线：位置、形态、分支情况
4. 事业线：是否清晰、走向如何
5. 财运线：有无、位置、特征
6. 手掌整体特征：手型、肤色、纹路清晰度

请给出详细的分析和建议。";

        await AskQuestionWithImageUrl(service, question, imageUrl);
    }

    static async Task AskQuestion(BaiLianService service, string question)
    {
        Console.WriteLine($"\n用户: {question}");
        Console.WriteLine("\nQVQ-Max 正在思考...\n");
        Console.ForegroundColor = ConsoleColor.Cyan;

        try
        {
            var chunks = new List<String>();
            
            // QVQ 系列仅支持流式输出
            await foreach (var chunk in service.ChatStreamAsync(question))
            {
                Console.Write(chunk);
                chunks.Add(chunk);
            }

            Console.ResetColor();
            Console.WriteLine("\n");
            Console.WriteLine($"[共收到 {chunks.Count} 个分块]");
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n错误: {ex.Message}");
            Console.ResetColor();
        }
    }

    static async Task AskQuestionWithLocalImage(BaiLianService service, string question, string imagePath)
    {
        Console.WriteLine($"\n用户: {question}");
        Console.WriteLine($"图片: {imagePath}");
        Console.WriteLine("\nQVQ-Max 正在分析图片...\n");
        Console.ForegroundColor = ConsoleColor.Cyan;

        try
        {
            var chunks = new List<String>();
            
            // QVQ 系列仅支持流式输出，使用本地图片
            await foreach (var chunk in service.ChatWithLocalImageStreamAsync(question, imagePath))
            {
                Console.Write(chunk);
                chunks.Add(chunk);
            }

            Console.ResetColor();
            Console.WriteLine("\n");
            Console.WriteLine($"[共收到 {chunks.Count} 个分块]");
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex.StackTrace}");
            Console.ResetColor();
        }
    }

    static async Task AskQuestionWithImageUrl(BaiLianService service, string question, string imageUrl)
    {
        Console.WriteLine($"\n用户: {question}");
        Console.WriteLine($"图片URL: {imageUrl}");
        Console.WriteLine("\nQVQ-Max 正在分析图片...\n");
        Console.ForegroundColor = ConsoleColor.Cyan;

        try
        {
            var chunks = new List<String>();
            
            // QVQ 系列仅支持流式输出，使用图片URL
            await foreach (var chunk in service.ChatWithImageStreamAsync(question, imageUrl))
            {
                Console.Write(chunk);
                chunks.Add(chunk);
            }

            Console.ResetColor();
            Console.WriteLine("\n");
            Console.WriteLine($"[共收到 {chunks.Count} 个分块]");
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex.StackTrace}");
            Console.ResetColor();
        }
    }
}
