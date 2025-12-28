using System;

namespace Bloomie.Models.Entities
{
    public class RatingImage
    {
        public int Id { get; set; }
        public int RatingId { get; set; }
        public string ImageUrl { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.Now;
        public virtual Rating Rating { get; set; }
    }
}
