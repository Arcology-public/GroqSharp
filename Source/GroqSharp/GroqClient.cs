using GroqSharp;
using GroqSharp.Models;
using GroqSharp.Tools;
using GroqSharp.Utilities;
using System;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

public class GroqClient :
    IGroqClient
{
    #region Class Fields

    private const string DefaultBaseUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string ContentTypeJson = "application/json";
    private const string BearerTokenPrefix = "Bearer";
    private const string DataPrefix = "data: ";
    private const string StreamDoneSignal = "[DONE]";
    private const string ChoicesKey = "choices";
    private const string DeltaKey = "delta";
    private const string ContentKey = "content";
    private const string FunctionTypeKey = "function";

    private static readonly Dictionary<Type, string> _typeCache = new Dictionary<Type, string>();

    #endregion

    #region Instance Fields

    private readonly HttpClient _client = new HttpClient();
    private readonly Dictionary<string, IGroqTool> _tools = new Dictionary<string, IGroqTool>();

    private string _baseUrl = DefaultBaseUrl;
    private string _model;
    private double? _temperature;
    private int? _maxTokens;
    private int? _seed;
    private bool _randomSeed = false;
    private double? _topP;
    private string? _stop;
    private Message _defaultSystemMessage;
    private int _maxStructuredRetryAttempts = 3;
    private int _maxToolInvocationDepth = 3;
    private bool _jsonResponse;
    private string? _reasoningFormat;
    private string? _reasoningEffort;
    private string? _serviceTier;
    private bool _parallelToolInvocationAllowed = false;
    private Random _random;
    #endregion

    #region Constructors

    public GroqClient(
        string apiKey,
        string model,
        HttpClient? client = null)
    {
        _random = new Random();
        _model = model;
        _client = client ?? new HttpClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(BearerTokenPrefix, apiKey);
    }

    #endregion

    #region Instance Methods

    #region Fluent Methods

    public IGroqClient SetBaseUrl(
        string baseUrl)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl), "Base URL cannot be null.");
        return this;
    }

    public IGroqClient SetModel(
        string model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model), "Model cannot be null.");
        return this;
    }

    public IGroqClient SetTemperature(
        double? temperature)
    {
        _temperature = (temperature == null || (temperature >= 0.0 && temperature <= 2.0)) ? temperature : throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature is a value between 0 and 2.");
        return this;
    }

    public IGroqClient SetMaxTokens(
        int? maxTokens)
    {
        _maxTokens = maxTokens;
        return this;
    } 
    
    public IGroqClient SetSeed(
        int? seed)
    {
        _seed = seed;
        return this;
    }
    
    public IGroqClient SetRandomSeedPerRequest(
        bool useRandomSeed)
    {
        _randomSeed = useRandomSeed;
        return this;
    }

    public IGroqClient SetTopP(
        double? topP)
    {
        _topP = (topP == null || (topP >= 0.0 && topP <= 1.0)) ? topP : throw new ArgumentOutOfRangeException(nameof(topP), "TopP is a value between 0 and 1.");
        return this;
    }

    public IGroqClient SetStop(
        string stop)
    {
        _stop = stop;
        return this;
    }

    public IGroqClient SetDefaultSystemMessage(
        Message message)
    {
        _defaultSystemMessage = message ?? throw new ArgumentNullException(nameof(message), "Default system message cannot be null.");
        return this;
    }

    public IGroqClient SetStructuredRetryPolicy(
        int maxRetryAttempts)
    {
        _maxStructuredRetryAttempts = maxRetryAttempts;
        return this;
    }

    public IGroqClient SetMaxToolInvocationDepth(
        int maxDepth)
    {
        _maxToolInvocationDepth = maxDepth;
        return this;
    }

    public IGroqClient SetJsonResponse(
        bool jsonResponse)
    {
        _jsonResponse = jsonResponse;
        return this;
    }

    public IGroqClient SetReasoningFormat(
        string reasoningFormat)
    {
        _reasoningFormat = reasoningFormat;
        return this;
    } 
    
    public IGroqClient SetReasoningEffort(
        string reasoningEffort)
    {
        _reasoningEffort = reasoningEffort;
        return this;
    }
        public IGroqClient SetServiceTier(
        string serviceTier)
    {
        _serviceTier = serviceTier;
        return this;
    }

    public IGroqClient RegisterTools(
        params IGroqTool[] tools)
    {
        foreach (var tool in tools)
            _tools[tool.Name.ToLower()] = tool;
        return this;
    }

    public IGroqClient UnregisterTools(
        params string[] toolNames)
    {
        foreach (var toolName in toolNames)
        {
            _tools.Remove(toolName.ToLower());
        }
        return this;
    }

    public IGroqClient AllowParallelToolInvocation(bool allow)
    {
        _parallelToolInvocationAllowed = allow;
        return this;
    }

    #endregion

    #region Helper Methods

    public object BuildToolSpecifications()
    {
        var toolsList = new List<object>();
        foreach (var tool in _tools.Values)
        {
            var toolSpec = new
            {
                type = FunctionTypeKey,
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.Parameters.ToDictionary(
                        param => param.Key,
                        param => param.Value.ToJsonSerializableObject()
                    )
                }
            };
            toolsList.Add(toolSpec);
        }

        return toolsList;
    }

    /// <summary>
    /// Attempts to attach messages to an exception's data dictionary.
    /// If messages already exist, replaces them only if the new messages contain more elements.
    /// </summary>
    /// <param name="exception">The exception to attach messages to</param>
    /// <param name="messages">The messages to potentially attach</param>
    private static void TryAttachMessages(Exception exception, IEnumerable<Message> messages)
    {
        if (exception == null) return;
        
        var messageList = messages?.ToList();
        if (messageList == null || !messageList.Any()) return;
        
        if (exception.Data.Contains("Messages"))
        {
            // Messages already attached - check if we should replace them
            if (exception.Data["Messages"] is List<Message> existingMessages)
            {
                // Replace only if new messages contain more elements (longer conversation)
                if (messageList.Count > existingMessages.Count)
                {
                    exception.Data["Messages"] = messageList;
                }
            }
        }
        else
        {
            // No messages attached yet - attach them
            exception.Data["Messages"] = messageList;
        }
    }

    private async Task<string> HandleToolResponsesAndReinvokeAsync(
        List<Message> messages,
        GroqClientResponse response,
        int depth,
        CancellationToken? cancellationToken)
    {
        try
        {
            // First check if max depth is exceeded
            if (depth >= _maxToolInvocationDepth)
            {
                var exception = new InvalidOperationException("Maximum tool invocation depth exceeded, possible loop detected.");
                TryAttachMessages(exception, messages);
                throw exception;
            }

        // Add the assistant's original response to the conversation before handling tool calls
        if (response.Contents.Any())
        {
            messages.Add(new Message
            {
                Role = MessageRoleType.Assistant,
                Content = response.Contents.FirstOrDefault()
            });
        }

        // Handle any tool calls
        if (response.ToolCalls != null && response.ToolCalls.Any())
        {
            if (_parallelToolInvocationAllowed)
            {
                var toolTasks = response.ToolCalls.Select(CallTool);
                var toolResults = await Task.WhenAll(toolTasks);
                messages.AddRange(toolResults);
            }
            else
            {
                foreach (var call in response.ToolCalls)
                {
                    var toolResult = await CallTool(call);
                    if (toolResult != null)
                        messages.Add(toolResult);
                }
            }

            // Reinvoke the API with the updated messages
            return await CreateChatCompletionWithToolsAsync(messages, depth, cancellationToken);
        }

            // If there were no tool calls, just return the original response
            return response.Contents.FirstOrDefault();
        }
        catch (Exception ex)
        {
            var ex2 = (ex is ApplicationException || ex is TaskCanceledException || ex is InvalidOperationException) ? ex : new ApplicationException("Failed to create chat completion", ex);
            TryAttachMessages(ex2, messages);
            throw;
        }
    }

    private async Task<MessageTool> CallTool(GroqToolCall call)
    {
        if (_tools.TryGetValue(call.ToolName.ToLower().Trim(), out var tool))
        {
            var toolResult = await tool.ExecuteAsync(call.Parameters);
            return new MessageTool
            {
                Role = MessageRoleType.Tool,
                Content = toolResult,
                ToolCallId = string.IsNullOrWhiteSpace(call.Id) ? Guid.NewGuid().ToString() : call.Id
            };
        }
        return null;
    }



    #endregion

    public async Task<string?> CreateChatCompletionAsync(
       params Message[] messages)
    {
        return await CreateChatCompletionAsync(messages?.ToList());
    }

    public async Task<string?> CreateChatCompletionAsync(IEnumerable<Message> messages, CancellationToken? cancellationToken = null)
    {
        var messageList = messages?.ToList();
        
        if (messageList == null || messageList.Count == 0)
        {
            if (_defaultSystemMessage != null)
            {
                messageList = new List<Message> { _defaultSystemMessage };
            }
            else
            {
                var argEx = new ArgumentException("Messages cannot be null or empty", nameof(messages));
                TryAttachMessages(argEx, messageList);
                throw argEx;
            }
        }

        var request = new GroqClientRequest
        {
            Model = _model,
            Temperature = _temperature,
            Seed = _randomSeed ? _random.Next() : _seed,
            Messages = messageList.ToArray(),
            MaxTokens = _maxTokens,
            TopP = _topP,
            Stop = _stop,
            JsonResponse = _jsonResponse,
            ReasoningFormat = _reasoningFormat,
            ReasoningEffort = _reasoningEffort,
            ServiceTier = _serviceTier
        };

        try
        {
            string requestJson = request.ToJson();
            var httpContent = new StringContent(requestJson, Encoding.UTF8, ContentTypeJson);
            HttpResponseMessage response;

            if (cancellationToken != null)
            {
                response = await _client.PostAsync(_baseUrl, httpContent, cancellationToken.Value);
            }
            else
            {
                response = await _client.PostAsync(_baseUrl, httpContent);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                var httpEx = new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
                TryAttachMessages(httpEx, messageList);
                throw httpEx;
            }

            var chatResponse = GroqClientResponse.TryCreateFromJson(await response.Content.ReadAsStringAsync());
            if (chatResponse != null &&
                chatResponse.Contents != null)
                return chatResponse.Contents.FirstOrDefault();
            return null;
        }
        catch (Exception ex)
        {
            var ex2 = (ex is ApplicationException || ex is TaskCanceledException || ex is InvalidOperationException) ? ex : new ApplicationException("Failed to create chat completion", ex);
            TryAttachMessages(ex2, messageList);
            throw ex2;
        }
    }

    public async Task<string?> CreateChatCompletionWithToolsAsync(
        List<Message> messages,
        int depth = 0,
        CancellationToken? cancellationToken = null)
    {
        try
        {
            if (depth >= _maxToolInvocationDepth)
            {
                var exception = new InvalidOperationException("Maximum tool invocation depth exceeded, possible loop detected.");
                TryAttachMessages(exception, messages);
                throw exception;
            }
            if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested)
            {
                var exception = new TaskCanceledException("Task was cancelled");
                TryAttachMessages(exception, messages);
                throw exception;
            }
            if (messages == null || messages.Count == 0)
            {
                if (_defaultSystemMessage != null)
                {
                    messages = new List<Message> { _defaultSystemMessage };
                }
                else
                {
                    var argEx = new ArgumentException("Messages cannot be null or empty", nameof(messages));
                    TryAttachMessages(argEx, messages);
                    throw argEx;
                }
            }

            // Build request with potential tools included
            var toolSpecs = BuildToolSpecifications();

        var request = new GroqClientRequest
        {
            Model = _model,
            Messages = messages.ToArray(),
            Tools = toolSpecs,
            ToolChoice = "auto",
            Temperature = _temperature,
            Seed = _randomSeed ? _random.Next() : _seed,
            MaxTokens = _maxTokens,
            TopP = _topP,
            Stop = _stop,
            JsonResponse = _jsonResponse,
            ReasoningFormat = _reasoningFormat,
            ReasoningEffort = _reasoningEffort,
            ServiceTier = _serviceTier
        };

            string requestJson = request.ToJson();
            var content = new StringContent(requestJson, Encoding.UTF8, ContentTypeJson);
            HttpResponseMessage response;
            if (cancellationToken != null)
            {
                response = await _client.PostAsync(_baseUrl, content, cancellationToken.Value);
            }
            else
            {
                response = await _client.PostAsync(_baseUrl, content);
            }
            if (!response.IsSuccessStatusCode)
            {
                var httpEx = new HttpRequestException($"API request failed: {await response.Content.ReadAsStringAsync()}");
                TryAttachMessages(httpEx, messages);
                throw httpEx;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var chatResponse = GroqClientResponse.TryCreateFromJson(responseJson);
            return await HandleToolResponsesAndReinvokeAsync(messages, chatResponse, depth + 1, cancellationToken);
        }
        catch (Exception ex)
        {
            var ex2 = (ex is ApplicationException || ex is TaskCanceledException || ex is InvalidOperationException) ? ex : new ApplicationException("Failed to create chat completion", ex);
            TryAttachMessages(ex2, messages);
            throw ex2;
        }
    }


    public async Task<string?> GetStructuredChatCompletionAsync(
        string jsonStructure,
        params Message[] messages)
    {
        if (messages == null || messages.Length == 0)
        {
            if (_defaultSystemMessage != null)
            {
                messages = [_defaultSystemMessage];
            }
            else
            {
                throw new ArgumentException("Messages cannot be null or empty", nameof(messages));
            }
        }

        // Sanitize jsonStructure
        try
        {
            var jsonObject = JsonSerializer.Deserialize<JsonElement>(jsonStructure);
            jsonStructure = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON format", nameof(jsonStructure), ex);
        }

        // Check if a system message is present
        var systemMessageIndex = messages.ToList().FindIndex(m => m.Role == MessageRoleType.System);
        if (systemMessageIndex != -1)
        {
            // Extend the existing system message
            messages[systemMessageIndex].Content += $"\nIMPORTANT: Please respond ONLY in JSON format as follows:\n{jsonStructure}";
        }
        else
        {
            // Create a new system message with explicit instructions
            var newSystemMessage = new Message
            {
                Role = MessageRoleType.System,
                Content = $"IMPORTANT: Please respond ONLY in JSON format as follows:\n{jsonStructure}"
            };
            messages = new Message[] { newSystemMessage }.Concat(messages).ToArray();
        }

        var currentMessages = messages.ToList();
        var currentRequestJson = "";
        for (int attempt = 1; attempt <= _maxStructuredRetryAttempts; attempt++)
        {
            var request = new GroqClientRequest
            {
                Model = _model,
                Temperature = _temperature,
                Seed = _randomSeed ? _random.Next() : _seed,
                Messages = currentMessages.ToArray(),
                MaxTokens = _maxTokens,
                TopP = _topP,
                Stop = _stop,
                JsonResponse = _jsonResponse,
                ReasoningFormat = _reasoningFormat,
                ReasoningEffort = _reasoningEffort,
                ServiceTier = _serviceTier                
            };

            try
            {
                currentRequestJson = request.ToJson();
                var httpContent = new StringContent(currentRequestJson, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _client.PostAsync(_baseUrl, httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    var httpEx = new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
                    TryAttachMessages(httpEx, currentMessages);
                    throw httpEx;
                }

                var chatResponse = GroqClientResponse.TryCreateFromJson(await response.Content.ReadAsStringAsync());
                return chatResponse?.Contents?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                if (attempt == _maxStructuredRetryAttempts)
                {
                    var ex2 = (ex is ApplicationException || ex is TaskCanceledException || ex is InvalidOperationException) ? ex : new ApplicationException("Failed to create chat completion", ex);
                    TryAttachMessages(ex2, currentMessages);
                    throw ex2;
                }
            }
        }

        return null;
    }

    public async Task<TResponse?> GetStructuredChatCompletionAsync<TResponse>(
        params Message[] messages)
        where TResponse : class, new()
    {
        var messageList = messages?.ToList() ?? new List<Message>();
        
        try
        {
            if (messageList.Count == 0)
            {
                var argEx = new ArgumentException("Messages cannot be null or empty");
                TryAttachMessages(argEx, messageList);
                throw argEx;
            }

            // Generate the JSON structure from the response type
            string jsonStructure = JsonStructureUtility.CreateJsonStructureFromType(typeof(TResponse), _typeCache);

            // Extend the system message to include the JSON structure
            var systemMessageIndex = messageList.FindIndex(m => m.Role == MessageRoleType.System);
            if (systemMessageIndex != -1)
            {
                messageList[systemMessageIndex].Content += $" IMPORTANT: Please respond ONLY in JSON format as follows: {jsonStructure}";
            }
            else
            {
                // Add a new system message if none exists
                var newSystemMessage = new Message
                {
                    Role = MessageRoleType.System,
                    Content = $"IMPORTANT: Please respond ONLY in JSON format as follows:\n{jsonStructure}"
                };
                messageList.Insert(0, newSystemMessage);
            }
            messages = messageList.ToArray();

            for (int attempt = 1; attempt <= _maxStructuredRetryAttempts; attempt++)
            {
                try
                {
                    // Call the existing method to get the JSON response
                    string jsonResponse = await GetStructuredChatCompletionAsync(jsonStructure, messages);

                    if (string.IsNullOrEmpty(jsonResponse))
                    {
                        var invalidOpEx = new InvalidOperationException("Received an empty response from the API.");
                        TryAttachMessages(invalidOpEx, messageList);
                        throw invalidOpEx;
                    }

                    // Deserialize the JSON response back into the expected type
                    TResponse responsePoco = JsonSerializer.Deserialize<TResponse>(jsonResponse);
                    if (responsePoco == null)
                    {
                        var invalidOpEx = new InvalidOperationException("Failed to deserialize the response.");
                        TryAttachMessages(invalidOpEx, messageList);
                        throw invalidOpEx;
                    }

                    return responsePoco;
                }
                catch (Exception ex)
                {
                    if (attempt == _maxStructuredRetryAttempts)
                    {
                        var appEx = new ApplicationException("Failed to create chat completion", ex);
                        TryAttachMessages(appEx, messageList);
                        throw appEx;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            TryAttachMessages(ex, messageList);
            throw;
        }
    }

    public async IAsyncEnumerable<string> CreateChatCompletionStreamAsync(
        params Message[] messages)
    {

        await foreach (var message in CreateChatCompletionStreamAsync(messages?.ToList()))
        {
            yield return message;
        }
    }

    public async IAsyncEnumerable<string> CreateChatCompletionStreamAsync(IEnumerable<Message> messages, CancellationToken? cancellationToken = null)
    {
        var messageList = messages?.ToList();
        
        if (messageList == null || messageList.Count == 0)
        {
            var argEx = new ArgumentException("Messages cannot be null or empty", nameof(messages));
            TryAttachMessages(argEx, messageList);
            throw argEx;
        }

        var request = new GroqClientRequest
        {
            Model = _model,
            Temperature = _temperature,
            Seed = _randomSeed ? _random.Next() : _seed,
            Messages = messageList.ToArray(),
            MaxTokens = _maxTokens,
            TopP = _topP,
            Stop = _stop,
            Stream = true,
            JsonResponse = _jsonResponse,
            ReasoningFormat = _reasoningFormat,
            ReasoningEffort = _reasoningEffort,
            ServiceTier = _serviceTier
        };

        string requestJson = request.ToJson();
        var httpContent = new StringContent(requestJson, Encoding.UTF8, ContentTypeJson);

        HttpResponseMessage response = null;
        
        try
        {
            if (cancellationToken != null)
            {
                response = await _client.PostAsync(_baseUrl, httpContent, cancellationToken.Value);
            }
            else
            {
                response = await _client.PostAsync(_baseUrl, httpContent);
            }
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                var httpEx = new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
                TryAttachMessages(httpEx, messageList);
                throw httpEx;
            }
        }
        catch (Exception ex)
        {
            var ex2 = (ex is ApplicationException || ex is TaskCanceledException || ex is InvalidOperationException) ? ex : new ApplicationException("Failed to create chat completion", ex);
            TryAttachMessages(ex2, messageList);
            throw ex2;
        }

        // Stream the results after successful setup
        using var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream);
        
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith(DataPrefix))
            {
                var data = line.Substring(DataPrefix.Length);
                if (data != StreamDoneSignal)
                {
                    string text = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        var jsonElement = doc.RootElement;
                        if (jsonElement.TryGetProperty(ChoicesKey, out var choices) && choices.GetArrayLength() > 0)
                        {
                            var firstChoice = choices[0];
                            if (firstChoice.TryGetProperty(DeltaKey, out var delta) && delta.TryGetProperty(ContentKey, out var content))
                            {
                                text = content.GetString();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed JSON lines in the stream
                        continue;
                    }
                    
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return text;
                    }
                }
            }
        }
    }


    #endregion
}