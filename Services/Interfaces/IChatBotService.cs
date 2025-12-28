using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Bloomie.Services.Interfaces
{
    public interface IChatBotService
    {
        Task<string> GetResponseAsync(string userMessage);
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);
    }
}
