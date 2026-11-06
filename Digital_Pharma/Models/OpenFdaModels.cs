using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Digital_Pharma.Models
{
    public class OpenFdaResponse
    {
        [JsonPropertyName("results")]
        public List<OpenFdaResult>? Results { get; set; }
    }

    public class OpenFdaResult
    {
        [JsonPropertyName("drug_interactions")]
        public List<string>? DrugInteractions { get; set; }

        [JsonPropertyName("openfda")]
        public OpenFdaDetails? OpenFda { get; set; }
    }

    public class OpenFdaDetails
    {
        [JsonPropertyName("brand_name")]
        public List<string>? BrandName { get; set; }

        [JsonPropertyName("generic_name")]
        public List<string>? GenericName { get; set; }
    }
}
