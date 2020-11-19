namespace SalesTabletAppPayments.Models
{
    public class PaymentModel
    {
        public string ShopperIpAddress { get; set; }
        public string ShopperSessionId { get; set; }
        public string ShopperUserAgent { get; set; }
        public string ShopperAcceptHeader { get; set; }
        public string OrderCode { get; set; }

    }
}