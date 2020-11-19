using Newtonsoft.Json;
using SalesTabletAppPayments.Helper;
using SalesTabletAppPayments.Models;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
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

        public ActionResult FinishPayment(ThreeDSModel model)
        {
            // Requires form post, comes as a redirect from bank, this is set as "TermUrl" as per WP docs
            //PaRes
            //MD (shopperSessionId)
            Trace.WriteLine($"Called back to FinishPayment");
            PaymentResult result = new PaymentResult();
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

                string orderCode = paymentModel.OrderCode;

                var responseCode = this.HttpContext.Request.Form["PaRes"];
                var httpRequest = this.HttpContext.Request;

                ThreeDSecureInfo threeDSInfo = new ThreeDSecureInfo()
                {
                    shopperIpAddress = paymentModel.ShopperIpAddress,
                    shopperSessionId = paymentModel.ShopperSessionId,
                    shopperUserAgent = paymentModel.ShopperUserAgent,
                    shopperAcceptHeader = paymentModel.ShopperAcceptHeader
                };

                string key = ConfigurationManager.AppSettings["WorldPayKey"];
                WorldpayRestClient restClient = new WorldpayRestClient("https://api.worldpay.com/v1", key);

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
                catch (Exception exc)
                {
                    result.IsSuccess = false;
                    Trace.TraceError("Worldpay Authorize Exception {0} {1}", exc, this);
                    throw exc;
                }
            }
            catch (Exception x)
            {
                Trace.TraceError("Worldpay Authorize Exception {0} {1}", x, this);
                result.IsSuccess = false;
                return new HttpStatusCodeResult(HttpStatusCode.InternalServerError);
            }
        }
    }
}