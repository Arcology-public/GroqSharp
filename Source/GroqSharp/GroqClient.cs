﻿using GroqSharp;
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

    private async Task<string> HandleToolResponsesAndReinvokeAsync(
        List<Message> messages,
        GroqClientResponse response,
        int depth,
        CancellationToken? cancellationToken)
    {

        // First check if max depth is exceeded
        if (depth >= _maxToolInvocationDepth)
        {
            var exception = new InvalidOperationException("Maximum tool invocation depth exceeded, possible loop detected.");
            exception.Data["Messages"] = messages;
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
        if (messages == null || messages.Count() == 0)
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

        var request = new GroqClientRequest
        {
            Model = _model,
            Temperature = _temperature,
            Seed = _randomSeed ? _random.Next() : _seed,
            Messages = messages.ToArray(),
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
                throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
            }

            var chatResponse = GroqClientResponse.TryCreateFromJson(await response.Content.ReadAsStringAsync());
            if (chatResponse != null &&
                chatResponse.Contents != null)
                return chatResponse.Contents.FirstOrDefault();
            return null;
        }
        catch(TaskCanceledException ex)
        {
            throw new TaskCanceledException("Task was cancelled", ex);
        }
        catch (Exception ex)
        {
            throw new ApplicationException("Failed to create chat completion", ex);
        }
    }

    public async Task<string?> CreateChatCompletionWithToolsAsync(
        List<Message> messages,
        int depth = 0,
        CancellationToken? cancellationToken = null)
    {
        if (depth >= _maxToolInvocationDepth)
        {
            var exception = new InvalidOperationException("Maximum tool invocation depth exceeded, possible loop detected.");
            exception.Data["Messages"] = messages;
            throw exception;
        }
        if (cancellationToken.HasValue && cancellationToken.Value.IsCancellationRequested)
        {
            var exception = new TaskCanceledException("Task was cancelled");
            exception.Data["Messages"] = messages;
            throw exception;
        }
        if (messages == null || messages.Count == 0)
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
            throw new HttpRequestException($"API request failed: {await response.Content.ReadAsStringAsync()}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var chatResponse = GroqClientResponse.TryCreateFromJson(responseJson);
        return await HandleToolResponsesAndReinvokeAsync(messages, chatResponse, depth + 1, cancellationToken);
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
                    throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
                }

                var chatResponse = GroqClientResponse.TryCreateFromJson(await response.Content.ReadAsStringAsync());
                return chatResponse?.Contents?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                if (attempt == _maxStructuredRetryAttempts)
                {
                    throw new ApplicationException("Failed to create chat completion", ex);
                }
            }
        }

        return null;
    }

    public async Task<TResponse?> GetStructuredChatCompletionAsync<TResponse>(
        params Message[] messages)
        where TResponse : class, new()
    {
        if (messages == null || messages.Length == 0)
        {
            throw new ArgumentException("Messages cannot be null or empty");
        }

        // Generate the JSON structure from the response type
        string jsonStructure = JsonStructureUtility.CreateJsonStructureFromType(typeof(TResponse), _typeCache);

        // Extend the system message to include the JSON structure
        var systemMessageIndex = messages.ToList().FindIndex(m => m.Role == MessageRoleType.System);
        if (systemMessageIndex != -1)
        {
            messages[systemMessageIndex].Content += $" IMPORTANT: Please respond ONLY in JSON format as follows: {jsonStructure}";
        }
        else
        {
            // Add a new system message if none exists
            var newSystemMessage = new Message
            {
                Role = MessageRoleType.System,
                Content = $"IMPORTANT: Please respond ONLY in JSON format as follows:\n{jsonStructure}"
            };
            var messageList = new List<Message>(messages) { newSystemMessage };
            messages = messageList.ToArray();
        }

        for (int attempt = 1; attempt <= _maxStructuredRetryAttempts; attempt++)
        {
            try
            {
                // Call the existing method to get the JSON response
                string jsonResponse = await GetStructuredChatCompletionAsync(jsonStructure, messages);

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    throw new InvalidOperationException("Received an empty response from the API.");
                }

                // Deserialize the JSON response back into the expected type
                TResponse responsePoco = JsonSerializer.Deserialize<TResponse>(jsonResponse) ??
                                         throw new InvalidOperationException("Failed to deserialize the response.");

                return responsePoco;
            }
            catch (Exception ex)
            {
                if (attempt == _maxStructuredRetryAttempts)
                {
                    throw new ApplicationException("Failed to create chat completion", ex);
                }
            }
        }

        return null;
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
        if (messages == null || messages.Count() == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var request = new GroqClientRequest
        {
            Model = _model,
            Temperature = _temperature,
            Seed = _randomSeed ? _random.Next() : _seed,
            Messages = messages.ToArray(),
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
            throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorResponse}");
        }

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
                    using var doc = JsonDocument.Parse(data);
                    var jsonElement = doc.RootElement;
                    if (jsonElement.TryGetProperty(ChoicesKey, out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty(DeltaKey, out var delta) && delta.TryGetProperty(ContentKey, out var content))
                        {
                            var text = content.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return text;
                            }
                        }
                    }
                }
            }
        }
    }


    #endregion
}