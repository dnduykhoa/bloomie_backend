using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserLike
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public int? RatingId { get; set; } // Có thể null nếu thích một Reply
        public Rating Rating { get; set; }
        public int? ReplyId { get; set; } // Có thể null nếu thích một Rating
        public Reply Reply { get; set; }
        public DateTime LikedAt { get; set; } // Ngày giờ thích
    }
}