using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using NewLife.Log;

namespace Pek.AI;

/// <summary>
/// 阿里百炼AI服务
/// </summary>
public class BaiLianService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatService;
    private readonly List<String> _registeredPlugins = new();

    public BaiLianService(String apiKey, String modelName = "qwen-plus")
    {
        // 创建Kernel构建器
        var builder = Kernel.CreateBuilder();

        // 添加阿里百炼的OpenAI兼容服务
        builder.AddOpenAIChatCompletion(
            modelId: modelName,
            apiKey: apiKey,
            endpoint: new Uri("https://dashscope.aliyuncs.com/compatible-mode/v1") // 百炼OpenAI兼容端点
        );

        _kernel = builder.Build();
        _chatService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    /// <summary>
    /// 简单对话
    /// </summary>
    public async Task<String> ChatAsync(String message)
    {
        try
        {
            var response = await _chatService.GetChatMessageContentAsync(message).ConfigureAwait(false);
            return response.Content ?? String.Empty;
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return $"AI服务调用失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 多轮对话
    /// </summary>
    public async Task<String> ChatWithHistoryAsync(ChatHistory chatHistory, String newMessage)
    {
        try
        {
            chatHistory.AddUserMessage(newMessage);
            var response = await _chatService.GetChatMessageContentAsync(chatHistory).ConfigureAwait(false);
            chatHistory.AddAssistantMessage(response.Content ?? String.Empty);
            return response.Content ?? String.Empty;
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return $"AI服务调用失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 流式对话
    /// </summary>
    public async IAsyncEnumerable<String> ChatStreamAsync(String message)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(message);

        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(chatHistory).ConfigureAwait(false))
        {
            if (!String.IsNullOrEmpty(chunk.Content))
            {
                yield return chunk.Content;
            }
        }
    }

    /// <summary>
    /// 带图片的流式对话 - 支持视觉模型
    /// </summary>
    /// <param name="message">文本消息</param>
    /// <param name="imageUrl">图片URL</param>
    public async IAsyncEnumerable<String> ChatWithImageStreamAsync(String message, String imageUrl)
    {
        var chatHistory = new ChatHistory();
        
        // 创建包含图片的消息
        var messageContent = new ChatMessageContentItemCollection
        {
            new TextContent(message),
            new ImageContent(new Uri(imageUrl))
        };
        
        chatHistory.AddUserMessage(messageContent);

        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(chatHistory).ConfigureAwait(false))
        {
            if (!String.IsNullOrEmpty(chunk.Content))
            {
                yield return chunk.Content;
            }
        }
    }

    /// <summary>
    /// 带本地图片的流式对话 - 支持视觉模型
    /// </summary>
    /// <param name="message">文本消息</param>
    /// <param name="imagePath">本地图片路径</param>
    public async IAsyncEnumerable<String> ChatWithLocalImageStreamAsync(String message, String imagePath)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"图片文件不存在: {imagePath}");
        }

        // 读取图片并转换为Base64
        var imageBytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
        var base64Image = Convert.ToBase64String(imageBytes);
        
        // 获取图片格式
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        var chatHistory = new ChatHistory();
        
        // 使用 BinaryData 创建图片内容
        var imageContent = new ImageContent(new BinaryData(imageBytes), mimeType);
        var messageContent = new ChatMessageContentItemCollection
        {
            new TextContent(message),
            imageContent
        };
        
        chatHistory.AddUserMessage(messageContent);

        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(chatHistory).ConfigureAwait(false))
        {
            if (!String.IsNullOrEmpty(chunk.Content))
            {
                yield return chunk.Content;
            }
        }
    }

    /// <summary>
    /// 使用插件进行函数调用
    /// </summary>
    public async Task<String> ChatWithPluginAsync(String message, params Object[] plugins)
    {
        try
        {
            // 清理之前注册的插件
            ClearPlugins();

            // 添加插件到内核
            foreach (var plugin in plugins)
            {
                var pluginName = plugin.GetType().Name;
                _kernel.ImportPluginFromObject(plugin, pluginName);
                _registeredPlugins.Add(pluginName);
                XTrace.WriteLine($"已注册插件: {pluginName}");
            }

            // 创建执行设置，启用自动函数调用
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            var response = await _chatService.GetChatMessageContentAsync(
                message,
                executionSettings
            ).ConfigureAwait(false);

            return response.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return $"AI服务调用失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 使用单个插件进行函数调用
    /// </summary>
    public async Task<string> ChatWithSinglePluginAsync(string message, object plugin)
    {
        return await ChatWithPluginAsync(message, plugin).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册插件
    /// </summary>
    public void RegisterPlugin(object plugin, string? pluginName = null)
    {
        try
        {
            pluginName ??= plugin.GetType().Name;
            _kernel.ImportPluginFromObject(plugin, pluginName);
            _registeredPlugins.Add(pluginName);
            XTrace.WriteLine($"插件 {pluginName} 注册成功");
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>
    /// 清理所有插件
    /// </summary>
    public void ClearPlugins()
    {
        foreach (var pluginName in _registeredPlugins)
        {
            try
            {
                if (_kernel.Plugins.Contains(pluginName))
                {
                    _kernel.Plugins.Remove(_kernel.Plugins[pluginName]);
                }
            }
            catch (Exception ex)
            {
                XTrace.WriteLine($"清理插件 {pluginName} 时出错: {ex.Message}");
            }
        }
        _registeredPlugins.Clear();
    }

    /// <summary>
    /// 获取已注册的插件列表
    /// </summary>
    public IReadOnlyList<string> GetRegisteredPlugins()
    {
        return _registeredPlugins.AsReadOnly();
    }

    /// <summary>
    /// 执行语义函数
    /// </summary>
    public async Task<string> ExecuteSemanticFunctionAsync(string prompt, Dictionary<string, object>? arguments = null)
    {
        try
        {
            var function = _kernel.CreateFunctionFromPrompt(prompt);
            var kernelArgs = arguments != null ? new KernelArguments(arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)) : new KernelArguments();
            var result = await _kernel.InvokeAsync(function, kernelArgs).ConfigureAwait(false);
            return result.ToString();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return $"语义函数执行失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 创建新的对话历史
    /// </summary>
    public ChatHistory CreateChatHistory(string? systemMessage = null)
    {
        var chatHistory = new ChatHistory();
        if (!string.IsNullOrEmpty(systemMessage))
        {
            chatHistory.AddSystemMessage(systemMessage);
        }
        return chatHistory;
    }

    /// <summary>
    /// JSON模式对话 - 强制模型返回JSON格式数据
    /// </summary>
    /// <param name="message">用户消息</param>
    /// <param name="systemPrompt">系统提示词（可选，建议说明期望的JSON结构）</param>
    public async Task<String> ChatJsonAsync(String message, String? systemPrompt = null)
    {
        try
        {
            var chatHistory = new ChatHistory();

            if (!String.IsNullOrEmpty(systemPrompt))
            {
                chatHistory.AddSystemMessage(systemPrompt);
            }

            chatHistory.AddUserMessage(message);

            // 设置 response_format 为 json_object，强制返回JSON
            var executionSettings = new OpenAIPromptExecutionSettings
            {
                ResponseFormat = "json_object"
            };

            var response = await _chatService.GetChatMessageContentAsync(chatHistory, executionSettings).ConfigureAwait(false);
            return response.Content ?? String.Empty;
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }
}