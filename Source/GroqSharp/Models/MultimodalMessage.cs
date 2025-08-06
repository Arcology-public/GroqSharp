using GroqSharp.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GroqSharp.Models
{
    public class MultimodalMessage : Message
    {
        private ContentItem[] _contentArray;

        [JsonIgnore]
        public ContentItem[] ContentArray
        {
            get => _contentArray;
            set
            {
                _contentArray = value;
                // Set the base Content to empty string when using array content
                base.Content = "";
            }
        }

        #region Constructors

        // Constructor that takes role and content items
        public MultimodalMessage(MessageRoleType role, IEnumerable<ContentItem> contentItems)
            : base(role, "")
        {
            ContentArray = contentItems?.ToArray() ?? new ContentItem[0];
        }

        // Constructor that takes role and string content (uses base constructor)
        public MultimodalMessage(MessageRoleType role, string content)
            : base(role, content)
        {
            _contentArray = null;
        }

        // Parameterless constructor for JSON deserialization
        public MultimodalMessage()
            : base()
        {
        }

        #endregion

        public override string ToJson()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null, // Don't apply camelCase to respect JsonPropertyName attributes
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Create an anonymous object with the correct structure
            object messageObject;
            if (_contentArray != null && _contentArray.Length > 0)
            {
                // Manually construct the content array to ensure proper serialization
                var contentList = new List<object>();
                foreach (var item in _contentArray)
                {
                    if (item is TextContent textContent)
                    {
                        contentList.Add(new
                        {
                            type = "text",
                            text = textContent.Text
                        });
                    }
                    else if (item is ImageContent imageContent)
                    {
                        contentList.Add(new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = imageContent.ImageUrl?.Url
                            }
                        });
                    }
                }
                
                messageObject = new
                {
                    role = Role.ToString().ToLowerInvariant(),
                    content = contentList
                };
            }
            else
            {
                messageObject = new
                {
                    role = Role.ToString().ToLowerInvariant(),
                    content = Content
                };
            }

            return JsonSerializer.Serialize(messageObject, options);
        }
    }
}