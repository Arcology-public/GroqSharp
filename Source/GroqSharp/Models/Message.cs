using GroqSharp.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroqSharp.Models
{
    public class Message
    {
        #region Instance Properties

        [JsonConverter(typeof(LowercaseEnumConverter<MessageRoleType>))]
        public MessageRoleType Role { get; set; }

        public virtual string Content { get; set; }

        #endregion

        #region Constructors

        // Constructor that takes role and content
        public Message(MessageRoleType role, string content)
        {
            Role = role;
            Content = content;
        }

        // Parameterless constructor for JSON deserialization
        public Message()
        {
            Content = "";
        }

        #endregion

        #region Instance Methods

        public virtual string ToJson()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            };

            return JsonSerializer.Serialize(this, options);
        }

        #endregion
    }
}