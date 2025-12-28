using System;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.Entities
{
    public class Blog
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string Slug { get; set; } = string.Empty; // URL-friendly title

        [Required]
        public string Content { get; set; } = string.Empty; // Nội dung đầy đủ

        [MaxLength(500)]
        public string? Excerpt { get; set; } // Mô tả ngắn

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        [Required]
        public DateTime PublishDate { get; set; } = DateTime.Now;

        [MaxLength(100)]
        public string? Author { get; set; }

        public int ViewCount { get; set; } = 0;

        public bool IsPublished { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? UpdatedDate { get; set; }
    }
}
