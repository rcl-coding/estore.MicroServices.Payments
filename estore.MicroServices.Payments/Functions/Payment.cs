#nullable disable
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using estore.MicroServices.Payments.BackgroundWorker;
using estore.MicroServices.Payments.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RCL.WebHook.DatabaseContext;

namespace estore.MicroServices.Payments.Functions
{
    public class Payment
    {
        private readonly ILogger _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private static HttpClient _httpClient;

        public IBackgroundTaskQueue Queue { get; }

        static Payment()
        {
            _httpClient = new HttpClient();
        }

        public Payment(ILoggerFactory loggerFactory,
            IServiceScopeFactory serviceScopeFactory,
            IBackgroundTaskQueue queue )
        {
            _logger = loggerFactory.CreateLogger<Payment>();
            _serviceScopeFactory = serviceScopeFactory;
            Queue = queue;
        }


        [Function("Payment_ReceiveMessage_V1")]
        public async Task<HttpResponseData> RunReceiveMessageV1([HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/payment/message")] HttpRequestData req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (!string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogInformation($"Received message : {requestBody}");

                    Queue.QueueBackgroundWorkItem(async token =>
                    {
                        using (var scope = _serviceScopeFactory.CreateScope())
                        {
                            var scopedServices = scope.ServiceProvider;
                            var _webhookDb = scopedServices.GetRequiredService<WebHookDbContext>();

                            try
                            {
                                string paymentReplyUrl = Environment.GetEnvironmentVariable("PaymentProvider:ReplyUrl");
                                string url = $"{paymentReplyUrl}?cmd=_notify-validate&{requestBody}";
                                var resp = await _httpClient.GetAsync(url);
                                string valid = await resp.Content.ReadAsStringAsync();

                                if (valid == "VERIFIED")
                                {
                                    var qryStr = HttpUtility.ParseQueryString(requestBody);
                                    var dict = qryStr.AllKeys.ToDictionary(o => o, o => qryStr[o]);
                                    string json = JsonSerializer.Serialize(dict);

                                    PaymentMessage message = JsonSerializer.Deserialize<PaymentMessage>(json);

                                    if (message.receiver_email == Environment.GetEnvironmentVariable("PaymentProvider:ReceiverEmail"))
                                    {
                                        List<WebHookSubscription> webHooks = await _webhookDb.WebHookSubscriptions
                                        .Where(w => w.EventSubscribed == "payment-message-received")
                                        .ToListAsync();

                                        if (webHooks?.Count > 0)
                                        {
                                            foreach (var webhook in webHooks)
                                            {
                                                await CallWebHookUrlAsync(webhook, json);
                                            }
                                        }
                                    }
                                }

                            }
                            catch (Exception)
                            {
                            }
                        }
                    });

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    _logger.LogInformation("Responded OK to payment message sender");
                    return response;
                }
                else
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    response.Headers.Add("Content-Type", "text/plain ; charset=utf-8");
                    string err = "The message was not received in the body of the request";
                    response.WriteString(err);
                    _logger.LogError($"{err}");
                    return response;
                }
            }
            catch (Exception ex)
            {
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                response.Headers.Add("Content-Type", "text/plain ; charset=utf-8");
                response.WriteString(ex.Message);
                _logger.LogError($"{ex.Message}");
                return response;
            }
        }

        private async Task CallWebHookUrlAsync(WebHookSubscription webhook, string json)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Add(webhook.BasicAuthUsername, webhook.BasicAuthPassword);

                var webhookResponse = await _httpClient.PostAsync($"{webhook.WebHookUrl}", new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch (Exception)
            {
            }
        }
    }
}
