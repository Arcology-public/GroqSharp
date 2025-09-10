using GroqSharp.Models;
using System.Text.Json;
using Xunit;

namespace GroqSharp.Tests.Models
{
    public class MultimodalMessageTests
    {
        [Fact]
        public void ToJson_WithTextContent_SerializesCorrectly()
        {
            // Arrange
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = "" // Required but will be ignored when ContentArray is set
            };
            message.ContentArray = new ContentItem[]
            {
                new TextContent("What's in this image?")
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
            Assert.True(doc.RootElement.GetProperty("content").ValueKind == JsonValueKind.Array);
            
            var contentArray = doc.RootElement.GetProperty("content");
            Assert.Equal(1, contentArray.GetArrayLength());
            
            var firstItem = contentArray[0];
            Assert.Equal("text", firstItem.GetProperty("type").GetString());
            Assert.Equal("What's in this image?", firstItem.GetProperty("text").GetString());
        }

        [Fact]
        public void ToJson_WithImageContent_SerializesCorrectly()
        {
            // Arrange
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = "" // Required but will be ignored when ContentArray is set
            };
            message.ContentArray = new ContentItem[]
            {
                new ImageContent("https://upload.wikimedia.org/wikipedia/commons/f/f2/LPU-v1-die.jpg")
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
            Assert.True(doc.RootElement.GetProperty("content").ValueKind == JsonValueKind.Array);
            
            var contentArray = doc.RootElement.GetProperty("content");
            Assert.Equal(1, contentArray.GetArrayLength());
            
            var firstItem = contentArray[0];
            Assert.Equal("image_url", firstItem.GetProperty("type").GetString());
            
            var imageUrl = firstItem.GetProperty("image_url");
            Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/f/f2/LPU-v1-die.jpg", 
                imageUrl.GetProperty("url").GetString());
        }

        [Fact]
        public void ToJson_WithMixedContent_SerializesCorrectly()
        {
            // Arrange
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = "" // Required but will be ignored when ContentArray is set
            };
            message.ContentArray = new ContentItem[]
            {
                new TextContent("What's in this image?"),
                new ImageContent("https://upload.wikimedia.org/wikipedia/commons/f/f2/LPU-v1-die.jpg")
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
            Assert.True(doc.RootElement.GetProperty("content").ValueKind == JsonValueKind.Array);
            
            var contentArray = doc.RootElement.GetProperty("content");
            Assert.Equal(2, contentArray.GetArrayLength());
            
            var textItem = contentArray[0];
            Assert.Equal("text", textItem.GetProperty("type").GetString());
            Assert.Equal("What's in this image?", textItem.GetProperty("text").GetString());
            
            var imageItem = contentArray[1];
            Assert.Equal("image_url", imageItem.GetProperty("type").GetString());
            var imageUrl = imageItem.GetProperty("image_url");
            Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/f/f2/LPU-v1-die.jpg", 
                imageUrl.GetProperty("url").GetString());
        }

        [Fact]
        public void ToJson_WithStringContent_SerializesAsString()
        {
            // Arrange
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = "Simple string content"
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
            Assert.True(doc.RootElement.GetProperty("content").ValueKind == JsonValueKind.String);
            Assert.Equal("Simple string content", doc.RootElement.GetProperty("content").GetString());
        }

        [Fact]
        public void BackwardCompatibility_MessageWorksAsExpected()
        {
            // Arrange
            var message = new Message(MessageRoleType.User, "Regular message");

            // Act
            var json = message.ToJson();

            // Assert
            Assert.Contains("\"role\":\"user\"", json);
            Assert.Contains("\"content\":\"Regular message\"", json);
        }

        [Fact]
        public void MultimodalMessage_CanBeUsedAsMessage()
        {
            // Arrange
            Message message = new MultimodalMessage(MessageRoleType.Assistant, "This is a response");

            // Act
            var json = message.ToJson();

            // Assert
            Assert.Contains("\"role\":\"assistant\"", json);
            Assert.Contains("\"content\":\"This is a response\"", json);
        }

        [Fact]
        public void ImageContent_WithUrl_SerializesCorrectly()
        {
            // Arrange
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = ""
            };
            message.ContentArray = new ContentItem[]
            {
                new ImageContent("https://example.com/image.jpg")
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            var contentArray = doc.RootElement.GetProperty("content");
            var imageItem = contentArray[0];
            var imageUrl = imageItem.GetProperty("image_url");
            Assert.Equal("https://example.com/image.jpg", imageUrl.GetProperty("url").GetString());
        }

        [Fact]
        public void ImageContent_WithByteArray_CreatesDataUrl()
        {
            // Arrange
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header bytes
            var message = new MultimodalMessage
            {
                Role = MessageRoleType.User,
                Content = ""
            };
            message.ContentArray = new ContentItem[]
            {
                new ImageContent(imageBytes, "png")
            };

            // Act
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            // Assert
            var contentArray = doc.RootElement.GetProperty("content");
            var imageItem = contentArray[0];
            var imageUrl = imageItem.GetProperty("image_url");
            var url = imageUrl.GetProperty("url").GetString();
            
            Assert.StartsWith("data:image/png;base64,", url);
            Assert.Contains("iVBORw==", url); // Base64 of the PNG header bytes
        }

        [Fact]
        public void MultimodalMessage_WithConstructor_CreatesCorrectly()
        {
            // Arrange & Act
            var message = new MultimodalMessage(
                MessageRoleType.User,
                new ContentItem[]
                {
                    new TextContent("Describe this image"),
                    new ImageContent("https://example.com/photo.jpg")
                }
            );

            // Assert
            var json = message.ToJson();
            var doc = JsonDocument.Parse(json);

            Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
            Assert.True(doc.RootElement.GetProperty("content").ValueKind == JsonValueKind.Array);
            
            var contentArray = doc.RootElement.GetProperty("content");
            Assert.Equal(2, contentArray.GetArrayLength());
        }

        [Fact]
        public void Message_WithConstructor_CreatesCorrectly()
        {
            // Arrange & Act
            var message = new Message(MessageRoleType.System, "You are a helpful assistant.");

            // Assert
            Assert.Equal(MessageRoleType.System, message.Role);
            Assert.Equal("You are a helpful assistant.", message.Content);
            
            var json = message.ToJson();
            Assert.Contains("\"role\":\"system\"", json);
            Assert.Contains("\"content\":\"You are a helpful assistant.\"", json);
        }
    }
}