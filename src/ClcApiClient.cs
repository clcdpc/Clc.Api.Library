using Clc.Api.Models;
using Clc.Rest;
using Clc.Rest.Auth;
using Clc.Rest.Models;

namespace Clc.Api.Library
{
    public class ClcApiClient : RestClient
    {
        public ClcApiClient(string baseUrl, string apiKey)
        {
            BaseUrl = baseUrl;
            Authenticator = new HeaderApiKeyAuthenticator(apiKey, "x-api-key");
        }

        internal ClcApiClient(string baseUrl, string apiKey, HttpClient httpClient) : base(baseUrl, httpClient)
        {
            Authenticator = new HeaderApiKeyAuthenticator(apiKey, "x-api-key");
        }

        public async Task<IRestResponse<string>> GetIpAsync()
        {
            var url = "/ip";
            return await ExecuteAsync<string>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<ItemInfo>> GetItemAsync(string id)
        {
            var url = $"/item/{id}";
            return await ExecuteAsync<ItemInfo>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, bool includeLibrary = false) =>
            await GetInTransitItemsAsync(sendingBranchId, [], includeLibrary);

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, IEnumerable<string> itemBarcodesToCheck, bool includeLibrary = false)
        {
            var url = $"/items/in-transit/{sendingBranchId}?includeLibrary={includeLibrary}";
            return await ExecuteAsync<List<InTransitItem>>(RestRequest.Post(url, itemBarcodesToCheck));
        }

        public async Task<IRestResponse<GetPatronHoldsResult>> GetPatronHoldsAsync(string barcode)
        {
            var url = $"/patron/{barcode}/holds";
            return await ExecuteAsync<GetPatronHoldsResult>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<List<PatronHeldItem>>> GetPatronHeldItemsAsync(string barcode)
        {
            var url = $"/patron/{barcode}/held-items";
            return await ExecuteAsync<List<PatronHeldItem>>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<IEnumerable<BibInfo>>> GetHoldingsAsync(params int[] bibIds)
        {
            var url = $"/bib/holdings?{string.Join("&", bibIds.Select(b => $"bibid={b}"))}";
            return await ExecuteAsync<IEnumerable<BibInfo>>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<RemoveSmsDetailsResult>> RemovePatronSmsDetailsAsync(string barcode, string note)
        {
            var url = $"/patron/{barcode}/sms";
            return await ExecuteAsync<RemoveSmsDetailsResult>(RestRequest.Create(HttpMethod.Delete, url, new RemoveSmsDetailsData(note)));
        }

        public async Task<IRestResponse<List<Patron>>> GetPatronsForSmsNumberAsync(string phone)
        {
            var url = $"/sms-numbers/{phone}/patrons";
            return await ExecuteAsync<List<Patron>>(RestRequest.Get(url));
        }

        public async Task<IRestResponse<AddNonBlockingNoteResult>> AddNonBlockingNoteAsync(string barcode, string note)
        {
            var url = $"/patron/{barcode}/notes/non-blocking";
            return await ExecuteAsync<AddNonBlockingNoteResult>(RestRequest.Post(url, new AddNonBlockingNoteData(note)));
        }
    }
}
