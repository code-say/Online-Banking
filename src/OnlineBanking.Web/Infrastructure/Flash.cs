using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace OnlineBanking.Web.Infrastructure;

/// <summary>One-time messages shown after a redirect (post/redirect/get).</summary>
public static class FlashExtensions
{
    public const string SuccessKey = "flash.success";
    public const string ErrorKey = "flash.error";

    public static void Flash(this ITempDataDictionary temp, string message) => temp[SuccessKey] = message;

    public static void FlashError(this ITempDataDictionary temp, string message) => temp[ErrorKey] = message;
}
