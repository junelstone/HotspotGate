using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace StonesHotspot.Web.Admin;

/// <summary>One-time success/error messages shown after a redirect.</summary>
public static class Flash
{
    public const string SuccessKey = "flash.success";
    public const string ErrorKey = "flash.error";

    public static void Success(this ITempDataDictionary tempData, string message) => tempData[SuccessKey] = message;
    public static void Error(this ITempDataDictionary tempData, string message) => tempData[ErrorKey] = message;
}
