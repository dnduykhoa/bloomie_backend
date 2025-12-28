using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Reply
    {
        public int Id { get; set; }
        public int RatingId { get; set; }
        public Rating Rating { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public string Comment { get; set; } // Nội dung bình luận
        public DateTime ReplyDate { get; set; }
        public int LikesCount { get; set; } = 0; // Số lượt thích, mặc định là 0
        public ICollection<UserLike> UserLikes { get; set; } = new List<UserLike>();
        public bool IsVisible { get; set; } = true; // Ẩn/Hiển thị
        public string LastModifiedBy { get; set; } // Người chỉnh sửa cuối cùng
        public DateTime? LastModifiedDate { get; set; } // Thời gian chỉnh sửa cuối

        public ICollection<ReplyImage> Images { get; set; } = new List<ReplyImage>();
    }
}