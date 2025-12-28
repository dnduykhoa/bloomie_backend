using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Report
    {
        public int Id { get; set; }
        public int RatingId { get; set; }
        public string ReporterId { get; set; }
        public string Reason { get; set; } // Lý do báo cáo
        public DateTime ReportDate { get; set; }
        public bool IsResolved { get; set; } // Trạng thái đã xử lý hay chưa
        public string? ResolvedBy { get; set; } // Người xử lý
        public DateTime? ResolvedDate { get; set; } // Thời gian xử lý
        public virtual Rating Rating { get; set; }
        public virtual ApplicationUser Reporter { get; set; }
    }
}