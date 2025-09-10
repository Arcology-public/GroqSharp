using System;
using System.Text.Json.Serialization;

namespace GroqSharp.Models
{
    public abstract class ContentItem
    {
        [JsonPropertyName("type")]
        public abstract string Type { get; }
    }

    public class TextContent : ContentItem
    {
        public override string Type => "text";

        [JsonPropertyName("text")]
        public string Text { get; set; }

        // Constructor for text string
        public TextContent(string text)
        {
            Text = text;
        }

        // Parameterless constructor for JSON deserialization
        public TextContent()
        {
            Text = "";
        }
    }

    public class ImageContent : ContentItem
    {
        public override string Type => "image_url";

        [JsonPropertyName("image_url")]
        public ImageUrl ImageUrl { get; set; }

        // Constructor for URL string
        public ImageContent(string imageUrl)
        {
            ImageUrl = new ImageUrl { Url = imageUrl };
        }

        // Constructor for byte array with image type
        public ImageContent(byte[] imageBytes, string imageType)
        {
            var base64String = Convert.ToBase64String(imageBytes);
            var dataUrl = $"data:image/{imageType};base64,{base64String}";
            ImageUrl = new ImageUrl { Url = dataUrl };
        }

        // Parameterless constructor for JSON deserialization
        public ImageContent()
        {
            ImageUrl = new ImageUrl { Url = "" };
        }
    }

    public class ImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }
}