using System.Text.Json.Serialization;
namespace Digital_Pharma.Models;
public class DrugLabel {
 [JsonPropertyName("id")] public string Id {get;set;}="";
 [JsonPropertyName("set_id")] public string SetId {get;set;}="";
 [JsonPropertyName("effective_time")] public string EffectiveTime {get;set;}="";
 [JsonPropertyName("openfda")] public LabelNames Names {get;set;}=new();
 [JsonPropertyName("contraindications")] public string[] Contraindications {get;set;}=[];
 [JsonPropertyName("warnings")] public string[] Warnings {get;set;}=[];
 [JsonPropertyName("warnings_and_cautions")] public string[] WarningsAndCautions {get;set;}=[];
 [JsonPropertyName("pregnancy")] public string[] Pregnancy {get;set;}=[];
 [JsonPropertyName("pregnancy_or_breast_feeding")] public string[] PregnancyOrBreastFeeding {get;set;}=[];
 [JsonPropertyName("drug_interactions")] public string[] Interactions {get;set;}=[];
 [JsonPropertyName("active_ingredient")] public string[] ActiveIngredient {get;set;}=[];
 [JsonPropertyName("indications_and_usage")] public string[] Indications {get;set;}=[];
 public DrugLabel Normalize() {
  static string[] Clean(string[]? values)=>(values??[]).Where(v=>!string.IsNullOrWhiteSpace(v)).ToArray();
  Names??=new(); Names.Brand=Clean(Names.Brand);Names.Generic=Clean(Names.Generic);Names.Substances=Clean(Names.Substances);
  Names.Manufacturers=Clean(Names.Manufacturers);Names.Routes=Clean(Names.Routes);Names.Ndc=Clean(Names.Ndc);
  Contraindications=Clean(Contraindications);Warnings=Clean(Warnings);WarningsAndCautions=Clean(WarningsAndCautions);
  Pregnancy=Clean(Pregnancy);PregnancyOrBreastFeeding=Clean(PregnancyOrBreastFeeding);Interactions=Clean(Interactions);
  ActiveIngredient=Clean(ActiveIngredient);Indications=Clean(Indications);return this;
 }
 [JsonIgnore] public string DisplayName=>Names.Brand.FirstOrDefault()??Names.Generic.FirstOrDefault()??"منتج دون اسم";
 [JsonIgnore] public string SourceUrl=>Guid.TryParse(SetId,out var guid) ? "https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid="+guid : "https://api.fda.gov/drug/label.json?search=id:"+Uri.EscapeDataString(Id);
}
public class LabelNames {
 [JsonPropertyName("brand_name")] public string[] Brand {get;set;}=[];
 [JsonPropertyName("generic_name")] public string[] Generic {get;set;}=[];
 [JsonPropertyName("substance_name")] public string[] Substances {get;set;}=[];
 [JsonPropertyName("manufacturer_name")] public string[] Manufacturers {get;set;}=[];
 [JsonPropertyName("route")] public string[] Routes {get;set;}=[];
 [JsonPropertyName("product_ndc")] public string[] Ndc {get;set;}=[];
}
public record LabelLookup(List<DrugLabel> Labels,string? Error=null);
public class LabelSearchPage {
 public string Query {get;set;}="";
 public int? MedicationId {get;set;}
 public LabelLookup Lookup {get;set;}=new([]);
}

