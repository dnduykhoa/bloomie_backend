using System.Security.Cryptography;
using System.Text;
using Bloomie.Models.Entities;

namespace Bloomie.Extensions
{
    public static class LoginHistoryExtensions
    {
        public static string GenerateSecurityToken(this LoginHistory history)
        {
            var data = Encoding.UTF8.GetBytes($"{history.SessionId}{history.UserId}BloomieSecretKey2025!@#");
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToBase64String(hashBytes);
        }
    }
}