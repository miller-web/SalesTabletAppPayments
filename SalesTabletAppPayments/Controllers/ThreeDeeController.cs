using Newtonsoft.Json;
using SalesTabletAppPayments.Helper;
using SalesTabletAppPayments.Models;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Threading.Tasks;
using System.Web.Mvc;
using Worldpay.Sdk;
using Worldpay.Sdk.Enums;
using Worldpay.Sdk.Models;

namespace SalesTabletAppPayments.Controllers
{
    public class ThreeDeeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult StorePayment(PaymentModel model)
        {
            // Requires form post
            //shopperIpAddress
            //shopperSessionId
            //shopperUserAgent
            //shopperAcceptHeader
            //orderCode
            try
            {
                //Store in cache - use shopperSessionId as key
                CacheItemPolicy cacheItemPolicy = new CacheItemPolicy
                {
                    AbsoluteExpiration = DateTime.Now.AddHours(1.0)
                };
                ObjectCache cache = MemoryCache.Default;
                cache.Add(model.ShopperSessionId, model, cacheItemPolicy);
                Trace.TraceInformation($"Added {model.ShopperSessionId} / {model.OrderCode} to cache");
                string key = AesCrypto.EncyptString(model.ShopperSessionId);
                Trace.TraceInformation($"{model.ShopperSessionId} has key {key}");
                return Json(new { md = key });
            }
            catch (Exception x)
            {
                Trace.TraceError("StorePayment Exception {0} {1}", x, this);
                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError);// Json(new { success = false });
            }
        }

        public ActionResult WaitPayment()
        {
            Trace.TraceInformation("Process WaitPayment", this);
            var model = new ThreeDSModel
            {
                PaRes = HttpContext.Request.Form["PaRes"], //PaRes
                MD = HttpContext.Request.Form["MD"]
            };
            Trace.TraceInformation($"ReservationTermUrl - ReservationPayModel: PaRes {model.PaRes}  MD {model.MD}", this);
            return View(model);
        }

        public async Task<ActionResult> TestEmail()
        {
            Exception x = new Exception("Test error");
            string orderCode = "Test Order code here";
            string message = $"Exception when completing WorldPay payment from tablet\r\n\r\nDateTime:{DateTime.Now}\r\nOrder code: {orderCode}\r\nException: {x.Message} {x.StackTrace}";
            var from = new EmailAddress { Email = "mhweb.enquiries@miller.co.uk", Name = "MH Web" };
            //  var to = new EmailAddress { Email = "gareth.watt@miller.co.uk", Name = "Gareth Watt" };
            var tos = ConfigurationManager.AppSettings["ExceptionEmailRecipients"].Split(';').Select(e => new EmailAddress { Email = e }).ToList();
            SendGridMessage msg = new SendGridMessage { From = from, PlainTextContent = message, ReplyTo = from, Subject = "Sales centre payment exception!" };
            msg.AddHeader("Priority", "Urgent");
            msg.AddHeader("Importance", "high");

            msg.AddTos(tos);
            await SendEmailMessage(msg);
            return Content("OK");
        }

        public async Task<ActionResult> FinishPayment(ThreeDSModel model)
        {
            // Requires form post, comes as a redirect from bank, this is set as "TermUrl" as per WP docs
            //PaRes
            //MD (shopperSessionId)
            Trace.WriteLine($"Called back to FinishPayment");
            string orderCode = "UNKNOWN";
            string customerId = "";
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            PaymentResult result = new PaymentResult();
            string key = ConfigurationManager.AppSettings["WorldPayKey"];
            WorldpayRestClient restClient = new WorldpayRestClient("https://api.worldpay.com/v1", key);

            try
            {
                if (model == null)
                {
                    throw new Exception("Model null");
                }
                if (string.IsNullOrEmpty(model.MD))
                {
                    throw new Exception("model.MD null");
                }
                if (string.IsNullOrEmpty(model.PaRes))
                {
                    throw new Exception("model.PaRes null");
                }
                string cachekey = AesCrypto.DecryptString(model.MD);
                Trace.TraceInformation($"Cache key is {cachekey}");
                //Find stored payment details by MD
                ObjectCache cache = MemoryCache.Default;
                PaymentModel paymentModel = null;
                if (cache.Contains(cachekey))
                {
                    paymentModel = cache.Get(cachekey) as PaymentModel;
                    Trace.TraceInformation($"Got {cachekey} from cache");
                }
                else
                {
                    throw new Exception("Cannot find order details");
                }

                orderCode = paymentModel.OrderCode;

                var responseCode = this.HttpContext.Request.Form["PaRes"];
                var httpRequest = this.HttpContext.Request;

                ThreeDSecureInfo threeDSInfo = new ThreeDSecureInfo()
                {
                    shopperIpAddress = paymentModel.ShopperIpAddress,
                    shopperSessionId = paymentModel.ShopperSessionId,
                    shopperUserAgent = paymentModel.ShopperUserAgent,
                    shopperAcceptHeader = paymentModel.ShopperAcceptHeader
                };

                Trace.TraceInformation($"WP Payment ready -  threeDSInfo.shopperIpAddress: {threeDSInfo.shopperIpAddress} shopperSessionId: {threeDSInfo.shopperSessionId} shopperUserAgent: {threeDSInfo.shopperUserAgent} shopperAcceptHeader: {threeDSInfo.shopperAcceptHeader}");
                try
                {
                    var orderResponse = restClient.GetOrderService().Authorize(orderCode, responseCode, threeDSInfo);
                    string orderid = orderResponse.orderCode;
                    result.IsSuccess = orderResponse.paymentStatus.Equals(OrderStatus.SUCCESS);
                    Trace.TraceInformation($"WP Payment made: {result.IsSuccess} Order: {orderid} Status: {orderResponse.paymentStatus}");
                    // Remove from cache
                    cache.Remove(model.MD);
                    Trace.TraceInformation($"Cache size: {cache.Count()} items");
                    result.JsonString = JsonConvert.SerializeObject(orderResponse, new Newtonsoft.Json.Converters.StringEnumConverter());
                    return View(result);
                }
                catch (WorldpayException exc)
                {
                    result.IsSuccess = false;
                    Trace.TraceError("Worldpay Authorize WorldpayException {0} {1}", exc, this);
                    throw exc;
                }
                catch (Exception x)
                {
                    // var to = restClient.GetOrderService().FindOrder(orderCode);
                    result.IsSuccess = false;
                    Trace.TraceError("Worldpay Authorize Exception {0} {1}", x, this);
                    throw x;
                }
                //throw new Exception("Test exception");
            }
            catch (Exception x)
            {
                Trace.TraceError("Worldpay Authorize Exception {0} {1}", x, this);
                bool orderSuccess = false;
                if (!(x is WorldpayException))
                {
                    // try to get payment status 
                    string message = $"Exception when completing WorldPay payment from tablet\r\n\r\nDateTime: {DateTime.Now}\r\nOrder code: {orderCode}\r\nException: {x.Message} {x.StackTrace}";

                    try
                    {
                        var foundOrder = restClient.GetOrderService().FindOrder(orderCode);
                       
                        if (foundOrder != null)
                        {
                            message += $"\r\n\r\n** ORDER STATUS: {foundOrder.paymentResponse} {foundOrder.paymentStatus} {foundOrder.paymentStatusReason} **\r\n\r\n";
                            if (foundOrder.paymentStatus.ToString().Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
                            {
                                customerId = foundOrder.customerIdentifiers.Where(c => c.key == "id").FirstOrDefault().value;
                                message += $"\r\n\r\n** PAYMENT WAS TAKEN FROM CUSTOMER ACCOUNT {customerId} **\r\n\r\n";
                                orderSuccess = true;
                            }
                        }
                    }
                    catch (Exception xx)
                    {
                        // Log and swallow exception so we can still report the issue
                        Trace.TraceError("Exception: was unable to check order", xx, this);
                        message += "\r\nFailed to get last order\r\n";
                    }

                    var from = new EmailAddress { Email = "mhweb.enquiries@miller.co.uk", Name = "MH Web" };
                    //  var to = new EmailAddress { Email = "gareth.watt@miller.co.uk", Name = "Gareth Watt" };
                    var tos = ConfigurationManager.AppSettings["ExceptionEmailRecipients"].Split(';').Select(e => new EmailAddress { Email = e }).ToList();
                    SendGridMessage msg = new SendGridMessage { From = from, PlainTextContent = message, ReplyTo = from, Subject = "Sales centre payment exception!" };
                    msg.AddHeader("Priority", "Urgent");
                    msg.AddHeader("Importance", "high");
                    msg.AddTos(tos);
                    await SendEmailMessage(msg);
                }

                result.IsSuccess = orderSuccess;
                OrderResponse orderResponse;
                if (orderSuccess)
                {
                    var dic = new Dictionary<string, string>
                    {
                        { "id", customerId }
                    };
                    orderResponse = new OrderResponse { orderCode = orderCode, paymentStatus = OrderStatus.SUCCESS, customerIdentifiers = dic };
                }
                else
                {
                    orderResponse = new OrderResponse { paymentStatus = OrderStatus.FAILED };
                }
              //  var orderResponse = new OrderResponse { orderCode = orderCode, paymentStatus = orderSuccess ? OrderStatus.SUCCESS : OrderStatus.FAILED,  };
                result.JsonString = JsonConvert.SerializeObject(orderResponse, new Newtonsoft.Json.Converters.StringEnumConverter());
                return View(result);
            }
        }

        protected async Task SendEmailMessage(SendGridMessage msg)
        {
            var apiKey = ConfigurationManager.AppSettings["EmailApiKey"];
            var client = new SendGridClient(apiKey);
            
            Response response = await client.SendEmailAsync(msg).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                //Get error description from response and log  
                //note these messages won't show on Esendex web interface as they were not sent
                var error = response.Body.ReadAsStringAsync();
                var errorText = error.Result;
                Trace.TraceError($"Error Sending Email.  Error Result: {errorText} Status: {response.StatusCode}", null);
                // throw new SmsMessageException($"Status: {response.StatusCode}  Error Sending SMS message due to {errorText} ");
            }
        }


    }
}