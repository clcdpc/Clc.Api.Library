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

        public async Task<IRestResponse<string>> GetIpAsync() =>
            await ExecuteAsync<string>(CreateGetIpRequest());

        public async Task<IRestResponse<ItemInfo>> GetItemAsync(string id) =>
            await ExecuteAsync<ItemInfo>(CreateGetItemRequest(id));

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, bool includeLibrary = false) =>
            await GetInTransitItemsAsync(sendingBranchId, [], includeLibrary);

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, IEnumerable<string> itemBarcodesToCheck, bool includeLibrary = false) =>
            await ExecuteAsync<List<InTransitItem>>(CreateGetInTransitItemsRequest(sendingBranchId, itemBarcodesToCheck, includeLibrary));

        public async Task<IRestResponse<GetPatronHoldsResult>> GetPatronHoldsAsync(string barcode) =>
            await ExecuteAsync<GetPatronHoldsResult>(CreateGetPatronHoldsRequest(barcode));

        public async Task<IRestResponse<List<PatronHeldItem>>> GetPatronHeldItemsAsync(string barcode) =>
            await ExecuteAsync<List<PatronHeldItem>>(CreateGetPatronHeldItemsRequest(barcode));

        public async Task<IRestResponse<IEnumerable<BibInfo>>> GetHoldingsAsync(params int[] bibIds) =>
            await ExecuteAsync<IEnumerable<BibInfo>>(CreateGetHoldingsRequest(bibIds));

        public async Task<IRestResponse<RemoveSmsDetailsResult>> RemovePatronSmsDetailsAsync(string barcode, string note) =>
            await ExecuteAsync<RemoveSmsDetailsResult>(CreateRemovePatronSmsDetailsRequest(barcode, note));

        public async Task<IRestResponse<List<Patron>>> GetPatronsForSmsNumberAsync(string phone) =>
            await ExecuteAsync<List<Patron>>(CreateGetPatronsForSmsNumberRequest(phone));

        public async Task<IRestResponse<AddNonBlockingNoteResult>> AddNonBlockingNoteAsync(string barcode, string note) =>
            await ExecuteAsync<AddNonBlockingNoteResult>(CreateAddNonBlockingNoteRequest(barcode, note));

        internal static RestRequest CreateGetIpRequest() =>
            RestRequest.Get("/ip");

        internal static RestRequest CreateGetItemRequest(string id) =>
            RestRequest.Get($"/item/{id}");

        internal static RestRequest CreateGetInTransitItemsRequest(int sendingBranchId, IEnumerable<string> itemBarcodesToCheck, bool includeLibrary = false) =>
            RestRequest.Post($"/items/in-transit/{sendingBranchId}?includeLibrary={includeLibrary}", itemBarcodesToCheck);

        internal static RestRequest CreateGetPatronHoldsRequest(string barcode) =>
            RestRequest.Get($"/patron/{barcode}/holds");

        internal static RestRequest CreateGetPatronHeldItemsRequest(string barcode) =>
            RestRequest.Get($"/patron/{barcode}/held-items");

        internal static RestRequest CreateGetHoldingsRequest(params int[] bibIds) =>
            RestRequest.Get($"/bib/holdings?{string.Join("&", bibIds.Select(b => $"bibid={b}"))}");

        internal static RestRequest CreateRemovePatronSmsDetailsRequest(string barcode, string note) =>
            RestRequest.Create(HttpMethod.Delete, $"/patron/{barcode}/sms", new RemoveSmsDetailsData(note));

        internal static RestRequest CreateGetPatronsForSmsNumberRequest(string phone) =>
            RestRequest.Get($"/sms-numbers/{phone}/patrons");

        internal static RestRequest CreateAddNonBlockingNoteRequest(string barcode, string note) =>
            RestRequest.Post($"/patron/{barcode}/notes/non-blocking", new AddNonBlockingNoteData(note));
    }
}
