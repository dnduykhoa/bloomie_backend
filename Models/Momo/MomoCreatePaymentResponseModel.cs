namespace Bloomie.Models.Momo
{
    public class MomoCreatePaymentResponseModel
    {
        public string RequestId { get; set; }
        public string ErrorCode { get; set; }
        public string OrderId { get; set; }
        public string Message { get; set; }
        public string LocalMessage { get; set; }
        public string RequestType { get; set; }
        public string PayUrl { get; set; }
        public string Signature { get; set; }
        public string QrCodeUrl { get; set; }
        public string Deeplink {  get; set; }
        public string DeeplinkWebInApp { get; set; }
        public int ResultCode { get; set; }
    }
}
