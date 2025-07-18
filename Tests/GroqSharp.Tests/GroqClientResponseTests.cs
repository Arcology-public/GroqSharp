﻿namespace GroqSharp.Tests
{
    public class GroqClientResponseTests
    {
        [Fact]
        public void FromJson_ValidJson_CreatesCorrectResponse()
        {
            // Arrange
            string json = "{\"choices\": [{\"message\": {\"content\": \"Hello World\"}}]}";

            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Single(response.Contents);
            Assert.Equal("Hello World", response.Contents[0]);
        }

        [Fact]
        public void FromJson_MissingChoices_ThrowsJsonException()
        {
            // Arrange
            string json = "{}";

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => GroqClientResponse.FromJson(json));
            Assert.Contains("Failed to parse JSON", exception.Message);
        }

        [Fact]
        public void FromJson_EmptyChoicesArray_CreatesEmptyResponse()
        {
            // Arrange
            string json = "{\"choices\": []}";

            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Empty(response.Contents);
        }

        [Fact]
        public void FromJson_InvalidJson_ThrowsArgumentException()
        {
            // Arrange
            string json = "{ this is not json }";

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => GroqClientResponse.FromJson(json));
            Assert.Contains("Failed to parse JSON", exception.Message);
        }

        [Fact]
        public void FromJson_MissingMessageOrContent_IgnoresSuchChoices()
        {
            // Arrange
            string json = "{\"choices\": [{\"message\": {}}, {\"notMessage\": {\"content\": \"Ignored\"}}]}";

            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Empty(response.Contents);
        }

        [Fact]
        public void FromJson_NullContent_IgnoresNullValues()
        {
            // Arrange
            string json = "{\"choices\": [{\"message\": {\"content\": null}}]}";

            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Empty(response.Contents); 
        }

        [Fact]
        public void TryCreateFromJson_InvalidJson_ReturnsNull()
        {
            // Arrange
            string json = "invalid json format";

            // Act
            var response = GroqClientResponse.TryCreateFromJson(json);

            // Assert
            Assert.Null(response);
        }

        [Fact]
        public void FromJson_ValidToolCalls_CreatesCorrectToolCalls()
        {
            // Arrange
            string json = "{\"choices\": [{\"message\": {\"content\": \"Example\", \"tool_calls\": [{\"id\": \"1\", \"type\": \"function\", \"function\": {\"name\": \"Sum\", \"arguments\": \"{\\\"a\\\": 1, \\\"b\\\": 2}\"}}]}}]}";

            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Single(response.ToolCalls);
            Assert.Equal("1", response.ToolCalls[0].Id);
            Assert.Equal("Sum", response.ToolCalls[0].ToolName);
        }



        [Fact]
        public void FromJson_ValidToolCallWithNullParameters_CreatesCorrectToolCalls()
        {
            // Arrange
            string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"1\",\"type\":\"function\",\"function\":{\"name\":\"parameterlessTool\",\"arguments\":\"null\"}}]}}]}";
            
            // Act
            var response = GroqClientResponse.FromJson(json);

            // Assert
            Assert.Single(response.ToolCalls);
            Assert.Equal("parameterlessTool", response.ToolCalls[0].ToolName);
        }
    }
}