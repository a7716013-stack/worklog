using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace WorkJournal.Web.Models;

public sealed class WorkDateRangeAttribute : ValidationAttribute, IClientModelValidator
{
    public WorkDateRangeAttribute() : base("日期須介於 2000 至 2100 年。") { }
    public override bool IsValid(object? value) =>
        value is DateOnly date && date >= new DateOnly(2000, 1, 1) && date <= new DateOnly(2100, 12, 31);
    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-workdate", ErrorMessage!);
        context.Attributes.TryAdd("data-val-workdate-min", "2000-01-01");
        context.Attributes.TryAdd("data-val-workdate-max", "2100-12-31");
    }
}

