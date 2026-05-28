using Clc.Api.Models;
using Clc.Rest;
using Clc.Rest.Auth;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Clc.Api.Library.Tests")]

namespace Clc.Api.Library
{
    public class ClcApiClient : RestClient
    {
        public ClcApiClient(string baseUrl, string apiKey)
        {
            BaseUrl = baseUrl;
            Authenticator = new HeaderApiKeyAuthenticator(apiKey, "x-api-key");
        }

        internal ClcApiClient(string baseUrl, string apiKey, HttpClient client) : base(client)
        {
            BaseUrl = baseUrl;
            Authenticator = new HeaderApiKeyAuthenticator(apiKey, "x-api-key");
        }

        public async Task<IRestResponse<string>> GetIpAsync()
        {
            var url = "/ip";
            return await GetAsync<string>(url);
        }

        public async Task<IRestResponse<ItemInfo>> GetItemAsync(string id)
        {
            var url = $"/item/{id}";
            return await GetAsync<ItemInfo>(url);
        }

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, bool includeLibrary = false) =>
            await GetInTransitItemsAsync(sendingBranchId, [], includeLibrary);

        public async Task<IRestResponse<List<InTransitItem>>> GetInTransitItemsAsync(int sendingBranchId, IEnumerable<string> itemBarcodesToCheck, bool includeLibrary = false)
        {
            var url = $"/items/in-transit/{sendingBranchId}?includeLibrary={includeLibrary}";
            return await PostAsync<List<InTransitItem>>(url, itemBarcodesToCheck);
        }

        public async Task<IRestResponse<GetPatronHoldsResult>> GetPatronHoldsAsync(string barcode)
        {
            var url = $"/patron/{barcode}/holds";
            return await GetAsync<GetPatronHoldsResult>(url);
        }

        public async Task<IRestResponse<List<PatronHeldItem>>> GetPatronHeldItemsAsync(string barcode)
        {
            var url = $"/patron/{barcode}/held-items";
            return await GetAsync<List<PatronHeldItem>>(url);
        }

        public async Task<IRestResponse<IEnumerable<BibInfo>>> GetHoldingsAsync(params int[] bibIds)
        {
            var url = $"/bib/holdings?{string.Join("&", bibIds.Select(b => $"bibid={b}"))}";
            return await GetAsync<IEnumerable<BibInfo>>(url);
        }

        public async Task<IRestResponse<RemoveSmsDetailsResult>> RemovePatronSmsDetailsAsync(string barcode, string note)
        {
            var url = $"/patron/{barcode}/sms";
            return await DeleteAsync<RemoveSmsDetailsResult>(url, new RemoveSmsDetailsData(note));
        }

        public async Task<IRestResponse<List<Patron>>> GetPatronsForSmsNumberAsync(string phone)
        {
            var url = $"/sms-numbers/{phone}/patrons";
            return await GetAsync<List<Patron>>(url);
        }

        public async Task<IRestResponse<AddNonBlockingNoteResult>> AddNonBlockingNoteAsync(string barcode, string note)
        {
            var url = $"/patron/{barcode}/notes/non-blocking";
            return await PostAsync<AddNonBlockingNoteResult>($"/patron/{barcode}/notes/non-blocking", new AddNonBlockingNoteData(note));
        }
    }
}
