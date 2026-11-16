using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using VoxCrm.Domain.Entities;
namespace VoxCrm.Web.Models;
public sealed class QuickVisitModel
{
    public Guid SubmissionId {get;set;}=Guid.NewGuid();
    public Guid PatientId {get;set;}
    public Guid OwnerId {get;set;}
    [Required, StringLength(4000)] public string Notes {get;set;}="";
    [StringLength(20)] public string? Weight {get;set;}
    [StringLength(20)] public string? Amount {get;set;}
    [StringLength(20)] public string? Paid {get;set;}
    public string PaymentMethod {get;set;}="";
    public DateTime VisitDate {get;set;}=DateTime.Today;
    public DateOnly DueDate {get;set;}=DateOnly.FromDateTime(DateTime.Today);
    public List<Guid> VaccineIds {get;set;}=[];
    [StringLength(2000)] public string? HomeSteps {get;set;}
    [StringLength(1000)] public string? CallInstructions {get;set;}
    public DateOnly? FollowUpDate {get;set;}
    [BindNever,ValidateNever] public List<Patient> Patients {get;set;}=[];
    [BindNever,ValidateNever] public List<VaccineType> Types {get;set;}=[];
}
public sealed record QuickVisitPreview(QuickVisitModel Model,string Patient,string Owner,string Phone,List<string> Vaccines,decimal? Weight,decimal Amount,decimal Paid);
