using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Digital_Pharma.Models;
namespace Digital_Pharma.Services;
public interface IDrugLabelClient {
 Task<LabelLookup> SearchAsync(string query,CancellationToken ct=default);
 Task<LabelLookup> ByIdAsync(string id,CancellationToken ct=default);
}
public sealed class DrugLabelClient(HttpClient http):IDrugLabelClient {
 public Task<LabelLookup> SearchAsync(string query,CancellationToken ct=default) {
  if(query.Trim().Length is <2 or >100) return Task.FromResult(new LabelLookup([],"أدخل اسم الدواء بين حرفين و100 حرف."));
  var term=Regex.Replace(query.Trim(),@"[^\p{L}\p{N} \-]"," ");
  var search=$"(openfda.brand_name:\"{term}\" OR openfda.generic_name:\"{term}\" OR openfda.substance_name:\"{term}\")";
  return Fetch(search,8,ct);
 }
 public Task<LabelLookup> ByIdAsync(string id,CancellationToken ct=default)=>
  Guid.TryParse(id,out var guid) ? Fetch($"id:\"{guid}\"",1,ct) : Task.FromResult(new LabelLookup([],"معرف النشرة غير صالح."));
 private async Task<LabelLookup> Fetch(string search,int limit,CancellationToken ct) {
  try {
   using var response=await http.GetAsync("https://api.fda.gov/drug/label.json?search="+Uri.EscapeDataString(search)+"&limit="+limit,ct);
   if(response.StatusCode==HttpStatusCode.NotFound) return new([],"لم نجد منتجاً مطابقاً؛ جرّب الاسم الإنجليزي أو المادة الفعالة. لا يوجد تطابق مؤكد.");
   if(!response.IsSuccessStatusCode) return new([],"تعذر الاتصال بمصدر النشرات. حاول لاحقاً؛ لم يكتمل التقييم.");
   using var stream=await response.Content.ReadAsStreamAsync(ct);
   using var doc=await JsonDocument.ParseAsync(stream,cancellationToken:ct);
   var labels=doc.RootElement.GetProperty("results").Deserialize<List<DrugLabel>>()??[];
   return new(labels.Where(l=>l!=null && Guid.TryParse(l.Id,out _)).Select(l=>l.Normalize()).ToList());
  } catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) {
   return new([],"تعذر تحميل بيانات موثوقة للدواء؛ لم يكتمل التقييم.");
  }
 }
}

