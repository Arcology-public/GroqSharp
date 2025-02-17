using GroqSharp.Models;
using GroqSharp.Tools;

namespace GroqSharp
{
    public interface IGroqClient
    {
        Task<string?> CreateChatCompletionAsync(
            params Message[] messages);

        Task<string?> CreateChatCompletionAsync(
         IEnumerable<Message> messages,
        CancellationToken? cancellationToken = null
        );

        Task<string?> CreateChatCompletionWithToolsAsync(
            List<Message> messages,
            int depth = 0,
            CancellationToken? cancellationToken = null);

        IAsyncEnumerable<string> CreateChatCompletionStreamAsync(
            params Message[] messages);  
        
        IAsyncEnumerable<string> CreateChatCompletionStreamAsync(
         IEnumerable<Message> messages,
        CancellationToken? cancellationToken = null
        );

        Task<string?> GetStructuredChatCompletionAsync(
            string jsonStructure,
            params Message[] messages);

        Task<TResponse?> GetStructuredChatCompletionAsync<TResponse>(
            params Message[] messages)
            where TResponse : class, new();

        IGroqClient SetBaseUrl(string baseUrl);

        IGroqClient SetDefaultSystemMessage(Message message);

        IGroqClient SetMaxTokens(int? maxTokens);

        IGroqClient SetModel(string model);

        IGroqClient SetStop(string stop);

        IGroqClient SetTemperature(double? temperature);

        IGroqClient SetTopP(double? topP);

        IGroqClient SetStructuredRetryPolicy(int maxRetryAttempts);

        IGroqClient RegisterTools(params IGroqTool[] tools);

        IGroqClient UnregisterTools(params string[] toolNames);
    }
}