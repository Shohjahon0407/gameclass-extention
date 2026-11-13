using Manajoy.GameClass.Bridge.Pricing;

namespace Manajoy.GameClass.Bridge.Setup;

/// <summary>Turns a pricing context into the setup API's answers; pricing errors become UI error codes ("quote_*").</summary>
internal static class CashierService
{
    public static PricingInfo Pricing(PricingContext? context, int pc, Func<DateTime> clock) =>
        Run(() => new CashierDesk(Require(context), clock).Pricing(pc));

    public static QuoteResult Quote(PricingContext? context, QuoteRequest request, Func<DateTime> clock) =>
        Run(() => new CashierDesk(Require(context), clock).Quote(request));

    private static PricingContext Require(PricingContext? context) =>
        context ?? throw new SetupException("pricing_unavailable");

    private static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (QuoteException ex)
        {
            throw new SetupException("quote_" + ex.Code, ex.Message == ex.Code ? null : ex.Message);
        }
    }
}
