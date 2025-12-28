using Bloomie.Models.ViewModels;
using Bloomie.Models.Entities;

namespace Bloomie.Services.Interfaces
{
    public interface IGeminiService
    {
        /// <summary>
        /// Generate a chatbot response using Gemini AI with product context
        /// </summary>
        /// <param name="userMessage">User's input message</param>
        /// <param name="productContext">Product information from database (optional)</param>
        /// <param name="conversationHistory">Recent conversation history (optional)</param>
        /// <returns>AI-generated response</returns>
        Task<string> GenerateResponseAsync(
            string userMessage, 
            string? productContext = null, 
            List<ChatMessage>? conversationHistory = null);

        /// <summary>
        /// Generate response with function calling support
        /// </summary>
        /// <param name="userMessage">User's input message</param>
        /// <param name="productContext">Product information from database (optional)</param>
        /// <param name="conversationHistory">Recent conversation history (optional)</param>
        /// <returns>Tuple of (response message, function calls if any)</returns>
        Task<(string Response, List<GeminiFunctionCall>? FunctionCalls)> GenerateResponseWithFunctionsAsync(
            string userMessage,
            string? productContext = null,
            List<ChatMessage>? conversationHistory = null);

        /// <summary>
        /// Detect user intent using Gemini AI
        /// </summary>
        /// <param name="message">User's message</param>
        /// <returns>Detected intent: greeting, price_inquiry, promotion_inquiry, product_search, advice, shipping, other</returns>
        Task<string> DetectIntentAsync(string message);

        /// <summary>
        /// Extract product keywords from user message
        /// </summary>
        /// <param name="message">User's message</param>
        /// <returns>List of product-related keywords</returns>
        Task<List<string>> ExtractProductKeywordsAsync(string message);
    }
}
