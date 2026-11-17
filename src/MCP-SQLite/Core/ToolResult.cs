using System.Collections.Generic;

namespace MCP_SQLite.Core
{
    /// <summary>
    /// Represents the result of a tool execution.
    /// </summary>
    public class ToolResult
    {
        public bool IsError { get; set; }
        public string Content { get; set; }
        public List<ToolContent> ContentList { get; set; }

        private ToolResult() { }

        public static ToolResult Success(string content)
        {
            return new ToolResult
            {
                IsError = false,
                Content = content,
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = content } }
            };
        }

        public static ToolResult Error(string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = message,
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = $"Error: {message}" } }
            };
        }

        public static ToolResult Error(string code, string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = $"[{code}] {message}",
                ContentList = new List<ToolContent> { new ToolContent { Type = "text", Text = $"Error [{code}]: {message}" } }
            };
        }

        public static ToolResult Image(string base64Data, string mimeType = "image/png")
        {
            return new ToolResult
            {
                IsError = false,
                Content = $"[Image: {mimeType}]",
                ContentList = new List<ToolContent> { new ToolContent { Type = "image", Data = base64Data, MimeType = mimeType } }
            };
        }

        public static ToolResult SuccessWithImage(string text, string base64Data, string mimeType = "image/png")
        {
            return new ToolResult
            {
                IsError = false,
                Content = text,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = text },
                    new ToolContent { Type = "image", Data = base64Data, MimeType = mimeType }
                }
            };
        }

        public static ToolResult SuccessWithImages(string text, List<ToolResultImage> images)
        {
            return WithImages(text, images, false);
        }

        /// <summary>
        /// Text plus any captured images, with an explicit error flag. Used by batch
        /// execution so a failed run still returns the screenshots taken before the
        /// failure while correctly reporting isError to the caller.
        /// </summary>
        public static ToolResult WithImages(string text, List<ToolResultImage> images, bool isError)
        {
            var list = new List<ToolContent>
            {
                new ToolContent { Type = "text", Text = text }
            };

            if (images != null)
            {
                foreach (var img in images)
                {
                    if (img == null || string.IsNullOrEmpty(img.Data)) continue;
                    list.Add(new ToolContent
                    {
                        Type = "image",
                        Data = img.Data,
                        MimeType = string.IsNullOrEmpty(img.MimeType) ? "image/png" : img.MimeType
                    });
                }
            }

            return new ToolResult
            {
                IsError = isError,
                Content = text,
                ContentList = list
            };
        }
    }

    /// <summary>
    /// Lightweight image payload used when a tool returns multiple images.
    /// </summary>
    public class ToolResultImage
    {
        public string Data { get; set; }
        public string MimeType { get; set; } = "image/png";
    }

    public class ToolContent
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Data { get; set; }
        public string MimeType { get; set; }
    }
}
