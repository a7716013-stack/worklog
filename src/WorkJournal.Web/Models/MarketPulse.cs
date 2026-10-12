using System.ComponentModel.DataAnnotations;
namespace WorkJournal.Web.Models;
public record PulseStock(string Symbol,string Name,string Industry,decimal Close,decimal? ChangePercent,decimal? High,decimal? Amount);
public record PulseMarket(string Market,DateOnly Date,decimal? Index,decimal? IndexChange,int? Up,int? Down,int? Flat,int? LimitUp,int? LimitDown,PulseStock[] Stocks)
{
 public decimal? IndexPercent=>Index-IndexChange>0?IndexChange/(Index-IndexChange)*100:null;
 public decimal? Turnover=>Stocks.Length>0&&Stocks.All(x=>x.Amount>=0)?Stocks.Sum(x=>x.Amount):null;
}
public record PulseSnapshot(PulseMarket[] Markets,DateTimeOffset UpdatedAt,string[] Warnings);
public class MarketPulseDaily
{
 public DateOnly TradeDate{get;set;}
 [MaxLength(10)] public string Market{get;set;}="";
 public string Json{get;set;}="{}";
 public DateTimeOffset CapturedAt{get;set;}
}
public class PortfolioDailyValue
{
 public long Id{get;set;}
 public int AccountId{get;set;}
 public Guid Generation{get;set;}
 public DateOnly TradeDate{get;set;}
 public decimal Cash{get;set;}
 public decimal MarketValue{get;set;}
 public decimal TotalAssets{get;set;}
 public decimal InitialCash{get;set;}
 public DateTimeOffset CapturedAt{get;set;}
}
