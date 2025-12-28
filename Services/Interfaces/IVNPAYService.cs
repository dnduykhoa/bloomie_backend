using Bloomie.Models.Vnpay;

namespace Bloomie.Services.Interfaces
{
    public interface IVNPAYService
    {
        string CreatePaymentUrl(PaymentInformationModel model, HttpContext context);
        PaymentResponseModel PaymentExecute(IQueryCollection collections);
    }
}
