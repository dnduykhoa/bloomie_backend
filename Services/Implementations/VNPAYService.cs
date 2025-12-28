using Bloomie.Libraries;
using Bloomie.Models.Vnpay;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class VNPAYService : IVNPAYService
    {
        private readonly IConfiguration _configuration;

        public VNPAYService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string CreatePaymentUrl(PaymentInformationModel model, HttpContext context)
        {
            var timeZoneById = TimeZoneInfo.FindSystemTimeZoneById(_configuration["TimeZoneId"]);
            var timeNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneById);
            var tick = DateTime.Now.Ticks.ToString();
            var pay = new VnpayLibrary();
            var urlCallBack = _configuration["Vnpay:ReturnUrl"];

            pay.AddRequestData("vnp_Version", _configuration["Vnpay:Version"]);
            pay.AddRequestData("vnp_Command", _configuration["Vnpay:Command"]);
            pay.AddRequestData("vnp_TmnCode", _configuration["Vnpay:TmnCode"]);
            pay.AddRequestData("vnp_Amount", ((int)model.Amount * 100).ToString());
            pay.AddRequestData("vnp_CreateDate", timeNow.ToString("yyyyMMddHHmmss"));
            pay.AddRequestData("vnp_CurrCode", _configuration["Vnpay:CurrCode"]);
            pay.AddRequestData("vnp_IpAddr", pay.GetIpAddress(context));
            pay.AddRequestData("vnp_Locale", _configuration["Vnpay:Locale"]);
            pay.AddRequestData("vnp_OrderInfo", model.OrderDescription);
            pay.AddRequestData("vnp_OrderType", model.OrderType);
            pay.AddRequestData("vnp_ReturnUrl", urlCallBack);
            pay.AddRequestData("vnp_TxnRef", model.TxnRef); // Dùng OrderId làm mã đơn hàng trên VNPAY
            pay.AddRequestData("vnp_ExpireDate", timeNow.AddMinutes(15).ToString("yyyyMMddHHmmss")); // Hết hạn sau 15 phút

            var paymentUrl =
                pay.CreateRequestUrl(_configuration["Vnpay:BaseUrl"], _configuration["Vnpay:HashSecret"]);

            return paymentUrl;
        }

        public PaymentResponseModel PaymentExecute(IQueryCollection collections)
        {
           var pay = new VnpayLibrary();
           var response = pay.GetFullResponseData(collections, _configuration["Vnpay:HashSecret"]);

           return response;
        }

        // public PaymentResponseModel PaymentExecute(IQueryCollection query)
        // {
        //     var vnpay = new VnpayLibrary();
        //     foreach (var (key, value) in query)
        //     {
        //         if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
        //         {
        //             vnpay.AddResponseData(key, value);
        //         }
        //     }

        //     var responseCode = vnpay.GetResponseData("vnp_ResponseCode");
        //     var success = responseCode == "00";
        //     var orderId = vnpay.GetResponseData("vnp_TxnRef"); // Lấy OrderId từ vnp_TxnRef

        //     return new PaymentResponseModel
        //     {
        //         Success = success,
        //         VnPayResponseCode = responseCode,
        //         OrderId = orderId
        //     };
        // }
    }
}
