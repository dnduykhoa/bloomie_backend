using System;

namespace Bloomie.Models.Entities
{
    public class ReplyImage
    {
        public int Id { get; set; }
        public int ReplyId { get; set; }
        public string ImageUrl { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.Now;
        public virtual Reply Reply { get; set; }
    }
}
