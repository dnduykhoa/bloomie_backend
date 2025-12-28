using Microsoft.Extensions.Options;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Momo;
using System.Text;
using Newtonsoft.Json;
using RestSharp;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Bloomie.Models;
using System.Reflection.Metadata.Ecma335;

namespace Bloomie.Services.Implementations
{
    public class MomoService : IMomoService
    {
        private readonly IOptions<MomoOptionModel> _options;

        public MomoService(IOptions<MomoOptionModel> options)
        {
            _options = options;
        }

        public async Task<MomoCreatePaymentResponseModel> CreatePaymentMomo(OrderInfoModel model)
        {
            // Sử dụng OrderId từ hệ thống thay vì tự tạo mới
            var orderId = model.OrderId ?? DateTime.UtcNow.Ticks.ToString();
            var requestId = DateTime.UtcNow.Ticks.ToString();
            var amount = ((long)model.Amount).ToString();
            var orderInfo = "Khách hàng: " + model.FullName + ". " + model.OrderInformation;
            
            // Tạo rawData theo đúng thứ tự Momo yêu cầu
            var rawData =
                $"accessKey={_options.Value.AccessKey}" +
                $"&amount={amount}" +
                $"&extraData=" +
                $"&ipnUrl={_options.Value.NotifyUrl}" +
                $"&orderId={orderId}" +
                $"&orderInfo={orderInfo}" +
                $"&partnerCode={_options.Value.PartnerCode}" +
                $"&redirectUrl={_options.Value.ReturnUrl}" +
                $"&requestId={requestId}" +
                $"&requestType={_options.Value.RequestType}";

            var signature = ComputeHmacSha256(rawData, _options.Value.SecretKey);

            // Gửi yêu cầu đến MoMo
            var client = new RestClient(_options.Value.MomoApiUrl);
            var request = new RestRequest();
            request.Method = Method.Post;
            request.AddHeader("Content-Type", "application/json; charset=UTF-8");

            var requestData = new
            {
                partnerCode = _options.Value.PartnerCode,
                partnerName = "Test",
                storeId = "MomoTestStore",
                requestId = requestId,
                amount = amount,
                orderId = orderId,
                orderInfo = orderInfo,
                redirectUrl = _options.Value.ReturnUrl,
                ipnUrl = _options.Value.NotifyUrl,
                lang = "vi",
                extraData = "",
                requestType = _options.Value.RequestType,
                signature = signature
            };
            
            request.AddJsonBody(requestData);
            var response = await client.ExecuteAsync(request);
            if (response.Content == null)
                return null;
            return JsonConvert.DeserializeObject<MomoCreatePaymentResponseModel>(response.Content);
        }

        public MomoExecuteResponseModel PaymentExecuteAsync(IQueryCollection collection)
        {
            var amount = collection.FirstOrDefault(s => s.Key == "amount").Value;
            var orderInfo = collection.FirstOrDefault(s => s.Key == "orderInfo").Value;
            var orderId = collection.FirstOrDefault(s => s.Key == "orderId").Value;
            var resultCode = collection["resultCode"];

            return new MomoExecuteResponseModel
            {
                Amount = amount,
                OrderId = orderId,
                OrderInfo = orderInfo,
                ResultCode = int.TryParse(resultCode, out var code) ? code : -1
            };
        }

        private string ComputeHmacSha256(string message, string secretKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            byte[] hashBytes;

            using (var hmac = new HMACSHA256(keyBytes))
            {
                hashBytes = hmac.ComputeHash(messageBytes);
            }

            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return hashString;
        }
    }
}