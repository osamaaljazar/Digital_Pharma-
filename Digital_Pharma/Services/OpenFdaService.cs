using Digital_Pharma.Models;
using System.Text.Json;

namespace Digital_Pharma.Services
{
    public class OpenFdaService
    {
        private readonly HttpClient _httpClient;

        public OpenFdaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<OpenFdaResponse?> GetDrugDataAsync(string drugName)
        {
            if (string.IsNullOrEmpty(drugName)) return null;

            string encodedDrugName = Uri.EscapeDataString(drugName);
            string url = $"https://api.fda.gov/drug/label.json?search=(openfda.brand_name:\"{encodedDrugName}\"+openfda.generic_name:\"{encodedDrugName}\")&limit=1";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<OpenFdaResponse>(jsonString, options);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching {drugName}: {ex.Message}");
            }
            return null;
        }

        // ── Autocomplete suggestions ───────────────────────────────────────────
        public async Task<List<string>> GetDrugSuggestionsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<string>();

            string encoded = Uri.EscapeDataString(query);
            string url = $"https://api.fda.gov/drug/label.json?search=openfda.brand_name:{encoded}*+openfda.generic_name:{encoded}*&limit=8";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new List<string>();

                var jsonString = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<OpenFdaResponse>(jsonString, options);

                if (data?.Results == null) return new List<string>();

                var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in data.Results)
                {
                    if (r.OpenFda?.BrandName != null)
                        foreach (var n in r.OpenFda.BrandName) suggestions.Add(n);
                    if (r.OpenFda?.GenericName != null)
                        foreach (var n in r.OpenFda.GenericName) suggestions.Add(n);
                }

                return suggestions
                    .Where(s => s.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s)
                    .Take(8)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}