using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using RestSharp;

namespace Microsoft.Bot.Sample.BootCamp.Actions
{
    public static class ActionBuilder
    {
        private static readonly string TenantId = ConfigurationManager.AppSettings["AzureTenant"];

        public static async Task<List<IAzureItem>> ListResourceGroups(string subscriptionId, string applicationId, string secretKey)
        {
            var result = await ExecuteRest<List<AzureResourceGroups>>(subscriptionId, applicationId, secretKey, "resourcegroups?api-version=2018-02-01", Method.GET, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });

            return new List<IAzureItem>(result);
        }

        public static async Task<List<IAzureItem>> ListResources(string subscriptionId, string applicationId, string secretKey)
        {
            var result = await ExecuteRest<List<AzureResource>>(subscriptionId, applicationId, secretKey, "resources?api-version=2018-02-01", Method.GET, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });

            return new List<IAzureItem>(result);
        }

        public static async Task<bool> StartStopResource(string subscriptionId, string applicationId, string secretKey, string resourceGroupName, string resourceName, string operation)
        {
            var resource = $"resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{resourceName}/{operation}?api-version=2016-08-01";

            var result = await ExecuteRest<bool>(subscriptionId, applicationId, secretKey, resource, Method.POST, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });

            return result;
        }

        public static async Task<bool> CreateWebApp(string subscriptionId, string applicationId, string secretKey, string resourceGroupName, string resourceName)
        {
            var resource = $"resourceGroups/{resourceGroupName}/providers/Microsoft.Web/sites/{resourceName}?api-version=2016-08-01";

            var body = new
            {
                kind = "app",
                location = "westus",
                properties = new
                {
                    clientAffinityEnabled = false,
                    clientCertEnabled = false
                }

            };

            var result = await ExecuteRest<bool>(subscriptionId, applicationId, secretKey, resource, Method.PUT, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted }, body);

            return result;
        }

        public static async Task<bool> StartDeallocateVm(string subscriptionId, string applicationId, string secretKey, string resourceGroupName, string resourceName, string operation)
        {
            var resource = $"resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/{resourceName}/{operation}?api-version=2017-12-01";

            var result = await ExecuteRest<bool>(subscriptionId, applicationId, secretKey, resource, Method.POST, new[] { HttpStatusCode.OK, HttpStatusCode.Accepted });

            return result;
        }

        private static async Task<T> ExecuteRest<T>(string subscriptionId, string applicationId, string secretKey, string resource, Method method, HttpStatusCode[] acceptedResult, object body = null)
        {
            var urlBase = "https://management.azure.com/subscriptions";

            var token = await GetAccessToken(applicationId, secretKey);

            var client = new RestClient(urlBase);

            var request = new RestRequest($"{subscriptionId}/{resource}", method);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Content-Type", "application/json");

            if (body != null)
                request.AddJsonBody(body);

            var result = client.Execute(request);


            if (!acceptedResult.Contains(result.StatusCode)) return default(T);

            if (typeof(T) == typeof(bool)) return (T)(object)true;


            var resultData = JsonConvert.DeserializeObject(result.Content);
            var data = ((Newtonsoft.Json.Linq.JObject)resultData)["value"].ToObject<T>();
            return data;

        }

        private static async Task<string> GetAccessToken(string applicationId, string secretKey)
        {
            var authContextUrl = "https://login.windows.net/" + TenantId;
            var authenticationContext = new AuthenticationContext(authContextUrl);
            var credential = new ClientCredential(applicationId, secretKey);
            var result = await authenticationContext.AcquireTokenAsync("https://management.azure.com/", credential);

            if (result == null)
                throw new InvalidOperationException("Failed to obtain the JWT token");

            var token = result.AccessToken;
            return token;
        }

    }


    public interface IAzureItem
    {
        string Id { get; set; }
        string Name { get; set; }
    }

    public class AzureResourceGroups : IAzureItem
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class AzureResource : IAzureItem
    {
        private string _type;

        public string Id { get; set; }
        public string Name { get; set; }

        public string ResrouceGroupName
        {
            get
            {
                var itens = Id.Split('/').ToList();
                var index = itens.FindIndex(i => i.Trim().ToUpper() == "RESOURCEGROUPS");
                return itens[index + 1];
            }
        }

        public string Type
        {
            set => _type = value;
            get => _type.Split('/').Last();
        }

        public override string ToString()
        {
            return $"{Type} - {Name}";
        }
    }
}